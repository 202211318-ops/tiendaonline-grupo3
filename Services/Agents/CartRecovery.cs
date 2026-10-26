using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;

namespace TiendaOnline.Services.Agents;

// Envia el correo. En la Lambda es Amazon SES; en las pruebas, un doble.
public interface IEmailSender
{
    Task EnviarAsync(string para, string asunto, string cuerpo, CancellationToken ct = default);
}

public sealed record CartRecoveryResult(string Resultado, CartOffer? Oferta);

// Recuperacion de carrito ante una CompraCancelada (llega por SQS):
//  1) busca el pedido y el correo del cliente en PostgreSQL (el evento NO trae datos personales);
//  2) regla del negocio: como maximo UN correo por cliente cada 24 h;
//  3) loop generador/critico (CartAgent); solo si el critico APRUEBA se envia por SES.
// El modelo nunca ve el correo ni el nombre del cliente: solo el motivo, el total y los productos.
public sealed class CartRecovery(ShopDbContext db, ICartAgent agente, IEmailSender correo)
{
    // Bases donde ya se creo la tabla (una vez por proceso y por base: host, puerto y nombre).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _esquemaListo = new();

    // Crea la tabla de control una sola vez. CREATE TABLE IF NOT EXISTS NO es seguro en paralelo en PostgreSQL
    // (varias Lambdas a la vez chocan en pg_class): se serializa con un bloqueo de PostgreSQL.
    public Task EnsureSchemaAsync(CancellationToken ct = default) => CrearTablaAsync(db, ct);

    // Tambien la llama la tienda al arrancar (Program.cs), junto con las migraciones.
    public static async Task CrearTablaAsync(ShopDbContext db, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        var baseDatos = $"{conn.Host}:{conn.Port}/{conn.Database}";
        if (_esquemaListo.ContainsKey(baseDatos)) return;
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(727002);", conn, tx))
            await lockCmd.ExecuteNonQueryAsync(ct);
        await using var cmd = new NpgsqlCommand("""
         CREATE TABLE IF NOT EXISTS "CartEmailsSent" (
            "Id"        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            "EmailHash" varchar(64) NOT NULL,
            "SentUtc"   timestamptz NOT NULL DEFAULT now());
         CREATE INDEX IF NOT EXISTS "IX_CartEmailsSent_Hash" ON "CartEmailsSent" ("EmailHash","SentUtc");
         """, conn, tx);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        _esquemaListo[baseDatos] = true;
    }

    public async Task<CartRecoveryResult> ProcesarAsync(string detalleJson, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(detalleJson);
        if (!doc.RootElement.TryGetProperty("pedidoId", out var idEl) || !idEl.TryGetInt32(out var pedidoId))
            return new("sin pedidoId en el evento", null);

        var pedido = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == pedidoId, ct);
        if (pedido is null) return new($"pedido {pedidoId} no existe", null);

        await EnsureSchemaAsync(ct);
        var hash = HashCorreo(pedido.Email);
        // Reserva ATOMICA del cupo de 24 h: si dos cancelaciones del mismo cliente llegan a la vez
        // (concurrencia de la Lambda), solo una obtiene la reserva.
        var reserva = await ReservarCupoAsync(hash, ct);
        if (reserva is null) return new("omitido: ya recibio un correo en las ultimas 24 h", null);

        try
        {
            var ids = pedido.Items.Select(i => i.ProductId).ToList();
            var nombres = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Name).ToListAsync(ct);
            var contexto = $"Cancelo un pedido de S/ {pedido.Total:0.00}. Motivo: {pedido.CancelReason ?? "no indicado"}. " +
                           $"Productos: {string.Join(", ", nombres)}.";
            var oferta = await agente.RunAsync(contexto, ct: ct);
            if (!oferta.Aprobado)
            {
                await LiberarCupoAsync(reserva.Value, ct);
                return new($"no enviado: el critico no aprobo tras {oferta.Vueltas} vueltas", oferta);
            }
            await correo.EnviarAsync(pedido.Email, "Tu carrito te espera en URPOnline", oferta.Correo, ct);
            return new("enviado", oferta);
        }
        catch
        {
            await LiberarCupoAsync(reserva.Value, CancellationToken.None);   // no se envio: el cupo vuelve a quedar libre
            throw;                                                          // SQS reintenta el mensaje
        }
    }

    // El correo se guarda como hash: la tabla de control no contiene datos personales.
    public static string HashCorreo(string email) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()))).ToLowerInvariant();

    // Devuelve el Id de la reserva, o null si el cliente ya tiene un correo en las ultimas 24 h.
    // pg_advisory_xact_lock serializa por cliente: la comprobacion y la insercion son una sola operacion.
    private async Task<long?> ReservarCupoAsync(string hash, CancellationToken ct)
    {
        var conn = await OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using (var lockCmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtext(@h));", conn, tx))
        {
            lockCmd.Parameters.AddWithValue("h", hash);
            await lockCmd.ExecuteNonQueryAsync(ct);
        }
        await using var cmd = new NpgsqlCommand("""
         INSERT INTO "CartEmailsSent" ("EmailHash")
         SELECT @h WHERE NOT EXISTS (
            SELECT 1 FROM "CartEmailsSent" WHERE "EmailHash"=@h AND "SentUtc" >= now() - interval '24 hours')
         RETURNING "Id";
         """, conn, tx);
        cmd.Parameters.AddWithValue("h", hash);
        var id = await cmd.ExecuteScalarAsync(ct);
        await tx.CommitAsync(ct);
        return id is long l ? l : null;
    }

    private async Task LiberarCupoAsync(long id, CancellationToken ct)
    {
        var conn = await OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("""DELETE FROM "CartEmailsSent" WHERE "Id"=@id;""", conn);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }
}
