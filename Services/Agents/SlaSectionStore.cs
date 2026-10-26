using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;
using TiendaOnline.Services;

namespace TiendaOnline.Services.Agents;

// RAG ESTRUCTURAL de los contratos de nivel de servicio (SLA), spec fase 2 categoria 1:
// cada producto tiene su SLA guardado POR SECCIONES (garantia, devoluciones, entrega, soporte,
// exclusiones). Cada seccion es una fila con su vector de 384 (el mismo modelo ONNX que la busqueda).
// La recuperacion filtra PRIMERO por producto y tipo de seccion, y luego busca por similitud:
// eso es lo que lo separa del RAG ingenuo (trozos iguales sin estructura).
public sealed record SlaHit(int ProductId, string SectionType, string Content, double Similarity);

public sealed class SlaSectionStore(ShopDbContext db, EmbeddingService embeddings)
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"""
         CREATE EXTENSION IF NOT EXISTS vector;
         CREATE TABLE IF NOT EXISTS "SlaSections" (
            "Id"          serial PRIMARY KEY,
            "ProductId"   integer NOT NULL,
            "SectionType" varchar(40) NOT NULL,
            "Content"     text NOT NULL,
            "Embedding"   vector({EmbeddingService.Dimensions}),
            "UpdatedUtc"  timestamptz NOT NULL DEFAULT now(),
            UNIQUE ("ProductId", "SectionType"));
         CREATE INDEX IF NOT EXISTS "ix_sla_prod_type" ON "SlaSections" ("ProductId", "SectionType");
         """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Guarda/actualiza una seccion y su vector. La calidad del texto afecta la busqueda (paso 2).
    public async Task UpsertAsync(int productId, string sectionType, string content, CancellationToken ct = default)
    {
        var vec = embeddings.IsAvailable ? embeddings.Embed($"{sectionType}: {content}") : null;
        var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
         INSERT INTO "SlaSections" ("ProductId","SectionType","Content","Embedding","UpdatedUtc")
         VALUES (@p,@t,@c,@v::vector,now())
         ON CONFLICT ("ProductId","SectionType") DO UPDATE
           SET "Content"=EXCLUDED."Content","Embedding"=EXCLUDED."Embedding","UpdatedUtc"=now();
         """, conn);
        cmd.Parameters.AddWithValue("p", productId);
        cmd.Parameters.AddWithValue("t", sectionType);
        cmd.Parameters.AddWithValue("c", content);
        cmd.Parameters.AddWithValue("v", (object?)(vec is null ? null : ToLiteral(vec)) ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // RAG estructural: filtra por producto (y tipo si se da) y ordena por similitud del vector.
    public async Task<List<SlaHit>> RetrieveAsync(int productId, string pregunta, int topK = 3,
        string? sectionType = null, CancellationToken ct = default)
    {
        if (!embeddings.IsAvailable) return new();
        var q = ToLiteral(embeddings.Embed(pregunta));
        var conn = await OpenAsync(ct);
        var sql = """
         SELECT "ProductId","SectionType","Content", 1 - ("Embedding" <=> @q::vector) AS sim
         FROM "SlaSections"
         WHERE "ProductId" = @p AND "Embedding" IS NOT NULL
         """ + (sectionType is null ? "" : " AND \"SectionType\" = @t") + """
         ORDER BY "Embedding" <=> @q::vector
         LIMIT @k;
         """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("q", q);
        cmd.Parameters.AddWithValue("p", productId);
        cmd.Parameters.AddWithValue("k", topK);
        if (sectionType is not null) cmd.Parameters.AddWithValue("t", sectionType);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var hits = new List<SlaHit>();
        while (await r.ReadAsync(ct)) hits.Add(new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3)));
        return hits;
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }

    internal static string ToLiteral(float[] v) =>
        "[" + string.Join(',', v.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]";
}
