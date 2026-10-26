using System.Net;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using StackExchange.Redis;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace URPOnline.Delivery;

// Puntos de entrada de las Lambdas de la base (infra/fase2-delivery.yaml). Solo el CAMINO FELIZ.
public class Funciones
{
    private static readonly Lazy<Infra> InfraCompartida = new(Infra.DesdeEntorno);
    private readonly Lazy<Infra> _infra;
    private Infra I => _infra.Value;
    private Datos D => new(I.Db);
    private const string ClaveGeo = "repartidores:geo";

    public Funciones() => _infra = InfraCompartida;
    public Funciones(Infra infra) => _infra = new(infra);

    // POST /v1/pedidos — síncrona: guarda el pedido, lo deja en la cola y responde 202 al instante.
    public async Task<APIGatewayProxyResponse> RecibirPedido(APIGatewayProxyRequest req, ILambdaContext ctx)
    {
        var sub = Sub(req);
        NuevoPedido? p;
        try { p = JsonSerializer.Deserialize<NuevoPedido>(req.Body ?? "", Infra.Json); }
        catch (JsonException) { p = null; }
        if (Reglas.Validar(p) is string motivo) return Problema(400, motivo);
        var pedido = await D.CrearPedidoAsync(sub, p!);
        if (pedido is null) return Problema(400, "Algún plato no es de ese restaurante o no está disponible.");
        await I.EncolarAsync("Colas__PedidosRecibidos", new MensajePedido(pedido.Id, 1));
        await I.PublicarAsync("PedidoRecibido", new { pedidoId = pedido.Id, total = pedido.Total });
        ctx.Logger.LogInformation(JsonSerializer.Serialize(new { evento = "PedidoRecibido", pedidoId = pedido.Id }));
        return Respuesta(202, pedido);
    }

    // GET /v1/pedidos/{id} y GET /v1/ofertas.
    public async Task<APIGatewayProxyResponse> Consultas(APIGatewayProxyRequest req, ILambdaContext ctx)
    {
        if (req.Resource == "/v1/ofertas")
            return Respuesta(200, await D.OfertasAsync(await D.RepartidorAsync(Sub(req))));
        if (!int.TryParse(Valor(req.PathParameters, "id"), out var id)) return Problema(400, "Id de pedido inválido.");
        var pedido = await D.PedidoAsync(id);
        return pedido is null ? Problema(404, "El pedido no existe.") : Respuesta(200, pedido);
    }

    // POST /v1/pedidos/{id}/aceptar — síncrona: el repartidor necesita saber en el momento si quedó asignado.
    public async Task<APIGatewayProxyResponse> AceptarPedido(APIGatewayProxyRequest req, ILambdaContext ctx)
    {
        if (!int.TryParse(Valor(req.PathParameters, "id"), out var id)) return Problema(400, "Id de pedido inválido.");
        var repartidor = await D.RepartidorAsync(Sub(req));
        var pedido = await D.AceptarAsync(id, repartidor);
        if (pedido is null) return Problema(404, "El pedido no existe.");
        await D.MarcarDisponibleAsync(repartidor, false);
        await I.PublicarAsync("PedidoAceptado", new { pedidoId = id, repartidorId = repartidor });
        return Respuesta(200, pedido);
    }

    // POST /v1/sincronizar (cambios del celular → cola FIFO) y GET /v1/sincronizar (estado actual completo).
    public async Task<APIGatewayProxyResponse> Sincronizar(APIGatewayProxyRequest req, ILambdaContext ctx)
    {
        var sub = Sub(req);
        if (req.HttpMethod == "GET") return Respuesta(200, await D.PedidosDelRepartidorAsync(await D.RepartidorAsync(sub)));
        LoteDeCambios? lote;
        try { lote = JsonSerializer.Deserialize<LoteDeCambios>(req.Body ?? "", Infra.Json); }
        catch (JsonException) { lote = null; }
        if (Reglas.Validar(lote) is string motivo) return Problema(400, motivo);
        foreach (var c in lote!.Cambios)
            // Base: deduplicación por un id nuevo en cada envío. Usar la clave que manda el celular (idempotencia) es B3.
            await I.EncolarAsync("Colas__CambiosEntrega", new MensajeCambio(c.PedidoId, c.Estado, sub, c.HoraDispositivo),
                grupo: c.PedidoId.ToString(), dedup: Guid.NewGuid().ToString("N"));
        return Respuesta(202, new { recibido = true });
    }

