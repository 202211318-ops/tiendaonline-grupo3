using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;

namespace TiendaOnline.Services.Agents;

// Las herramientas que usan los agentes para leer datos (SQL). El grupo las amplia (paso 5: agregan
// ventasUltimaSemana). Separadas en una interfaz para poder probar los agentes con herramientas falsas.
public interface IAgentTools
{
    Task<string> VentasUltimaSemanaAsync(int productId, CancellationToken ct = default);
    Task<string> ResumenCancelacionesAsync(int minutos, CancellationToken ct = default);
}

// Implementacion sobre PostgreSQL. Cada consulta esta protegida: si la tabla aun no existe (el grupo
// todavia no la creo), devuelve un texto claro en vez de romper el agente.
public sealed class SqlAgentTools(ShopDbContext db) : IAgentTools
{
    public async Task<string> VentasUltimaSemanaAsync(int productId, CancellationToken ct = default)
    {
        try
        {
            var conn = await OpenAsync(ct);
            await using var cmd = new NpgsqlCommand("""
             SELECT COALESCE(SUM(oi."Quantity"),0)
             FROM "OrderItems" oi JOIN "Orders" o ON o."Id"=oi."OrderId"
             WHERE oi."ProductId"=@p AND o."CreatedUtc" >= now() - interval '7 days';
             """, conn);
            cmd.Parameters.AddWithValue("p", productId);
            var n = await cmd.ExecuteScalarAsync(ct);
            return $"Ventas del producto {productId} en los ultimos 7 dias: {n}.";
        }
        catch (Exception e) { return $"No se pudo consultar ventas ({e.GetType().Name})."; }
    }

    public async Task<string> ResumenCancelacionesAsync(int minutos, CancellationToken ct = default)
    {
        try
        {
            var conn = await OpenAsync(ct);
            // Ventana por fecha de CANCELACION (no de creacion) y con parametro, sin interpolar SQL.
            await using var cmd = new NpgsqlCommand("""
             SELECT "CancelReason", count(*) FROM "Orders"
             WHERE "Status"='CANCELADA' AND "CancelledUtc" >= now() - make_interval(mins => @m)
             GROUP BY "CancelReason" ORDER BY 2 DESC;
             """, conn);
            cmd.Parameters.AddWithValue("m", minutos);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var filas = new List<string>();
            while (await r.ReadAsync(ct))
                filas.Add($"{(r.IsDBNull(0) ? "sin_motivo" : r.GetString(0))}: {r.GetInt64(1)}");
            return filas.Count == 0 ? "Sin cancelaciones en la ventana." : "Cancelaciones por motivo -> " + string.Join(", ", filas);
        }
        catch (Exception e) { return $"No se pudo consultar cancelaciones ({e.GetType().Name})."; }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }
}
