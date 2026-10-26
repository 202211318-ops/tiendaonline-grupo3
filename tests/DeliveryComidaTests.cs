using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TiendaOnline.Services;
using TiendaOnline.Services.Comida;
using Xunit;

namespace TiendaOnline.Tests;

// Base del delivery (fase 2, §9.9.4) contra PostgreSQL + pgvector real (corre en GitHub Actions, como las demás
// pruebas de base de datos). Sin el modelo de embeddings la búsqueda NO inventa un orden: devuelve vacío.
public class DeliveryComidaTests(BaseDeDatosFixture bd) : IClassFixture<BaseDeDatosFixture>
{
    private sealed class Entorno : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static EmbeddingService SinModelo() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Embeddings:ModelPath"] = Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid()) }).Build(),
        new Entorno(), NullLogger<EmbeddingService>.Instance);

    private static async Task<long> Contar(TiendaOnline.Data.ShopDbContext db, string sql)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task La_semilla_carga_la_carta_y_la_politica_una_sola_vez()
    {
        await using var db = bd.NuevoContexto();
        var store = new ComidaStore(db, SinModelo());
        var semilla = new ComidaSeeder(db, store);
        await semilla.SeedAsync();
        await semilla.SeedAsync();   // la segunda vez no duplica

        Assert.Equal(4, await Contar(db, "SELECT count(*) FROM restaurantes"));
        Assert.Equal(16, await Contar(db, "SELECT count(*) FROM platos"));
        Assert.Equal(6, await Contar(db, "SELECT count(*) FROM politica_delivery"));
        // Hay platos con maní a propósito: el desafío C5 debe excluirlos ante «sin maní».
        Assert.Equal(4, await Contar(db, "SELECT count(*) FROM platos WHERE 'maní' = ANY(alergenos)"));
    }

    [Fact]
    public async Task La_carta_de_un_restaurante_trae_ingredientes_y_alergenos()
    {
        await using var db = bd.NuevoContexto();
        var store = new ComidaStore(db, SinModelo());
        await new ComidaSeeder(db, store).SeedAsync();

        var restaurantes = await store.RestaurantesAsync();
        var wok = restaurantes.Single(r => r.Nombre == "Wok Miraflores");
        var platos = await store.PlatosAsync(wok.Id);

        Assert.Equal(4, platos.Count);
        var kungPao = platos.Single(p => p.Nombre == "Pollo kung pao picante");
        Assert.Contains("maní", kungPao.Ingredientes);
        Assert.Contains("maní", kungPao.Alergenos);
    }

    [Fact]
    public async Task Sin_modelo_de_embeddings_la_busqueda_no_inventa_resultados()
    {
        await using var db = bd.NuevoContexto();
        var store = new ComidaStore(db, SinModelo());
        await new ComidaSeeder(db, store).SeedAsync();

        Assert.Empty(await store.BuscarAsync("pollo picante sin maní"));
    }

    [Fact]
    public async Task El_pedido_rechaza_un_estado_que_no_existe()
    {
        await using var db = bd.NuevoContexto();
        var store = new ComidaStore(db, SinModelo());
        await new ComidaSeeder(db, store).SeedAsync();
        var rest = (await store.RestaurantesAsync())[0];

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO pedidos_comida (cliente_sub, restaurante_id, estado, total, lat, lon) VALUES ('c1', @r, 'volando', 10, -12.1, -77.0)", conn);
        cmd.Parameters.AddWithValue("r", rest.Id);

        await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
    }
}
