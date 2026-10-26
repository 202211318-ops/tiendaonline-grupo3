using Npgsql;

namespace URPOnline.Delivery;

// Todo el SQL de la base del delivery. Solo el CAMINO FELIZ: la escritura condicional de la aceptacion (B2),
// la maquina de estados (A3), la idempotencia (B3) y la sincronizacion incremental (B4) las construye el grupo.
public sealed class Datos(NpgsqlDataSource db)
{
    private const string ColumnasPedido = "id, estado, version, restaurante_id, repartidor_id, total, lat, lon, actualizado_en";

    private static Pedido LeerPedido(NpgsqlDataReader r) => new(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3),
        r.IsDBNull(4) ? null : r.GetInt32(4), r.GetDecimal(5), r.GetDouble(6), r.GetDouble(7), r.GetDateTime(8));

    // Crea el pedido con sus platos; el total lo calcula el servidor con los precios de la carta, nunca el celular.
    public async Task<Pedido?> CrearPedidoAsync(string clienteSub, NuevoPedido p, CancellationToken ct = default)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var precios = new Dictionary<int, decimal>();
        await using (var cmd = new NpgsqlCommand(
            "SELECT id, precio FROM platos WHERE restaurante_id = @r AND disponible AND id = ANY(@ids)", conn, tx))
        {
            cmd.Parameters.AddWithValue("r", p.RestauranteId);
            cmd.Parameters.AddWithValue("ids", p.Items.Select(i => i.PlatoId).ToArray());
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct)) precios[rd.GetInt32(0)] = rd.GetDecimal(1);
        }
        if (p.Items.Any(i => !precios.ContainsKey(i.PlatoId))) return null;   // plato de otro restaurante o no disponible
        var total = p.Items.Sum(i => precios[i.PlatoId] * i.Cantidad);

        Pedido pedido;
        await using (var cmd = new NpgsqlCommand($"""
            INSERT INTO pedidos_comida (cliente_sub, restaurante_id, total, lat, lon)
            VALUES (@c, @r, @t, @la, @lo) RETURNING {ColumnasPedido}
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("c", clienteSub); cmd.Parameters.AddWithValue("r", p.RestauranteId);
            cmd.Parameters.AddWithValue("t", total); cmd.Parameters.AddWithValue("la", p.Lat); cmd.Parameters.AddWithValue("lo", p.Lon);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            await rd.ReadAsync(ct);
            pedido = LeerPedido(rd);
        }
        foreach (var i in p.Items.GroupBy(x => x.PlatoId).Select(g => new ItemPedido(g.Key, g.Sum(x => x.Cantidad))))
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO pedido_items (pedido_id, plato_id, cantidad, precio_unitario) VALUES (@p, @pl, @c, @pr)", conn, tx);
            cmd.Parameters.AddWithValue("p", pedido.Id); cmd.Parameters.AddWithValue("pl", i.PlatoId);
            cmd.Parameters.AddWithValue("c", i.Cantidad); cmd.Parameters.AddWithValue("pr", precios[i.PlatoId]);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return pedido;
    }

    public async Task<Pedido?> PedidoAsync(int id, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand($"SELECT {ColumnasPedido} FROM pedidos_comida WHERE id = @id AND NOT borrado");
        cmd.Parameters.AddWithValue("id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LeerPedido(r) : null;
    }

    // El repartidor se registra solo la primera vez que llama a la API (su sub de Cognito lo identifica).
    public async Task<int> RepartidorAsync(string sub, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("""
            INSERT INTO repartidores (cognito_sub) VALUES (@s)
            ON CONFLICT (cognito_sub) DO UPDATE SET actualizado_en = now()
            RETURNING id
            """);
        cmd.Parameters.AddWithValue("s", sub);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<List<Oferta>> OfertasAsync(int repartidorId, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("""
            SELECT o.pedido_id, max(o.intento), r.nombre, p.total
            FROM ofertas o JOIN pedidos_comida p ON p.id = o.pedido_id JOIN restaurantes r ON r.id = p.restaurante_id
            WHERE o.repartidor_id = @rep AND p.estado = 'ofrecido'
            GROUP BY o.pedido_id, r.nombre, p.total
            ORDER BY o.pedido_id
            """);
        cmd.Parameters.AddWithValue("rep", repartidorId);
        var lista = new List<Oferta>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(new(r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetDecimal(3)));
        return lista;
    }

    // Camino feliz: asigna sin mirar si otro ya lo tomo. Que gane solo uno (escritura condicional) es el desafio B2.
    public async Task<Pedido?> AceptarAsync(int pedidoId, int repartidorId, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand($"""
            UPDATE pedidos_comida SET repartidor_id = @rep, estado = 'aceptado', version = version + 1, actualizado_en = now()
            WHERE id = @id AND NOT borrado RETURNING {ColumnasPedido}
            """);
        cmd.Parameters.AddWithValue("rep", repartidorId); cmd.Parameters.AddWithValue("id", pedidoId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LeerPedido(r) : null;
    }

    // Camino feliz: aplica el estado que manda el celular. Rechazar saltos invalidos es el desafio A3.
    public async Task<Pedido?> CambiarEstadoAsync(int pedidoId, string estado, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand($"""
            UPDATE pedidos_comida SET estado = @e, version = version + 1, actualizado_en = now()
            WHERE id = @id AND NOT borrado RETURNING {ColumnasPedido}
            """);
        cmd.Parameters.AddWithValue("e", estado); cmd.Parameters.AddWithValue("id", pedidoId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? LeerPedido(r) : null;
    }

    public async Task<List<Pedido>> PedidosDelRepartidorAsync(int repartidorId, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand($"SELECT {ColumnasPedido} FROM pedidos_comida WHERE repartidor_id = @rep AND NOT borrado ORDER BY id");
        cmd.Parameters.AddWithValue("rep", repartidorId);
        var lista = new List<Pedido>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(LeerPedido(r));
        return lista;
    }

    public async Task<(double Lat, double Lon)?> RestauranteDelPedidoAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("SELECT r.lat, r.lon FROM pedidos_comida p JOIN restaurantes r ON r.id = p.restaurante_id WHERE p.id = @id");
        cmd.Parameters.AddWithValue("id", pedidoId);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        return await rd.ReadAsync(ct) ? (rd.GetDouble(0), rd.GetDouble(1)) : null;
    }

    public async Task<List<int>> FiltrarDisponiblesAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("SELECT id FROM repartidores WHERE disponible AND id = ANY(@ids)");
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        var lista = new List<int>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(r.GetInt32(0));
        return lista;
    }

    public async Task OfrecerAsync(int pedidoId, IReadOnlyCollection<int> repartidores, int intento, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("""
            INSERT INTO ofertas (pedido_id, repartidor_id, intento) SELECT @p, unnest(@reps), @i ON CONFLICT DO NOTHING;
            UPDATE pedidos_comida SET estado = 'ofrecido', actualizado_en = now() WHERE id = @p AND estado = 'recibido';
            """);
        cmd.Parameters.AddWithValue("p", pedidoId); cmd.Parameters.AddWithValue("reps", repartidores.ToArray());
        cmd.Parameters.AddWithValue("i", intento);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task GuardarPosicionAsync(int repartidorId, Posicion p, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("""
            INSERT INTO posiciones (repartidor_id, pedido_id, secuencia, lat, lon, hora_dispositivo)
            VALUES (@r, NULLIF(@p, 0), @s, @la, @lo, @h)
            """);
        cmd.Parameters.AddWithValue("r", repartidorId); cmd.Parameters.AddWithValue("p", p.PedidoId);
        cmd.Parameters.AddWithValue("s", p.Secuencia); cmd.Parameters.AddWithValue("la", p.Lat);
        cmd.Parameters.AddWithValue("lo", p.Lon); cmd.Parameters.AddWithValue("h", p.HoraDispositivo.ToUniversalTime());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarcarDisponibleAsync(int? repartidorId, bool disponible, CancellationToken ct = default)
    {
        if (repartidorId is null) return;
        await using var cmd = db.CreateCommand("UPDATE repartidores SET disponible = @d, actualizado_en = now() WHERE id = @id");
        cmd.Parameters.AddWithValue("d", disponible); cmd.Parameters.AddWithValue("id", repartidorId.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Conexiones WebSocket: quien sigue que pedido.
    public async Task ConectarAsync(string conexion, string clienteSub, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("INSERT INTO conexiones_ws (connection_id, cliente_sub) VALUES (@c, @s) ON CONFLICT (connection_id) DO NOTHING");
        cmd.Parameters.AddWithValue("c", conexion); cmd.Parameters.AddWithValue("s", clienteSub);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Solo el dueño del pedido puede seguirlo.
    public async Task<bool> SeguirAsync(string conexion, int pedidoId, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("""
            UPDATE conexiones_ws c SET pedido_id = @p
            FROM pedidos_comida p WHERE c.connection_id = @c AND p.id = @p AND p.cliente_sub = c.cliente_sub
            """);
        cmd.Parameters.AddWithValue("c", conexion); cmd.Parameters.AddWithValue("p", pedidoId);
        return await cmd.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task DesconectarAsync(string conexion, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("DELETE FROM conexiones_ws WHERE connection_id = @c");
        cmd.Parameters.AddWithValue("c", conexion);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<string>> ConexionesDelPedidoAsync(int pedidoId, CancellationToken ct = default)
    {
        await using var cmd = db.CreateCommand("SELECT connection_id FROM conexiones_ws WHERE pedido_id = @p");
        cmd.Parameters.AddWithValue("p", pedidoId);
        var lista = new List<string>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) lista.Add(r.GetString(0));
        return lista;
    }
}
