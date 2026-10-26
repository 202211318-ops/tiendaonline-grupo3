using System.Reflection;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.ApiGatewayManagementApi;
using Amazon.EventBridge;
using Amazon.SQS;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StackExchange.Redis;
using TiendaOnline.Services;
using TiendaOnline.Services.Comida;
using URPOnline.Delivery;
using Xunit;
using Xunit.Abstractions;

namespace TiendaOnline.Tests;

// CALIFICADOR AUTOMATICO del delivery (fase 2, §9.9.6). Ataca las Lambdas del delivery (URPOnline.Delivery.Funciones,
// el MISMO codigo que corre en AWS) contra PostgreSQL real, con escenarios generados desde una SEMILLA y repetidos
// CORRIDAS veces. Cumple solo si TODAS las corridas cumplen:
//   B2. Entre 2 y 6 repartidores aceptan el MISMO pedido a la vez: exactamente uno recibe 200, el resto 409,
//       y el pedido queda con ese repartidor.
//   A3. Una secuencia al azar de cambios del celular (con saltos invalidos y repetidos) nunca deja un estado
//       imposible: «entregado» solo despues de «recogido», y la version sube una vez por transicion valida.
//   B3. Un mismo cambio entregado dos veces por SQS (reintento) se aplica una sola vez.
// Sobre la BASE del curso NO cumple a proposito: son desafios del grupo. Por eso no corre en el CI normal
// (Trait Categoria=CalificadorDelivery); se lanza con el workflow «Calificador delivery».
// Uso: SEMILLA=123 CORRIDAS=50 dotnet test tests/TiendaOnline.Tests.csproj --filter "Categoria=CalificadorDelivery"
[Trait("Categoria", "CalificadorDelivery")]
public class CalificadorDelivery(BaseDeDatosFixture bd, ITestOutputHelper salida) : IClassFixture<BaseDeDatosFixture>
{
    [Fact]
    public async Task Calificador_delivery_todas_las_corridas_cumplen()
    {
        var semilla = int.TryParse(Environment.GetEnvironmentVariable("SEMILLA"), out var s) ? s : 2026;
        var corridas = int.TryParse(Environment.GetEnvironmentVariable("CORRIDAS"), out var c) ? c : 5;
        await using (var db = bd.NuevoContexto())
            await new ComidaSeeder(db, new ComidaStore(db, SinModelo())).SeedAsync();
        await using var fuente = NpgsqlDataSource.Create(bd.Cadena);
        var funciones = new Funciones(InfraDePrueba(fuente));
        var fallas = new List<string>();
        for (int i = 1; i <= corridas; i++)
        {
            var azar = new Random(HashCode.Combine(semilla, i));
            try
            {
                await CarreraDeAceptacionAsync(funciones, fuente, azar, i);
                await CambiosDelCelularAsync(funciones, fuente, azar, i);
            }
            catch (Exception e) { fallas.Add($"Corrida {i}: {e.Message}"); }
        }
        salida.WriteLine($"Calificador delivery: semilla {semilla}, {corridas} corridas, {corridas - fallas.Count} cumplen.");
        fallas.ForEach(salida.WriteLine);
        Assert.True(fallas.Count == 0, $"{fallas.Count} de {corridas} corridas NO cumplen. Primera: {fallas.FirstOrDefault()}");
    }

    // B2: varios repartidores aceptan a la vez.
    private static async Task CarreraDeAceptacionAsync(Funciones f, NpgsqlDataSource db, Random azar, int corrida)
    {
        var pedido = await NuevoPedidoAsync(db);
        var n = azar.Next(2, 7);
        var respuestas = await Task.WhenAll(Enumerable.Range(0, n).Select(k =>
            f.AceptarPedido(Solicitud($"moto-{corrida}-{k}", ("id", pedido.ToString())), new Contexto())));
        var ganadores = respuestas.Count(r => r.StatusCode == 200);
        var rechazos = respuestas.Count(r => r.StatusCode == 409);
        if (ganadores != 1 || rechazos != n - 1)
            throw new Exception($"B2: {n} aceptaciones a la vez dieron {ganadores} con 200 y {rechazos} con 409 (se espera 1 y {n - 1}).");
    }

