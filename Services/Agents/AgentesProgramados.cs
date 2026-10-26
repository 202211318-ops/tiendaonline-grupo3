using Microsoft.EntityFrameworkCore;
using TiendaOnline.Data;
using TiendaOnline.Models;

namespace TiendaOnline.Services.Agents;

// Publica la alerta del agente analista. En la Lambda es Amazon SNS; en las pruebas, un doble.
public interface IAlertPublisher
{
    Task PublicarAsync(string asunto, string mensaje, CancellationToken ct = default);
}

// Lo que corre por TIEMPO (EventBridge Scheduler invoca la Lambda con {"agente":"stock"|"analista"}).
public sealed class AgentesProgramados(ShopDbContext db, IStockAgent stock, IAnalystAgent analista, IAlertPublisher alertas)
{
    // Agente de stock (cada noche): un loop ReAct por cada producto con poco stock que no tenga ya una
    // propuesta PENDIENTE. La propuesta queda en /admin/compras para que un humano la apruebe.
    public async Task<int> StockAsync(int umbralStock = 20, int maxProductos = 10, CancellationToken ct = default)
    {
        var pendientes = await db.PurchaseProposals.Where(p => p.Estado == "PENDIENTE").Select(p => p.ProductId).ToListAsync(ct);
        var productos = await db.Products.AsNoTracking()
            .Where(p => p.Stock < umbralStock && !pendientes.Contains(p.Id))
            .OrderBy(p => p.Stock).Take(maxProductos).ToListAsync(ct);

        foreach (var p in productos)
        {
            var r = await stock.RunAsync(p.Id, ct: ct);
            var traza = string.Join("\n", r.Traza.Select((t, i) =>
                $"{i + 1}. PENSAMIENTO: {t.Pensamiento} | ACCION: {t.Accion} | OBSERVACION: {t.Observacion}"));
            db.PurchaseProposals.Add(new PurchaseProposal
            {
                ProductId = p.Id, ProductName = p.Name, StockAlProponer = p.Stock,
                Propuesta = Recortar(r.Propuesta, 1000),
                Traza = Recortar($"{traza}\nParada: {r.MotivoParada}", 4000),
            });
        }
        await db.SaveChangesAsync(ct);
        return productos.Count;
    }

    // Agente analista (cada 15 min): razona sobre el RESUMEN SQL de la ventana y emite UNA alerta por
    // ventana (agrupada). Si en la ventana no hubo cancelaciones, no gasta el modelo ni alerta.
    public async Task<AnalystResult?> AnalistaAsync(int ventanaMinutos = 15, CancellationToken ct = default)
    {
        var hubo = await db.Orders.AnyAsync(o => o.Status == "CANCELADA" && o.CancelledUtc >= DateTime.UtcNow.AddMinutes(-ventanaMinutos), ct);
        if (!hubo) return null;
        var r = await analista.RunAsync(ventanaMinutos, ct: ct);
        await alertas.PublicarAsync("URPOnline: causa probable de las cancelaciones",
            $"{r.Alerta}\nHipotesis probadas: {r.Hipotesis}. Parada: {r.MotivoParada}. Ventana: {ventanaMinutos} min.", ct);
        return r;
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
