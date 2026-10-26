using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;

namespace TiendaOnline.Services.Agents;

// Idempotencia (spec fase 2, categoria 2): una tabla en PostgreSQL con los mensajes YA procesados,
// para que un duplicado de SQS no genere un segundo correo. Antes de actuar, el agente pregunta
// "¿ya procese este mensaje?"; si si, lo descarta.
public sealed class IdempotencyStore(ShopDbContext db)
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
         CREATE TABLE IF NOT EXISTS "ProcessedMessages" (
            "MessageId"   varchar(128) PRIMARY KEY,
            "ProcessedUtc" timestamptz NOT NULL DEFAULT now());
         """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Marca el mensaje como procesado. Devuelve TRUE si es la primera vez (hay que procesarlo),
    // FALSE si ya estaba (es un duplicado: no volver a actuar). Atomico por el PRIMARY KEY.
    public async Task<bool> TryMarkAsync(string messageId, CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""
         INSERT INTO "ProcessedMessages" ("MessageId") VALUES (@id)
         ON CONFLICT ("MessageId") DO NOTHING;
         """, conn);
        cmd.Parameters.AddWithValue("id", messageId);
        var filas = await cmd.ExecuteNonQueryAsync(ct);
        return filas == 1;   // 1 = insertado (primera vez); 0 = ya existia (duplicado)
    }

    // Si el procesamiento FALLA, se quita la marca: asi el reintento de SQS (o la DLQ) si actua.
    // Sin esto, el reintento se descartaria como "duplicado" y el mensaje se perderia.
    public async Task UnmarkAsync(string messageId, CancellationToken ct = default)
    {
        var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""DELETE FROM "ProcessedMessages" WHERE "MessageId"=@id;""", conn);
        cmd.Parameters.AddWithValue("id", messageId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }
}
