using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TiendaOnline.Data;
using TiendaOnline.Models;
using TiendaOnline.Services.Agents;
using TiendaOnline.Services.Payments;
using Xunit;
using Xunit.Abstractions;

namespace TiendaOnline.Tests;

// CALIFICADOR AUTOMATICO de la fase 2 (categoria 2: arquitectura serverless). Ataca la Lambda de los agentes
// (URPOnline.Agentes.Handler, el MISMO codigo que corre en AWS) con escenarios generados al azar desde una
// SEMILLA, repetidos CORRIDAS veces, con fallas inyectadas. Cumple solo si TODAS las corridas cumplen:
//   1. Idempotencia: un mensaje SQS repetido (mismo MessageId, en el mismo lote o en otro) actua una sola vez.
//   2. Maximo UN correo por cliente cada 24 h, aunque sus cancelaciones lleguen en paralelo.
//   3. Fallas parciales: un mensaje envenenado (JSON roto) y un envio de correo que falla se reportan en
//      batchItemFailures SIN tumbar al resto del lote; el reintento posterior SI actua (no se pierde).
//   4. Webhook de pago: un aviso valido deja el pedido PAGADO una sola vez; un aviso falso, vencido o de un
//      pago no aprobado NUNCA paga y queda auditado.
// Uso:  SEMILLA=123 CORRIDAS=50 dotnet test tests/TiendaOnline.Tests.csproj --filter CalificadorFase2
// (en la sustentacion el profesor usa una SEMILLA NUEVA). Sin variables: semilla 2026 y 5 corridas.
public class CalificadorFase2(BaseDeDatosFixture bd, ITestOutputHelper salida) : IClassFixture<BaseDeDatosFixture>
{
    private const string ClaveWebhook = "clave-webhook-calificador";

    [Fact]
    public async Task Calificador_fase2_todas_las_corridas_cumplen()
    {
        var semilla = int.TryParse(Environment.GetEnvironmentVariable("SEMILLA"), out var s) ? s : 2026;
        var corridas = int.TryParse(Environment.GetEnvironmentVariable("CORRIDAS"), out var c) ? c : 5;
        var fallas = new List<string>();
        for (int i = 1; i <= corridas; i++)
        {
            var semillaCorrida = HashCode.Combine(semilla, i);
            try { await UnaCorridaAsync(new Random(semillaCorrida), i); }
            catch (Exception e) { fallas.Add($"Corrida {i} (semilla {semillaCorrida}): {e.Message}"); }
        }
        salida.WriteLine($"Calificador fase 2: semilla {semilla}, {corridas} corridas, {corridas - fallas.Count} cumplen.");
        fallas.ForEach(salida.WriteLine);
        Assert.True(fallas.Count == 0, $"NO CUMPLE: {fallas.Count} de {corridas} corridas fallaron.\n" + string.Join("\n", fallas));
    }