    // pedidos-recibidos → ofrece el pedido a los repartidores disponibles dentro de un radio fijo (Redis GEO).
    public Task<SQSBatchResponse> OfrecerPedido(SQSEvent evt, ILambdaContext ctx) => PorLote(evt, ctx, async m =>
    {
        var msg = JsonSerializer.Deserialize<MensajePedido>(m.Body, Infra.Json)!;
        await OfrecerAsync(msg.PedidoId, msg.Intento, ctx);
    });

    // pedidos-reofrecer (con retraso) → si nadie aceptó, vuelve a ofrecer con el MISMO radio (ampliarlo es A4).
    public Task<SQSBatchResponse> ReofrecerPedido(SQSEvent evt, ILambdaContext ctx) => PorLote(evt, ctx, async m =>
    {
        var msg = JsonSerializer.Deserialize<MensajePedido>(m.Body, Infra.Json)!;
        var pedido = await D.PedidoAsync(msg.PedidoId);
        if (pedido?.Estado == "ofrecido") await OfrecerAsync(msg.PedidoId, msg.Intento, ctx);
    });

    private async Task OfrecerAsync(int pedidoId, int intento, ILambdaContext ctx)
    {
        var origen = await D.RestauranteDelPedidoAsync(pedidoId);
        if (origen is null) return;
        var cerca = await I.Redis.Value.GetDatabase().GeoSearchAsync(ClaveGeo, origen.Value.Lon, origen.Value.Lat,
            new GeoSearchCircle(Reglas.RadioKm, GeoUnit.Kilometers), count: 20, order: Order.Ascending);
        var ids = cerca.Select(c => int.Parse(c.Member.ToString())).ToList();
        var disponibles = ids.Count == 0 ? [] : await D.FiltrarDisponiblesAsync(ids);
        if (disponibles.Count > 0) await D.OfrecerAsync(pedidoId, disponibles, intento);
        ctx.Logger.LogInformation(JsonSerializer.Serialize(new { evento = "PedidoOfrecido", pedidoId, intento, repartidores = disponibles }));
        if (intento < Reglas.MaxIntentos)
            await I.EncolarAsync("Colas__PedidosReofrecer", new MensajePedido(pedidoId, intento + 1), Reglas.RetrasoReofrecerSeg);
    }

    // cambios-entrega.fifo → aplica recogido / entregado.
    public Task<SQSBatchResponse> ActualizarEntrega(SQSEvent evt, ILambdaContext ctx) => PorLote(evt, ctx, async m =>
    {
        var c = JsonSerializer.Deserialize<MensajeCambio>(m.Body, Infra.Json)!;
        var pedido = await D.CambiarEstadoAsync(c.PedidoId, c.Estado);
        if (pedido?.Estado == "entregado")
        {
            await D.MarcarDisponibleAsync(pedido.RepartidorId, true);
            await I.PublicarAsync("PedidoEntregado", new { pedidoId = pedido.Id, repartidorId = pedido.RepartidorId });
        }
    });

