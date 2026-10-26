using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;

namespace TiendaOnline.Services.Comida;

// Datos de PRUEBA del laboratorio (§9.9.4): restaurantes ficticios de Lima, su carta con ingredientes y
// alergenos, y la politica del delivery que usa el soporte (RAG). Solo siembra si las tablas estan vacias.
// Hay platos con mani a proposito: el desafio C5 debe excluirlos cuando el cliente pide «sin mani».
public sealed class ComidaSeeder(ShopDbContext db, ComidaStore comida)
{
    private static readonly (string Nombre, double Lat, double Lon)[] Restaurantes =
    {
        ("La Brasa de Barranco", -12.1490, -77.0215),
        ("Wok Miraflores", -12.1211, -77.0297),
        ("Mar y Limón Surquillo", -12.1125, -77.0127),
        ("Huerta Verde San Isidro", -12.0977, -77.0365),
    };

    private static readonly (int Indice, string Nombre, string Desc, decimal Precio, string[] Ingr, string[] Alerg)[] Platos =
    {
        (0, "Pollo a la brasa con papas", "Cuarto de pollo a la brasa con papas fritas y ensalada", 32.90m, ["pollo","papa","lechuga","tomate","ají"], []),
        (0, "Anticuchos de corazón", "Brochetas de corazón a la parrilla con papa dorada", 28.00m, ["corazón de res","papa","ají panca","ajo"], []),
        (0, "Pollo picante con salsa de maní", "Pollo a la parrilla bañado en salsa picante de maní", 30.50m, ["pollo","maní","ají","cebolla"], ["maní"]),
        (0, "Ají de gallina", "Gallina deshilachada en crema de ají amarillo con pecanas y queso", 27.00m, ["gallina","ají amarillo","pan","pecanas","queso","leche"], ["frutos secos","lácteos","gluten"]),
        (1, "Arroz chaufa de pollo", "Arroz salteado al wok con pollo, huevo y cebolla china", 24.90m, ["arroz","pollo","huevo","cebolla china","sillao"], ["huevo","soya"]),
        (1, "Pollo kung pao picante", "Pollo salteado con maní tostado y ají seco", 29.90m, ["pollo","maní","ají seco","pimiento"], ["maní","soya"]),
        (1, "Tallarín saltado de carne", "Fideos salteados al wok con lomo y verduras", 31.00m, ["fideos","res","cebolla","tomate","sillao"], ["gluten","soya"]),
        (1, "Wantán frito", "Masa crocante rellena de cerdo con salsa tamarindo", 15.00m, ["harina","cerdo","tamarindo"], ["gluten"]),
        (2, "Ceviche clásico", "Pescado del día en leche de tigre con camote y choclo", 34.00m, ["pescado","limón","cebolla","camote","choclo","ají limo"], ["pescado"]),
        (2, "Arroz con mariscos", "Arroz norteño con mariscos y salsa criolla", 36.50m, ["arroz","langostino","calamar","choros","ají panca"], ["mariscos"]),
        (2, "Chicharrón de pescado", "Trozos de pescado frito con yuca y salsa tártara", 30.00m, ["pescado","harina","yuca","huevo"], ["pescado","gluten","huevo"]),
        (2, "Causa limeña de pollo", "Papa amarilla con ají, rellena de pollo y palta", 22.00m, ["papa amarilla","pollo","palta","mayonesa","limón"], ["huevo"]),
        (3, "Ensalada de quinua", "Quinua con palta, tomate, pepino y vinagreta de limón", 21.50m, ["quinua","palta","tomate","pepino","limón"], []),
        (3, "Tallarines verdes con pollo", "Fideos en salsa de albahaca y espinaca con pollo a la plancha", 26.00m, ["fideos","albahaca","espinaca","queso","pollo"], ["gluten","lácteos"]),
        (3, "Bowl picante de pollo y camote", "Pollo especiado con camote asado y ají de huacatay", 25.50m, ["pollo","camote","huacatay","ají"], []),
        (3, "Tostadas con mantequilla de maní", "Pan integral con mantequilla de maní y plátano", 14.00m, ["pan integral","maní","plátano"], ["maní","gluten"]),
    };

    private static readonly (string Seccion, string Texto)[] Politica =
    {
        ("tiempo_entrega", "El tiempo de entrega prometido es de 35 a 50 minutos desde que el restaurante confirma el pedido. Si pasa de 60 minutos, el cliente puede pedir el reembolso del costo de envío."),
        ("comida_fria", "Si la comida llega fría o en mal estado, el cliente debe reportarlo por el chat dentro de los 30 minutos de recibida, con una foto. Se reembolsa el plato afectado."),
        ("reembolsos", "Los reembolsos de hasta 50 soles se aprueban de inmediato. Los mayores a 50 soles los revisa una persona del equipo de URPOnline antes de aprobarse."),
        ("alergenos", "Cada plato indica sus alérgenos. URPOnline no garantiza la ausencia de trazas: los restaurantes cocinan en el mismo espacio. Ante una alergia grave, el cliente debe confirmarla con el restaurante."),
        ("cancelacion", "El cliente puede cancelar sin costo mientras el pedido no haya sido aceptado por un repartidor. Después de aceptado, la cancelación cobra el costo de envío."),
        ("repartidor", "Si el repartidor no llega a recoger el pedido en 20 minutos, el sistema ofrece el pedido a otros repartidores cercanos. Si nadie lo acepta, se avisa al cliente y no se le cobra."),
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        await using (var hay = new NpgsqlCommand("SELECT count(*) FROM restaurantes", conn))
            if (Convert.ToInt64(await hay.ExecuteScalarAsync(ct)) == 0)
            {
                var ids = new List<int>();
                foreach (var (nombre, lat, lon) in Restaurantes)
                {
                    await using var ins = new NpgsqlCommand("INSERT INTO restaurantes (nombre, lat, lon) VALUES (@n, @la, @lo) RETURNING id", conn);
                    ins.Parameters.AddWithValue("n", nombre); ins.Parameters.AddWithValue("la", lat); ins.Parameters.AddWithValue("lo", lon);
                    ids.Add(Convert.ToInt32(await ins.ExecuteScalarAsync(ct)));
                }
                foreach (var p in Platos)
                {
                    await using var ins = new NpgsqlCommand("""
                        INSERT INTO platos (restaurante_id, nombre, descripcion, precio, ingredientes, alergenos)
                        VALUES (@r, @n, @d, @p, @i, @a)
                        """, conn);
                    ins.Parameters.AddWithValue("r", ids[p.Indice]); ins.Parameters.AddWithValue("n", p.Nombre);
                    ins.Parameters.AddWithValue("d", p.Desc); ins.Parameters.AddWithValue("p", p.Precio);
                    ins.Parameters.AddWithValue("i", p.Ingr); ins.Parameters.AddWithValue("a", p.Alerg);
                    await ins.ExecuteNonQueryAsync(ct);
                }
            }
        await using (var hay = new NpgsqlCommand("SELECT count(*) FROM politica_delivery", conn))
            if (Convert.ToInt64(await hay.ExecuteScalarAsync(ct)) == 0)
                foreach (var (seccion, texto) in Politica)
                {
                    await using var ins = new NpgsqlCommand("INSERT INTO politica_delivery (seccion, contenido) VALUES (@s, @c)", conn);
                    ins.Parameters.AddWithValue("s", seccion); ins.Parameters.AddWithValue("c", texto);
                    await ins.ExecuteNonQueryAsync(ct);
                }
        await comida.CompletarEmbeddingsAsync(ct);
    }
}
