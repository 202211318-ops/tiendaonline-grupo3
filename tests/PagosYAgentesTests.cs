using Amazon.Lambda.SQSEvents;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TiendaOnline.Data;
using TiendaOnline.Models;
using TiendaOnline.Services.Agents;
using TiendaOnline.Services.Payments;
using Xunit;

namespace TiendaOnline.Tests;

// --- Firma del webhook de Mercado Pago (sin base de datos) ---
public class FirmaMercadoPagoTests
{
    private const string Secreto = "secreto-de-prueba";
    private static readonly DateTimeOffset Ahora = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);

    [Fact]
    public void Acepta_una_firma_correcta()
    {
        var sig = MercadoPagoFirma.Firmar("123456", "req-1", Ahora.ToUnixTimeSeconds(), Secreto);
        Assert.True(MercadoPagoFirma.EsValida(sig, "req-1", "123456", Secreto, Ahora));
    }

    [Theory]
    [InlineData("otro-secreto", "req-1", "123456")]   // firmado con otra clave
    [InlineData(Secreto, "req-2", "123456")]          // otro request-id
    [InlineData(Secreto, "req-1", "999999")]          // otro pago
    public void Rechaza_si_cambia_cualquier_dato(string secreto, string requestId, string dataId)
    {
        var sig = MercadoPagoFirma.Firmar("123456", "req-1", Ahora.ToUnixTimeSeconds(), Secreto);
        Assert.False(MercadoPagoFirma.EsValida(sig, requestId, dataId, secreto, Ahora));
    }

    [Fact]
    public void Rechaza_un_aviso_viejo_reenviado()
    {
        var sig = MercadoPagoFirma.Firmar("123456", "req-1", Ahora.AddHours(-1).ToUnixTimeSeconds(), Secreto);
        Assert.False(MercadoPagoFirma.EsValida(sig, "req-1", "123456", Secreto, Ahora));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("basura")]
    [InlineData("ts=1760000000,v1=zz")]
    public void Rechaza_cabeceras_vacias_o_mal_formadas(string? sig) =>
        Assert.False(MercadoPagoFirma.EsValida(sig, "req-1", "123456", Secreto, Ahora));

    [Fact]
    public void La_lambda_arma_el_mensaje_del_webhook_desde_los_atributos_de_SQS()
    {
        var msg = new SQSEvent.SQSMessage
        {
            Body = """{"type":"payment","data":{"id":"77"}}""",
            MessageAttributes = new()
            {
                ["origen"] = new() { StringValue = "webhook-pago" },
                ["xSignature"] = new() { StringValue = "ts=1,v1=ab" },
                ["xRequestId"] = new() { StringValue = "ninguno" },   // cabecera ausente
                ["dataId"] = new() { StringValue = "77" },
            },
        };
        var json = URPOnline.Agentes.Handler.MensajeWebhook(msg);
        Assert.Contains("\"xSignature\":\"ts=1,v1=ab\"", json);
        Assert.Contains("\"xRequestId\":null", json);
        Assert.Contains("\"dataId\":\"77\"", json);
        Assert.Contains("\"type\":\"payment\"", json);
    }
}

// --- Con PostgreSQL real (pgvector) en un contenedor: webhook, limite de correos y agentes programados ---
public sealed class BaseDeDatosFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("pgvector/pgvector:pg16").Build();
    public string Cadena => _pg.GetConnectionString();

    public ShopDbContext NuevoContexto() =>
        new(new DbContextOptionsBuilder<ShopDbContext>().UseNpgsql(Cadena).Options);

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NuevoContexto();
        await db.Database.MigrateAsync();
        await new IdempotencyStore(db).EnsureSchemaAsync();
    }

    public Task DisposeAsync() => _pg.DisposeAsync().AsTask();
}

