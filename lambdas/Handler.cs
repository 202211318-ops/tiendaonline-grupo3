using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SecretsManager;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TiendaOnline.Data;
using TiendaOnline.Services;
using TiendaOnline.Services.Agents;
using TiendaOnline.Services.Llm;
using TiendaOnline.Services.Payments;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace URPOnline.Agentes;

// Lo que manda EventBridge Scheduler: {"agente":"stock"} o {"agente":"analista"}.
public sealed record EntradaProgramada(string Agente);

// Dos puntos de entrada (dos funciones Lambda en infra/fase2-agentes.yaml, mismo codigo):
//  - Procesar   : SQS -> Lambda. Eventos de la tienda (EventBridge) y avisos del webhook de pago.
//                 Idempotencia (un duplicado no actua dos veces) y REPORTE DE FALLAS PARCIALES del lote.
//  - Programado : EventBridge Scheduler -> Lambda. Agente de stock (cada noche) y analista (cada 15 min).
public class Handler
{
    private readonly IServiceProvider _sp;

    public Handler() : this(BuildServices()) { }                // el que usa AWS Lambda
    public Handler(IServiceProvider servicios) => _sp = servicios; // el que usa el calificador (servicios de prueba)

    public async Task<SQSBatchResponse> Procesar(SQSEvent evt, ILambdaContext ctx)
    {
        var fallidos = new List<SQSBatchResponse.BatchItemFailure>();
        foreach (var msg in evt.Records ?? [])
        {
            try
            {
                using var scope = _sp.CreateScope();
                // Caída simulada (fase 2): con SIMULAR_CAIDA=true no se procesa nada y cada mensaje se reporta como
                // fallido, como si se hubiera caído el servicio del que depende. SQS lo reintenta y, al agotar
                // maxReceiveCount, lo manda a la DLQ.
                if (scope.ServiceProvider.GetService<IConfiguration>()?["SIMULAR_CAIDA"] == "true")
                    throw new InvalidOperationException("Caída simulada (SIMULAR_CAIDA=true)");
                var idem = scope.ServiceProvider.GetRequiredService<IdempotencyStore>();
                if (!await idem.TryMarkAsync(msg.MessageId))
                {
                    ctx.Logger.LogInformation($"Duplicado ignorado: {msg.MessageId}");
                    continue;
                }
                try
                {
                    await DispatchAsync(scope.ServiceProvider, msg, ctx);
                }
                catch
                {
                    // Fallo al procesar: se libera la marca para que el REINTENTO de SQS si actue.
                    await idem.UnmarkAsync(msg.MessageId);
                    throw;
                }
            }
            catch (Exception e)
            {
                ctx.Logger.LogError($"Fallo {msg.MessageId}: {e.GetType().Name}: {e.Message}");
                fallidos.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = msg.MessageId });
            }
        }
        return new SQSBatchResponse { BatchItemFailures = fallidos };
    }

    public async Task<string> Programado(EntradaProgramada entrada, ILambdaContext ctx)
    {
        using var scope = _sp.CreateScope();
        var agentes = scope.ServiceProvider.GetRequiredService<AgentesProgramados>();
        switch (entrada.Agente?.ToLowerInvariant())
        {
            case "stock":
                var n = await agentes.StockAsync();
                ctx.Logger.LogInformation($"Agente de stock: {n} propuestas nuevas en /admin/compras");
                return $"stock: {n} propuestas";
            case "analista":
                var r = await agentes.AnalistaAsync();
                ctx.Logger.LogInformation(r is null ? "Agente analista: sin cancelaciones en la ventana"
                    : $"Agente analista: causa={r.Causa} hipotesis={r.Hipotesis} parada={r.MotivoParada}");
                return r is null ? "analista: sin cancelaciones" : $"analista: {r.Causa}";
            default:
                throw new ArgumentException($"Agente desconocido: '{entrada.Agente}' (usa stock o analista)");
        }
    }

    private static async Task DispatchAsync(IServiceProvider sp, SQSEvent.SQSMessage msg, ILambdaContext ctx)
    {
        // Aviso del webhook de pago: API Gateway lo mete en la cola con el atributo origen=webhook-pago
        // y las cabeceras de la firma como atributos (xSignature, xRequestId, dataId).
        if (Atributo(msg, "origen") == "webhook-pago")
        {
            var r = await sp.GetRequiredService<MercadoPagoWebhook>().ProcesarAsync(MensajeWebhook(msg));
            ctx.Logger.LogInformation($"Webhook de pago: aceptado={r.Aceptado} pedido={r.PedidoId} motivo={r.Motivo}");
            return;
        }

        using var doc = JsonDocument.Parse(msg.Body);
        var root = doc.RootElement;

        // Eventos de la tienda: EventBridge los envuelve y "detail-type" indica el tipo.
        var tipo = root.TryGetProperty("detail-type", out var dt) ? dt.GetString() : null;
        if (tipo == "CompraCancelada")
        {
            var detalle = root.TryGetProperty("detail", out var d) ? d.GetRawText() : "{}";
            var r = await sp.GetRequiredService<CartRecovery>().ProcesarAsync(detalle);
            ctx.Logger.LogInformation($"Agente de carrito: {r.Resultado} vueltas={r.Oferta?.Vueltas}");
            return;
        }
        // Los demas tipos (PedidoCreado, ProductoVisto...) no tienen agente: aqui el grupo conecta su HOOK nuevo.
        ctx.Logger.LogInformation($"Evento sin agente asociado: {tipo}");
    }

    private static string? Atributo(SQSEvent.SQSMessage msg, string nombre)
    {
        var v = msg.MessageAttributes is not null && msg.MessageAttributes.TryGetValue(nombre, out var a) ? a.StringValue : null;
        return v is null or "ninguna" or "ninguno" ? null : v;   // API Gateway pone "ninguno" si la cabecera no vino
    }

    // Arma el mensaje que entiende MercadoPagoWebhook: firma + cuerpo original del aviso.
    public static string MensajeWebhook(SQSEvent.SQSMessage msg)
    {
        JsonElement cuerpo;
        try { cuerpo = JsonDocument.Parse(string.IsNullOrWhiteSpace(msg.Body) ? "{}" : msg.Body).RootElement.Clone(); }
        catch (JsonException) { cuerpo = JsonDocument.Parse("{}").RootElement.Clone(); }
        return JsonSerializer.Serialize(new
        {
            origen = "webhook-pago",
            xSignature = Atributo(msg, "xSignature"),
            xRequestId = Atributo(msg, "xRequestId"),
            dataId = Atributo(msg, "dataId"),
            body = cuerpo,
        });
    }

    // Composicion de servicios dentro de la Lambda. La configuracion llega por variables de entorno
    // (infra/fase2-agentes.yaml): ConnectionStrings__Postgres, Database__SecretArn, Llm__SecretId,
    // Payments__SecretId, Alerts__TopicArn, Email__From.
    private static IServiceProvider BuildServices()
    {
        var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>(new LambdaHostEnvironment());
        services.AddLogging();
        services.AddHttpClient("llm", c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<IPaymentGateway, MercadoPagoGateway>(c => c.Timeout = TimeSpan.FromSeconds(15));

        var cs = DatabaseSecret.BuildConnectionString(config, config.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Falta ConnectionStrings__Postgres en la Lambda"));
        services.AddDbContext<ShopDbContext>(o => o.UseNpgsql(cs));

        services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>();
        services.AddSingleton<IAmazonSimpleNotificationService, AmazonSimpleNotificationServiceClient>();
        services.AddSingleton<IAmazonSimpleEmailServiceV2, AmazonSimpleEmailServiceV2Client>();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<LlmCatalog>();
        services.AddSingleton<LlmSettingsStore>();
        services.AddSingleton<LlmProviderFactory>();
        services.AddScoped<IdempotencyStore>();
        services.AddScoped<IAgentTools, SqlAgentTools>();
        services.AddScoped<ICartAgent, CartAgent>();
        services.AddScoped<IStockAgent, StockAgent>();
        services.AddScoped<IAnalystAgent, AnalystAgent>();
        services.AddScoped<AgentesProgramados>();
        services.AddScoped<CartRecovery>();
        services.AddScoped<MercadoPagoWebhook>();
        services.AddSingleton<IAlertPublisher>(sp => new SnsAlertPublisher(
            sp.GetRequiredService<IAmazonSimpleNotificationService>(), Requerido(config, "Alerts:TopicArn")));
        services.AddSingleton<IEmailSender>(sp => new SesEmailSender(
            sp.GetRequiredService<IAmazonSimpleEmailServiceV2>(), Requerido(config, "Email:From")));
        services.AddSingleton<IPaymentSecrets>(sp => new SecretsManagerPaymentSecrets(
            sp.GetRequiredService<IAmazonSecretsManager>(), Requerido(config, "Payments:SecretId")));
        return services.BuildServiceProvider();
    }

    private static string Requerido(IConfiguration c, string clave) =>
        c[clave] is { Length: > 0 } v ? v : throw new InvalidOperationException($"Falta la variable {clave.Replace(":", "__")} en la Lambda");
}

// Alerta del agente analista por Amazon SNS.
public sealed class SnsAlertPublisher(IAmazonSimpleNotificationService sns, string topicArn) : IAlertPublisher
{
    public Task PublicarAsync(string asunto, string mensaje, CancellationToken ct = default) =>
        sns.PublishAsync(new PublishRequest { TopicArn = topicArn, Subject = asunto, Message = mensaje }, ct);
}

// Correo de recuperacion de carrito por Amazon SES (en el sandbox de SES solo llega a correos VERIFICADOS).
public sealed class SesEmailSender(IAmazonSimpleEmailServiceV2 ses, string remitente) : IEmailSender
{
    public Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default) =>
        ses.SendEmailAsync(new SendEmailRequest
        {
            FromEmailAddress = remitente,
            Destination = new Destination { ToAddresses = [para] },
            Content = new EmailContent { Simple = new Message
            {
                Subject = new Content { Data = asunto, Charset = "UTF-8" },
                Body = new Body { Text = new Content { Data = cuerpo, Charset = "UTF-8" } },
            } },
        }, ct);
}

// Entorno minimo para la Lambda: ContentRootPath = raiz del paquete (donde se copia Data/llm/modelos.json).
internal sealed class LambdaHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "URPOnline.Agentes";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
        = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
}