    // posiciones.fifo → última posición en Redis (el mapa y la búsqueda de cercanos leen de ahí), historial en RDS
    // y aviso por WebSocket a quien sigue ese pedido.
    public Task<SQSBatchResponse> RegistrarPosicion(SQSEvent evt, ILambdaContext ctx) => PorLote(evt, ctx, async m =>
    {
        var p = JsonSerializer.Deserialize<Posicion>(m.Body, Infra.Json)!;
        var sub = m.MessageAttributes.TryGetValue("repartidor", out var a) ? a.StringValue : m.Attributes.GetValueOrDefault("MessageGroupId");
        var repartidor = await D.RepartidorAsync(sub!);
        var redis = I.Redis.Value.GetDatabase();
        await redis.GeoAddAsync(ClaveGeo, p.Lon, p.Lat, repartidor.ToString());
        await redis.StringSetAsync($"repartidor:{repartidor}:pos", JsonSerializer.Serialize(p, Infra.Json), TimeSpan.FromMinutes(30));
        await D.GuardarPosicionAsync(repartidor, p);
        if (p.PedidoId <= 0) return;
        var ws = I.WebSocket();
        foreach (var conexion in await D.ConexionesDelPedidoAsync(p.PedidoId))
            if (!await I.EnviarAsync(ws, conexion, new { tipo = "posicion", p.PedidoId, p.Lat, p.Lon, p.Secuencia }))
                await D.DesconectarAsync(conexion);
    });

    // WebSocket: $connect (valida el token de Cognito), $disconnect y «seguir» (el cliente sigue SU pedido).
    public async Task<APIGatewayProxyResponse> ConexionWs(APIGatewayProxyRequest req, ILambdaContext ctx)
    {
        var conexion = req.RequestContext.ConnectionId;
        switch (req.RequestContext.RouteKey)
        {
            case "$connect":
                var token = new TokenCognito(I.Config("AWS_REGION"), I.Config("Cognito__UserPoolId"));
                var sub = await token.ValidarAsync(Valor(req.QueryStringParameters, "token"));
                if (sub is null) return new APIGatewayProxyResponse { StatusCode = 401 };
                await D.ConectarAsync(conexion, sub);
                return new APIGatewayProxyResponse { StatusCode = 200 };
            case "$disconnect":
                await D.DesconectarAsync(conexion);
                return new APIGatewayProxyResponse { StatusCode = 200 };
            default:
                var cuerpo = JsonDocument.Parse(req.Body ?? "{}").RootElement;
                var ok = cuerpo.TryGetProperty("pedidoId", out var id) && id.TryGetInt32(out var pedidoId)
                         && await D.SeguirAsync(conexion, pedidoId);
                return new APIGatewayProxyResponse { StatusCode = ok ? 200 : 403 };
        }
    }

    public Task<string> Simulador(EntradaSimulador entrada, ILambdaContext ctx) => new Simulador(I).CorrerAsync(entrada, ctx);

    // ---- utilidades ----
    private static string? Valor(IDictionary<string, string>? d, string clave) =>
        d is not null && d.TryGetValue(clave, out var v) ? v : null;

    private static string Sub(APIGatewayProxyRequest req) =>
        req.RequestContext?.Authorizer?.Claims?.GetValueOrDefault("sub") ?? throw new UnauthorizedAccessException("Sin usuario de Cognito");

    private static APIGatewayProxyResponse Respuesta(int estado, object cuerpo) => new()
    {
        StatusCode = estado,
        Body = JsonSerializer.Serialize(cuerpo, Infra.Json),
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
    };

    private static APIGatewayProxyResponse Problema(int estado, string titulo) => new()
    {
        StatusCode = estado,
        Body = JsonSerializer.Serialize(new { type = "about:blank", title = titulo, status = estado }, Infra.Json),
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/problem+json" },
    };

    // Lote de SQS con REPORTE DE FALLAS PARCIALES: solo vuelve a la cola el mensaje que falló.
    private static async Task<SQSBatchResponse> PorLote(SQSEvent evt, ILambdaContext ctx, Func<SQSEvent.SQSMessage, Task> procesar)
    {
        var fallidos = new List<SQSBatchResponse.BatchItemFailure>();
        foreach (var m in evt.Records ?? [])
        {
            try { await procesar(m); }
            catch (Exception e)
            {
                ctx.Logger.LogError($"Fallo {m.MessageId}: {e.GetType().Name}: {e.Message}");
                fallidos.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = m.MessageId });
            }
        }
        return new SQSBatchResponse { BatchItemFailures = fallidos };
    }
}