    private async Task UnaCorridaAsync(Random azar, int corrida)
    {
        var etiqueta = $"c{corrida}-{azar.Next():x}";
        var correo = new CorreoConFallas(probabilidadFalla: 0.3, azar: new Random(azar.Next()));
        var pasarela = new PasarelaSimulada();
        var handler = new URPOnline.Agentes.Handler(Servicios(correo, pasarela));
        var ctx = new ContextoLambda();

        // ---- Escenario: clientes con varias cancelaciones (algunas duplicadas), avisos de pago de todo tipo ----
        var clientes = Enumerable.Range(0, azar.Next(2, 5)).Select(n => $"{etiqueta}-cliente{n}@ejemplo.com").ToList();
        var mensajes = new List<SQSEvent.SQSMessage>();
        foreach (var cli in clientes)
            for (int k = 0, n = azar.Next(1, 4); k < n; k++)
            {
                var pedido = await CrearPedidoAsync(cli, "CANCELADA", 100 + azar.Next(1, 900));
                var m = Cancelacion($"{etiqueta}-cancel-{pedido.Id}", pedido.Id);
                mensajes.Add(m);
                if (azar.NextDouble() < 0.4) mensajes.Add(m);                 // duplicado (entrega "al menos una vez")
            }
        mensajes.Add(new SQSEvent.SQSMessage { MessageId = $"{etiqueta}-veneno", Body = "{esto no es json" });

        var pagosValidos = new List<int>(); var pagosQueNoDebenPagar = new List<int>();
        for (int k = 0, n = azar.Next(2, 5); k < n; k++)
        {
            var pedido = await CrearPedidoAsync($"{etiqueta}-pago{k}@ejemplo.com", "PENDIENTE_PAGO", 50 + azar.Next(1, 500));
            var pagoId = $"{etiqueta}-pago-{pedido.Id}";
            switch (azar.Next(4))
            {
                case 0:  // valido y aprobado (y a veces repetido)
                    pasarela.Registrar(pagoId, new PagoConsultado("approved", pedido.Id, pedido.Total));
                    mensajes.Add(AvisoPago($"{etiqueta}-mp-{k}", pagoId, ClaveWebhook, DateTimeOffset.UtcNow));
                    if (azar.NextDouble() < 0.5) mensajes.Add(AvisoPago($"{etiqueta}-mp-{k}-bis", pagoId, ClaveWebhook, DateTimeOffset.UtcNow));
                    pagosValidos.Add(pedido.Id); break;
                case 1:  // falso: firmado con otra clave
                    pasarela.Registrar(pagoId, new PagoConsultado("approved", pedido.Id, pedido.Total));
                    mensajes.Add(AvisoPago($"{etiqueta}-mp-{k}", pagoId, "clave-del-atacante", DateTimeOffset.UtcNow));
                    pagosQueNoDebenPagar.Add(pedido.Id); break;
                case 2:  // reenvio de un aviso viejo (antireplay)
                    pasarela.Registrar(pagoId, new PagoConsultado("approved", pedido.Id, pedido.Total));
                    mensajes.Add(AvisoPago($"{etiqueta}-mp-{k}", pagoId, ClaveWebhook, DateTimeOffset.UtcNow.AddHours(-2)));
                    pagosQueNoDebenPagar.Add(pedido.Id); break;
                default: // firmado bien, pero el pago real fue rechazado
                    pasarela.Registrar(pagoId, new PagoConsultado("rejected", pedido.Id, pedido.Total));
                    mensajes.Add(AvisoPago($"{etiqueta}-mp-{k}", pagoId, ClaveWebhook, DateTimeOffset.UtcNow));
                    pagosQueNoDebenPagar.Add(pedido.Id); break;
            }
        }

        // ---- Entrega: lotes desordenados y en PARALELO (concurrencia de la Lambda), luego reintentos como SQS ----
        var pendientes = mensajes.OrderBy(_ => azar.Next()).ToList();
        for (int intento = 1; intento <= 8 && pendientes.Count > 0; intento++)
        {
            var lotes = pendientes.Chunk(Math.Max(1, azar.Next(2, 6))).ToList();
            var respuestas = await Task.WhenAll(lotes.Select(l => handler.Procesar(new SQSEvent { Records = l.ToList() }, ctx)));
            var fallidos = respuestas.SelectMany(r => r.BatchItemFailures.Select(f => f.ItemIdentifier)).ToHashSet();
            // Solo vuelven a la cola los que la Lambda REPORTO como fallidos (fallas parciales).
            pendientes = pendientes.Where(m => fallidos.Contains(m.MessageId)).DistinctBy(m => m.MessageId).ToList();
            if (intento == 1 && !fallidos.Contains($"{etiqueta}-veneno"))
                throw new Exception("el mensaje envenenado no se reporto como fallido (batchItemFailures)");
            correo.ProbabilidadFalla = intento >= 3 ? 0 : correo.ProbabilidadFalla;   // la falla inyectada se "repara"
        }
        if (pendientes.Any(m => m.MessageId != $"{etiqueta}-veneno"))
            throw new Exception($"quedaron mensajes sin procesar tras los reintentos: {string.Join(", ", pendientes.Select(m => m.MessageId))}");

        // ---- Reglas ----
        foreach (var cli in clientes)
        {
            var n = correo.EnviadosA(cli);
            if (n != 1) throw new Exception($"{cli} recibio {n} correos (debe ser exactamente 1 en 24 h)");
        }
        await using var db = bd.NuevoContexto();
        foreach (var id in pagosValidos)
        {
            var p = await db.Orders.FindAsync(id);
            if (p!.Status != "PAGADO") throw new Exception($"pedido {id}: aviso valido y no quedo PAGADO ({p.Status})");
            var confirmaciones = await db.BusinessEvents.CountAsync(e => e.EventType == "payment_confirmed" && e.OrderId == id);
            if (confirmaciones != 1) throw new Exception($"pedido {id}: {confirmaciones} confirmaciones de pago (debe ser 1)");
        }
        foreach (var id in pagosQueNoDebenPagar)
        {
            var p = await db.Orders.FindAsync(id);
            if (p!.Status != "PENDIENTE_PAGO") throw new Exception($"pedido {id}: un aviso invalido lo dejo en {p.Status}");
            var clave = $"webhook:{etiqueta}-pago-{id}";
            if (!await db.BusinessEvents.AnyAsync(e => e.EventType == "payment_webhook_rejected" && e.SessionKey == clave))
                throw new Exception($"pedido {id}: el aviso rechazado no quedo auditado");
        }
    }

