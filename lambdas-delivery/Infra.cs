using System.Text.Json;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Npgsql;
using StackExchange.Redis;

namespace URPOnline.Delivery;

// Clientes que se crean UNA vez por caja (entorno de ejecucion) y se reusan entre invocaciones.
// La contrasena de RDS no esta en ninguna variable: se pide a Secrets Manager, como en la tienda y los agentes.
public sealed class Infra
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public required NpgsqlDataSource Db { get; init; }
    public required IAmazonSQS Sqs { get; init; }
    public required IAmazonEventBridge Eventos { get; init; }
    public required Lazy<IConnectionMultiplexer> Redis { get; init; }
    public required Func<IAmazonApiGatewayManagementApi> WebSocket { get; init; }
    public required Func<string, string> Config { get; init; }

    public static Infra DesdeEntorno()
    {
        string Env(string k) => Environment.GetEnvironmentVariable(k) ?? throw new InvalidOperationException($"Falta la variable {k}");
        var secretos = new AmazonSecretsManagerClient();
        var secreto = secretos.GetSecretValueAsync(new GetSecretValueRequest { SecretId = Env("Database__SecretArn") }).GetAwaiter().GetResult();
        var clave = JsonDocument.Parse(secreto.SecretString).RootElement.GetProperty("password").GetString();
        var cadena = new NpgsqlConnectionStringBuilder(Env("ConnectionStrings__Postgres")) { Password = clave, MaxPoolSize = 5 };
        return new Infra
        {
            Db = NpgsqlDataSource.Create(cadena.ConnectionString),
            Sqs = new AmazonSQSClient(),
            Eventos = new AmazonEventBridgeClient(),
            Redis = new(() => ConnectionMultiplexer.Connect($"{Env("Redis__Host")}:6379,ssl=true,abortConnect=false")),
            WebSocket = () => new AmazonApiGatewayManagementApiClient(new AmazonApiGatewayManagementApiConfig { ServiceURL = Env("WebSocket__Endpoint") }),
            Config = Env,
        };
    }

    public Task EncolarAsync(string cola, object mensaje, int retrasoSeg = 0, string? grupo = null, string? dedup = null) =>
        Sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = Config(cola),
            MessageBody = JsonSerializer.Serialize(mensaje, Json),
            DelaySeconds = retrasoSeg,
            MessageGroupId = grupo,
            MessageDeduplicationId = dedup,
        });

    // Evento de negocio al bus de la tienda (la regla de la pila lo copia a un grupo de logs: linea de tiempo del pedido).
    public Task PublicarAsync(string tipo, object detalle) =>
        Eventos.PutEventsAsync(new PutEventsRequest
        {
            Entries = [new PutEventsRequestEntry
            {
                EventBusName = Config("Eventos__Bus"),
                Source = "urponline.delivery",
                DetailType = tipo,
                Detail = JsonSerializer.Serialize(detalle, Json),
            }],
        });

    // Envia un mensaje a una conexion WebSocket. Devuelve false si la conexion ya no existe (el cliente se fue).
    public async Task<bool> EnviarAsync(IAmazonApiGatewayManagementApi ws, string conexion, object mensaje)
    {
        try
        {
            await ws.PostToConnectionAsync(new PostToConnectionRequest
            {
                ConnectionId = conexion,
                Data = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(mensaje, Json)),
            });
            return true;
        }
        catch (GoneException) { return false; }
    }
}
