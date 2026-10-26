using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;

namespace TiendaOnline.Services.Comida;

public sealed record Restaurante(int Id, string Nombre, double Lat, double Lon);
public sealed record Plato(int Id, int RestauranteId, string Nombre, string Descripcion, decimal Precio,
    bool Disponible, string[] Ingredientes, string[] Alergenos);
public sealed record PlatoEncontrado(Plato Plato, double Coseno);

// Carta y busqueda de platos del delivery (fase 2, §9.9.2). La BASE busca solo por coseno (pgvector):
// la reescritura de la pregunta, Jaccard y el filtro de alergenos son el desafio C5 del grupo.
public sealed class ComidaStore(ShopDbContext db, EmbeddingService embeddings)
{
    public async Task<List<Restaurante>> RestaurantesAsync(CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand("SELECT id, nombre, lat, lon FROM restaurantes ORDER BY nombre", await OpenAsync(ct));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<Restaurante>();
        while (await r.ReadAsync(ct)) lista.Add(new(r.GetInt32(0), r.GetString(1), r.GetDouble(2), r.GetDouble(3)));
        return lista;
    }

    public async Task<List<Plato>> PlatosAsync(int restauranteId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand($"{SelectPlato} WHERE restaurante_id = @id ORDER BY nombre", await OpenAsync(ct));
        cmd.Parameters.AddWithValue("id", restauranteId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<Plato>();
        while (await r.ReadAsync(ct)) lista.Add(LeerPlato(r));
        return lista;
    }

    // Coseno = 1 - distancia (<=>). Sin modelo de embeddings disponible no se inventa un orden: se devuelve vacio.
    public async Task<List<PlatoEncontrado>> BuscarAsync(string consulta, int limite = 10, CancellationToken ct = default)
    {
        if (!embeddings.IsAvailable || string.IsNullOrWhiteSpace(consulta)) return [];
        var vector = embeddings.Embed(consulta);
        await using var cmd = new NpgsqlCommand($"""
            SELECT {ColumnasPlato}, 1 - (embedding <=> @q::vector) AS coseno
            FROM platos
            WHERE embedding IS NOT NULL AND disponible
            ORDER BY embedding <=> @q::vector
            LIMIT @limite
            """, await OpenAsync(ct));
        cmd.Parameters.AddWithValue("q", ALiteral(vector));
        cmd.Parameters.AddWithValue("limite", limite);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<PlatoEncontrado>();
        while (await r.ReadAsync(ct)) lista.Add(new(LeerPlato(r), r.GetDouble(8)));
        return lista;
    }

    // Calcula los embeddings que faltan: platos (nombre + descripcion + ingredientes) y secciones de la politica.
    public async Task<int> CompletarEmbeddingsAsync(CancellationToken ct = default)
    {
        if (!embeddings.IsAvailable) return 0;
        var total = await CompletarAsync("platos",
            "nombre || '. ' || descripcion || '. Ingredientes: ' || array_to_string(ingredientes, ', ')", ct);
        return total + await CompletarAsync("politica_delivery", "contenido", ct);
    }

    private async Task<int> CompletarAsync(string tabla, string texto, CancellationToken ct)
    {
        var pendientes = new List<(int Id, string Texto)>();
        await using (var cmd = new NpgsqlCommand($"SELECT id, {texto} FROM {tabla} WHERE embedding IS NULL", await OpenAsync(ct)))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) pendientes.Add((r.GetInt32(0), r.GetString(1)));
        foreach (var (id, t) in pendientes)
        {
            await using var up = new NpgsqlCommand($"UPDATE {tabla} SET embedding = @v::vector WHERE id = @id", await OpenAsync(ct));
            up.Parameters.AddWithValue("v", ALiteral(embeddings.Embed(t)));
            up.Parameters.AddWithValue("id", id);
            await up.ExecuteNonQueryAsync(ct);
        }
        return pendientes.Count;
    }

    private const string ColumnasPlato = "id, restaurante_id, nombre, descripcion, precio, disponible, ingredientes, alergenos";
    private const string SelectPlato = $"SELECT {ColumnasPlato} FROM platos";

    private static Plato LeerPlato(NpgsqlDataReader r) => new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
        r.GetDecimal(4), r.GetBoolean(5), r.GetFieldValue<string[]>(6), r.GetFieldValue<string[]>(7));

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }

    internal static string ALiteral(float[] v) =>
        "[" + string.Join(",", v.Select(x => x.ToString("R", CultureInfo.InvariantCulture))) + "]";
}
