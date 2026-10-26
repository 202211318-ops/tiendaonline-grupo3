using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
namespace TiendaOnline.Services;

// Eventos de negocio que la tienda publica en Amazon EventBridge (bus urponline-<pila>).
// Una regla del bus los envía a la cola SQS de los agentes. Sin Events:BusName (en tu PC) solo se escriben en el log.
public static class StoreEventTypes {
 public const string Source = "urponline.tienda";
 public const string PedidoCreado = "PedidoCreado";
 public const string CompraCancelada = "CompraCancelada";
 public const string ProductoAgregadoAlCarrito = "ProductoAgregadoAlCarrito";
}

// Motivos que el cliente puede elegir al cancelar (lista cerrada: el agente analista los agrupa).
public static class CancelReasons {
 public static readonly IReadOnlyDictionary<string,string> All = new Dictionary<string,string> {
  ["precio"]="El precio es alto", ["costo_envio"]="El costo de envío", ["garantia"]="La garantía no me convence",
  ["demora"]="El tiempo de entrega", ["pago"]="Tuve problemas con el pago", ["me_arrepenti"]="Me arrepentí", ["otro"]="Otro motivo"
 };
 public static bool IsValid(string? code) => code is not null && All.ContainsKey(code);
}

public record EventItem(int ProductoId, int Cantidad, decimal Precio);

public class StoreEventPublisher(IAmazonEventBridge eventBridge, IConfiguration config, ILogger<StoreEventPublisher> log) {
 static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

 // El identificador de sesión nunca viaja en claro: los agentes solo necesitan saber si dos eventos son de la misma persona.
 public static string HashSession(string? sessionId) =>
  string.IsNullOrEmpty(sessionId) ? "sin-sesion"
  : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)))[..16].ToLowerInvariant();

 // Arma el detalle del evento. Es una función pura para poder probarla sin AWS.
 public static string BuildDetail(object detail) => JsonSerializer.Serialize(detail, Json);

 public async Task PublishAsync(string detailType, object detail, CancellationToken ct = default) {
  var json = BuildDetail(detail);
  var bus = config["Events:BusName"];
  if (string.IsNullOrWhiteSpace(bus)) { log.LogInformation("Evento {Tipo} (sin bus configurado): {Detalle}", detailType, json); return; }
  try {
   var r = await eventBridge.PutEventsAsync(new PutEventsRequest { Entries = new List<PutEventsRequestEntry> {
    new() { EventBusName = bus, Source = StoreEventTypes.Source, DetailType = detailType, Detail = json } } }, ct);
   if (r.FailedEntryCount > 0)
    log.LogWarning("EventBridge rechazó el evento {Tipo}: {Error}", detailType, r.Entries[0].ErrorMessage);
  } catch (Exception ex) when (ex is not OperationCanceledException) {
   // El pedido ya está guardado en PostgreSQL; si EventBridge falla, se registra el aviso y la compra sigue.
   log.LogWarning(ex, "No se pudo publicar el evento {Tipo} en EventBridge", detailType);
  }
 }
}