    // A3 + B3: cambios del celular en orden aleatorio, con saltos y duplicados (reintentos de SQS).
    private static async Task CambiosDelCelularAsync(Funciones f, NpgsqlDataSource db, Random azar, int corrida)
    {
        var pedido = await NuevoPedidoAsync(db);
        var aceptado = await f.AceptarPedido(Solicitud($"moto-{corrida}-cambios", ("id", pedido.ToString())), new Contexto());
        if (aceptado.StatusCode != 200) throw new Exception($"A3: no se pudo aceptar el pedido {pedido} ({aceptado.StatusCode}).");
        var version0 = await LeerAsync<int>(db, "SELECT version FROM pedidos_comida WHERE id = @id", pedido);
        var secuencia = Enumerable.Range(0, azar.Next(3, 8)).Select(_ => Reglas.EstadosDelCelular[azar.Next(2)]).ToList();
        var mensajes = secuencia.SelectMany(e => azar.Next(3) == 0 ? new[] { e, e } : new[] { e })   // B3: reintentos
            .Select(e => new SQSEvent.SQSMessage { MessageId = Guid.NewGuid().ToString(),
                Body = JsonSerializer.Serialize(new MensajeCambio(pedido, e, $"moto-{corrida}-cambios", null), Infra.Json) }).ToList();
        foreach (var m in mensajes) await f.ActualizarEntrega(new SQSEvent { Records = [m] }, new Contexto());

        // Transiciones validas desde «aceptado»: recogido (una vez) y luego entregado (una vez).
        var esperadas = 0; var estado = "aceptado";
        foreach (var e in secuencia)
            if ((estado, e) is ("aceptado", "recogido") or ("recogido", "entregado")) { estado = e; esperadas++; }
        var final = await LeerAsync<string>(db, "SELECT estado FROM pedidos_comida WHERE id = @id", pedido);
        var version = await LeerAsync<int>(db, "SELECT version FROM pedidos_comida WHERE id = @id", pedido);
        if (final != estado || version - version0 != esperadas)
            throw new Exception($"A3/B3: con [{string.Join(", ", mensajes.Select(m => m.Body.Contains("entregado") ? "entregado" : "recogido"))}] " +
                                $"quedó «{final}» con {version - version0} cambios de versión (se espera «{estado}» y {esperadas}).");
    }

    // ---- utilidades ----
    private static async Task<int> NuevoPedidoAsync(NpgsqlDataSource db)
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO pedidos_comida (cliente_sub, restaurante_id, total, lat, lon, estado) SELECT 'cliente-calificador', id, 30, -12.12, -77.03, 'ofrecido' FROM restaurantes LIMIT 1 RETURNING id");
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<T> LeerAsync<T>(NpgsqlDataSource db, string sql, int id)
    {
        await using var cmd = db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("id", id);
        return (T)(await cmd.ExecuteScalarAsync())!;
    }

    private static APIGatewayProxyRequest Solicitud(string sub, params (string, string)[] ruta) => new()
    {
        PathParameters = ruta.ToDictionary(p => p.Item1, p => p.Item2),
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            { Authorizer = new APIGatewayCustomAuthorizerContext { Claims = new Dictionary<string, string> { ["sub"] = sub } } },
    };

    // AWS (SQS, EventBridge, WebSocket) se reemplaza por dobles que no hacen nada: lo que se califica es la base de datos.
    private static Infra InfraDePrueba(NpgsqlDataSource db) => new()
    {
        Db = db,
        Sqs = Nada<IAmazonSQS>(),
        Eventos = Nada<IAmazonEventBridge>(),
        Redis = new(() => throw new InvalidOperationException("El calificador no usa Redis")),
        WebSocket = Nada<IAmazonApiGatewayManagementApi>,
        Config = k => $"prueba-{k}",
    };

    private static T Nada<T>() where T : class => DispatchProxy.Create<T, Doble>();

    public class Doble : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? args)
        {
            var t = metodo!.ReturnType;
            if (t == typeof(Task)) return Task.CompletedTask;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var r = t.GetGenericArguments()[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(r)
                    .Invoke(null, [Activator.CreateInstance(r)]);
            }
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }

    private static EmbeddingService SinModelo() => new(
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Embeddings:ModelPath"] = Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid()) }).Build(),
        new EntornoDePrueba(), Microsoft.Extensions.Logging.Abstractions.NullLogger<EmbeddingService>.Instance);

    private sealed class EntornoDePrueba : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class Contexto : ILambdaContext
    {
        public string AwsRequestId => "calificador";
        public IClientContext ClientContext => null!;
        public string FunctionName => "calificador";
        public string FunctionVersion => "1";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "calificador";
        public ILambdaLogger Logger { get; } = new Registro();
        public string LogGroupName => "calificador";
        public string LogStreamName => "calificador";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(1);
    }

    private sealed class Registro : ILambdaLogger
    {
        public void Log(string message) { }
        public void LogLine(string message) { }
    }
}