public class PagosYAgentesTests(BaseDeDatosFixture bd) : IClassFixture<BaseDeDatosFixture>
{
    private sealed class RelojFijo(DateTimeOffset t) : TimeProvider { public override DateTimeOffset GetUtcNow() => t; }
    private sealed class SecretosFijos : IPaymentSecrets
    {
        public Task<PaymentSecrets> LeerAsync(CancellationToken ct = default) => Task.FromResult(new PaymentSecrets("token", "clave-webhook"));
    }
    private sealed class PasarelaFalsa(PagoConsultado? pago) : IPaymentGateway
    {
        public Task<PagoConsultado?> ConsultarPagoAsync(string pagoId, string accessToken, CancellationToken ct = default) => Task.FromResult(pago);
    }
    private sealed class CorreoFalso : IEmailSender
    {
        public int Enviados;
        public Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default) { Interlocked.Increment(ref Enviados); return Task.CompletedTask; }
    }
    private sealed class AgenteCarritoFalso(bool aprueba) : ICartAgent
    {
        public Task<CartOffer> RunAsync(string contextoCliente, int descuentoMaximo = 15, int maxVueltas = 3, CancellationToken ct = default)
        {
            Assert.DoesNotContain("@", contextoCliente);            // el modelo nunca ve el correo del cliente
            return Task.FromResult(new CartOffer(aprueba, "Hola, te esperamos con 10% de descuento.", 1, []));
        }
    }

    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    private async Task<Order> CrearPedidoAsync(string email, decimal total = 219.90m, string estado = "PENDIENTE_PAGO")
    {
        await using var db = bd.NuevoContexto();
        var p = new Order { CustomerName = "Cliente Prueba", Email = email, Address = "Av. Siempre Viva 123", Total = total, Status = estado,
                            CancelReason = estado == "CANCELADA" ? "precio" : null, CancelledUtc = estado == "CANCELADA" ? DateTime.UtcNow : null };
        db.Orders.Add(p); await db.SaveChangesAsync();
        return p;
    }

    private string Aviso(string pagoId, string clave = "clave-webhook") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            origen = "webhook-pago",
            xSignature = MercadoPagoFirma.Firmar(pagoId, "req-x", Ahora.ToUnixTimeSeconds(), clave),
            xRequestId = "req-x", dataId = pagoId, body = new { type = "payment", data = new { id = pagoId } },
        });

    private async Task<WebhookResult> ProcesarAvisoAsync(string mensaje, PagoConsultado? pago)
    {
        await using var db = bd.NuevoContexto();
        return await new MercadoPagoWebhook(db, new SecretosFijos(), new PasarelaFalsa(pago), new RelojFijo(Ahora)).ProcesarAsync(mensaje);
    }

    [Fact]
    public async Task Webhook_marca_PAGADO_una_sola_vez_y_audita()
    {
        var pedido = await CrearPedidoAsync("pago1@ejemplo.com");
        var pago = new PagoConsultado("approved", pedido.Id, pedido.Total);

        var primero = await ProcesarAvisoAsync(Aviso("5001"), pago);
        var repetido = await ProcesarAvisoAsync(Aviso("5001"), pago);

        Assert.True(primero.Aceptado); Assert.Equal("pago confirmado", primero.Motivo);
        Assert.True(repetido.Aceptado); Assert.Contains("ya estaba pagado", repetido.Motivo);
        await using var db = bd.NuevoContexto();
        Assert.Equal("PAGADO", (await db.Orders.FindAsync(pedido.Id))!.Status);
        Assert.Equal(1, await db.BusinessEvents.CountAsync(e => e.EventType == "payment_confirmed" && e.OrderId == pedido.Id));
    }

    [Fact]
    public async Task Webhook_rechaza_y_audita_un_aviso_falso()
    {
        var pedido = await CrearPedidoAsync("falso@ejemplo.com");
        var r = await ProcesarAvisoAsync(Aviso("5002", clave: "clave-del-atacante"), new PagoConsultado("approved", pedido.Id, pedido.Total));

        Assert.False(r.Aceptado);
        await using var db = bd.NuevoContexto();
        Assert.Equal("PENDIENTE_PAGO", (await db.Orders.FindAsync(pedido.Id))!.Status);
        Assert.True(await db.BusinessEvents.AnyAsync(e => e.EventType == "payment_webhook_rejected" && e.Campaign!.Contains("firma")));
    }

    [Theory]
    [InlineData("rejected", 219.90)]   // pago no aprobado
    [InlineData("approved", 1.00)]     // monto distinto al del pedido
    public async Task Webhook_no_paga_si_el_pago_real_no_coincide(string estado, double monto)
    {
        var pedido = await CrearPedidoAsync($"nocoincide{estado}@ejemplo.com");
        var r = await ProcesarAvisoAsync(Aviso("5003"), new PagoConsultado(estado, pedido.Id, (decimal)monto));
        Assert.False(r.Aceptado);
        await using var db = bd.NuevoContexto();
        Assert.Equal("PENDIENTE_PAGO", (await db.Orders.FindAsync(pedido.Id))!.Status);
    }

    private async Task<CartRecoveryResult> RecuperarAsync(int pedidoId, ICartAgent agente, CorreoFalso correo)
    {
        await using var db = bd.NuevoContexto();
        return await new CartRecovery(db, agente, correo).ProcesarAsync($$"""{"pedidoId":{{pedidoId}},"motivo":"precio"}""");
    }

    [Fact]
    public async Task Carrito_maximo_un_correo_por_cliente_cada_24h()
    {
        var a = await CrearPedidoAsync("repetido@ejemplo.com", estado: "CANCELADA");
        var b = await CrearPedidoAsync("REPETIDO@ejemplo.com", estado: "CANCELADA");   // mismo cliente, otra mayuscula
        var correo = new CorreoFalso();

        var r1 = await RecuperarAsync(a.Id, new AgenteCarritoFalso(true), correo);
        var r2 = await RecuperarAsync(b.Id, new AgenteCarritoFalso(true), correo);

        Assert.Equal("enviado", r1.Resultado);
        Assert.StartsWith("omitido", r2.Resultado);
        Assert.Equal(1, correo.Enviados);
    }

    [Fact]
    public async Task Carrito_si_el_critico_no_aprueba_no_envia_y_libera_el_cupo()
    {
        var a = await CrearPedidoAsync("critico@ejemplo.com", estado: "CANCELADA");
        var correo = new CorreoFalso();

        var r1 = await RecuperarAsync(a.Id, new AgenteCarritoFalso(false), correo);
        var r2 = await RecuperarAsync(a.Id, new AgenteCarritoFalso(true), correo);

        Assert.StartsWith("no enviado", r1.Resultado);
        Assert.Equal("enviado", r2.Resultado);                 // el cupo quedo libre
        Assert.Equal(1, correo.Enviados);
    }

    [Fact]
    public async Task Carrito_en_paralelo_el_mismo_cliente_recibe_un_solo_correo()
    {
        var pedidos = new List<Order>();
        for (int i = 0; i < 6; i++) pedidos.Add(await CrearPedidoAsync("paralelo@ejemplo.com", estado: "CANCELADA"));
        var correo = new CorreoFalso();

        await Task.WhenAll(pedidos.Select(p => RecuperarAsync(p.Id, new AgenteCarritoFalso(true), correo)));

        Assert.Equal(1, correo.Enviados);
    }

    private sealed class StockFalso : IStockAgent
    {
        public Task<StockResult> RunAsync(int productId, int maxIteraciones = 8, CancellationToken ct = default) =>
            Task.FromResult(new StockResult($"Comprar 50 unidades del producto {productId}",
                [new ReActStep("Veo pocas unidades", $"ventasUltimaSemana({productId})", "Ventas: 30")], "propuesta final"));
    }
    private sealed class AnalistaFalso : IAnalystAgent
    {
        public Task<AnalystResult> RunAsync(int ventanaMinutos = 15, int maxHipotesis = 4, CancellationToken ct = default) =>
            Task.FromResult(new AnalystResult("precio", "precio: 12", 1, "ALERTA: la causa probable es el precio", "confianza alta"));
    }
    private sealed class AlertasFalsas : IAlertPublisher
    {
        public List<string> Mensajes { get; } = [];
        public Task PublicarAsync(string asunto, string mensaje, CancellationToken ct = default) { Mensajes.Add(mensaje); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Stock_propone_para_los_de_poco_stock_y_no_duplica_pendientes()
    {
        int idPoco;
        await using (var db = bd.NuevoContexto())
        {
            var poco = new Product { Name = "Producto escaso", Price = 10, Stock = 3, Category = "Prueba" };
            db.Products.Add(poco); db.Products.Add(new Product { Name = "Producto sobrado", Price = 10, Stock = 500, Category = "Prueba" });
            await db.SaveChangesAsync(); idPoco = poco.Id;
        }
        await using (var db = bd.NuevoContexto()) await new AgentesProgramados(db, new StockFalso(), new AnalistaFalso(), new AlertasFalsas()).StockAsync();
        await using (var db = bd.NuevoContexto()) await new AgentesProgramados(db, new StockFalso(), new AnalistaFalso(), new AlertasFalsas()).StockAsync();

        await using var dbFinal = bd.NuevoContexto();
        var propuestas = await dbFinal.PurchaseProposals.Where(p => p.ProductId == idPoco).ToListAsync();
        Assert.Single(propuestas);                                   // la segunda noche no duplica la pendiente
        Assert.Equal("PENDIENTE", propuestas[0].Estado);
        Assert.Contains("ventasUltimaSemana", propuestas[0].Traza);
        Assert.False(await dbFinal.PurchaseProposals.AnyAsync(p => p.ProductName == "Producto sobrado"));
    }

    [Fact]
    public async Task Analista_alerta_una_vez_por_ventana_solo_si_hubo_cancelaciones()
    {
        await using (var db = bd.NuevoContexto())
            await db.Orders.Where(o => o.Status == "CANCELADA").ExecuteUpdateAsync(u => u.SetProperty(o => o.CancelledUtc, DateTime.UtcNow.AddDays(-1)));
        var alertas = new AlertasFalsas();
        await using (var db = bd.NuevoContexto())
            Assert.Null(await new AgentesProgramados(db, new StockFalso(), new AnalistaFalso(), alertas).AnalistaAsync());
        Assert.Empty(alertas.Mensajes);

        await CrearPedidoAsync("analista@ejemplo.com", estado: "CANCELADA");
        await using (var db = bd.NuevoContexto())
            Assert.NotNull(await new AgentesProgramados(db, new StockFalso(), new AnalistaFalso(), alertas).AnalistaAsync());
        Assert.Single(alertas.Mensajes);
        Assert.Contains("precio", alertas.Mensajes[0]);
    }
}
