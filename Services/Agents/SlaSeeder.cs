using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;

namespace TiendaOnline.Services.Agents;

// Siembra SLA de ejemplo (uno por producto, por secciones) para poder probar el RAG del agente de
// soporte. El grupo luego los edita/crea con migraciones (paso 2). Solo siembra si la tabla esta vacia.
public sealed class SlaSeeder(ShopDbContext db, SlaSectionStore sla)
{
    private static readonly (string tipo, string texto)[] Secciones =
    {
        ("garantia",     "La garantia de este producto es de 12 meses por defectos de fabricacion, contados desde la fecha de entrega."),
        ("devoluciones", "Se aceptan devoluciones dentro de los 7 dias de recibido el producto, sin uso y con su empaque original."),
        ("entrega",      "El tiempo de entrega estimado es de 3 a 5 dias habiles en Lima y de 5 a 10 dias en provincias."),
        ("soporte",      "El soporte tecnico se atiende de lunes a viernes de 9 a 18 horas por el chat de la tienda."),
        ("exclusiones",  "La garantia no cubre danos por mal uso, caidas, contacto con liquidos ni manipulacion por terceros."),
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await sla.EnsureSchemaAsync(ct);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM \"SlaSections\"", conn))
            if (Convert.ToInt64(await count.ExecuteScalarAsync(ct)) > 0) return;  // ya hay SLA

        // IDs de productos existentes (la tienda base trae 6).
        var ids = new List<int>();
        await using (var prod = new NpgsqlCommand("SELECT \"Id\" FROM \"Products\" ORDER BY \"Id\"", conn))
        await using (var r = await prod.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) ids.Add(r.GetInt32(0));

        foreach (var id in ids)
            foreach (var (tipo, texto) in Secciones)
                await sla.UpsertAsync(id, tipo, texto, ct);
    }
}