    // ----------------- Escenario -----------------
    private async Task<Order> CrearPedidoAsync(string email, string estado, decimal total)
    {
        await using var db = bd.NuevoContexto();
        var p = new Order { CustomerName = "Cliente", Email = email, Address = "Av. Prueba 1", Total = total, Status = estado,
                            CancelReason = estado == "CANCELADA" ? "precio" : null, CancelledUtc = estado == "CANCELADA" ? DateTime.UtcNow : null };
        db.Orders.Add(p); await db.SaveChangesAsync();
        return p;
    }

    private static SQSEvent.SQSMessage Cancelacion(string messageId, int pedidoId) => new()
    {
        MessageId = messageId,
        Body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["detail-type"] = "CompraCancelada", ["source"] = "urponline.tienda",
            ["detail"] = new { pedidoId, motivo = "precio", total = 1 },
        }),
    };

    private static SQSEvent.SQSMessage AvisoPago(string messageId, string pagoId, string clave, DateTimeOffset cuando) => new()
    {
        MessageId = messageId,
        Body = JsonSerializer.Serialize(new { type = "payment", data = new { id = pagoId } }),
        MessageAttributes = new()
        {
            ["origen"] = new() { StringValue = "webhook-pago", DataType = "String" },
            ["xSignature"] = new() { StringValue = MercadoPagoFirma.Firmar(pagoId, "req-" + messageId, cuando.ToUnixTimeSeconds(), clave), DataType = "String" },
            ["xRequestId"] = new() { StringValue = "req-" + messageId, DataType = "String" },
            ["dataId"] = new() { StringValue = pagoId, DataType = "String" },
        },
    };

    // ----------------- Servicios de la Lambda con dobles (sin AWS ni modelo de lenguaje) -----------------
    private IServiceProvider Servicios(CorreoConFallas correo, PasarelaSimulada pasarela)
    {
        var s = new ServiceCollection();
        s.AddDbContext<ShopDbContext>(o => o.UseNpgsql(bd.Cadena));
        s.AddScoped<IdempotencyStore>();
        s.AddScoped<CartRecovery>();
        s.AddScoped<MercadoPagoWebhook>();
        s.AddSingleton<ICartAgent>(new AgenteCarritoQueAprueba());
        s.AddSingleton<IEmailSender>(correo);
        s.AddSingleton<IPaymentGateway>(pasarela);
        s.AddSingleton<IPaymentSecrets>(new SecretosFijos());
        s.AddSingleton(TimeProvider.System);
        return s.BuildServiceProvider();
    }

    private sealed class AgenteCarritoQueAprueba : ICartAgent
    {
        public Task<CartOffer> RunAsync(string contextoCliente, int descuentoMaximo = 15, int maxVueltas = 3, CancellationToken ct = default) =>
            Task.FromResult(new CartOffer(true, "Te esperamos con 10% de descuento.", 1, []));
    }

    private sealed class CorreoConFallas(double probabilidadFalla, Random azar) : IEmailSender
    {
        private readonly List<string> _enviados = [];
        public double ProbabilidadFalla { get; set; } = probabilidadFalla;
        public int EnviadosA(string email) { lock (_enviados) return _enviados.Count(e => e.Equals(email, StringComparison.OrdinalIgnoreCase)); }
        public Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default)
        {
            lock (_enviados)
            {
                if (azar.NextDouble() < ProbabilidadFalla) throw new InvalidOperationException("SES no disponible (falla inyectada)");
                _enviados.Add(para);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class PasarelaSimulada : IPaymentGateway
    {
        private readonly Dictionary<string, PagoConsultado> _pagos = [];
        public void Registrar(string id, PagoConsultado p) => _pagos[id] = p;
        public Task<PagoConsultado?> ConsultarPagoAsync(string pagoId, string accessToken, CancellationToken ct = default) =>
            Task.FromResult(_pagos.TryGetValue(pagoId, out var p) ? p : null);
    }

    private sealed class SecretosFijos : IPaymentSecrets
    {
        public Task<PaymentSecrets> LeerAsync(CancellationToken ct = default) => Task.FromResult(new PaymentSecrets("token", ClaveWebhook));
    }

    private sealed class ContextoLambda : ILambdaContext
    {
        public string AwsRequestId => "calificador";
        public IClientContext ClientContext => null!;
        public string FunctionName => "urponline-agentes";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-2:000000000000:function:urponline-agentes";
        public ILambdaLogger Logger { get; } = new Registro();
        public string LogGroupName => "/aws/lambda/urponline-agentes";
        public string LogStreamName => "calificador";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(2);
    }

    private sealed class Registro : ILambdaLogger
    {
        public void Log(string message) { }
        public void LogLine(string message) { }
    }
}
