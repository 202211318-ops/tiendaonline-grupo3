using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;
namespace TiendaOnline.Services;

// Publica métricas de negocio en Amazon CloudWatch con el AWS SDK (namespace "URPOnline").
// Se ven en el dashboard URPOnline-<pila>. En la EC2 las credenciales vienen del rol IAM (sin claves).
// Se activa con Metrics:CloudWatchEnabled=true (la plantilla lo pone en las EC2; en tu PC queda apagado).
public class BusinessMetrics(IAmazonCloudWatch cloudWatch, IConfiguration config, ILogger<BusinessMetrics> log) {
 public async Task OrderCreatedAsync(decimal amount, CancellationToken ct = default) {
  if (!config.GetValue("Metrics:CloudWatchEnabled", false)) return;
  try {
   await cloudWatch.PutMetricDataAsync(new PutMetricDataRequest {
    Namespace = "URPOnline",
    MetricData = new List<MetricDatum> {
     new() { MetricName = "OrdersCreated", Value = 1, Unit = StandardUnit.Count },
     new() { MetricName = "OrderAmount", Value = (double)amount, Unit = StandardUnit.None }
    }
   }, ct);
  } catch (Exception ex) when (ex is not OperationCanceledException) {
   // La venta ya se guardó en PostgreSQL; si CloudWatch falla, solo se registra el aviso.
   log.LogWarning(ex, "No se pudo publicar la métrica del pedido en CloudWatch");
  }
 }

 // Fase 2 — contadores PROPIOS con el AWS SDK (PutMetricData). El grupo agrega los suyos (paso 11).
 // CartItemsAdded: cada "Agregar al carrito".
 public Task CartItemAddedAsync(CancellationToken ct = default) =>
  PublishAsync("CartItemsAdded", 1, StandardUnit.Count, ct);

 // ActiveCarts: personas distintas con carrito en los ultimos 5 min (cuenta personas, no clics).
 public Task ActiveCartsAsync(int personas, CancellationToken ct = default) =>
  PublishAsync("ActiveCarts", personas, StandardUnit.Count, ct);

 // Contador generico de los agentes: CorreosEnviados, HipotesisProbadas, AlertasEmitidas, DoSDetectado...
 public Task AgentMetricAsync(string nombre, double valor = 1, CancellationToken ct = default) =>
  PublishAsync(nombre, valor, StandardUnit.Count, ct);

 private async Task PublishAsync(string nombre, double valor, StandardUnit unidad, CancellationToken ct) {
  if (!config.GetValue("Metrics:CloudWatchEnabled", false)) return;
  try {
   await cloudWatch.PutMetricDataAsync(new PutMetricDataRequest {
    Namespace = "URPOnline",
    MetricData = new List<MetricDatum> { new() { MetricName = nombre, Value = valor, Unit = unidad } }
   }, ct);
  } catch (Exception ex) when (ex is not OperationCanceledException) {
   log.LogWarning(ex, "No se pudo publicar la métrica {Metric} en CloudWatch", nombre);
  }
 }
}
