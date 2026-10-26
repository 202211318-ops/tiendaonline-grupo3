using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TiendaOnline.Data;
using TiendaOnline.Services;
using Xunit;

namespace TiendaOnline.Tests;

// Fase 3 (B3): lecturas a la replica, escrituras al primario (sin AWS ni base de datos).
public class LecturaReplicaTests
{
    private const string Primario = "Host=primario;Database=urponline;Username=urponline";

    private static IConfiguration Config(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => (string?)v.Valor)).Build();

    [Fact]
    public void Sin_replica_lee_del_primario() =>
        Assert.Equal(Primario, DatabaseSecret.BuildReadConnectionString(Config(), Primario));

    [Fact]
    public void Con_replica_lee_de_la_replica()
    {
        var replica = "Host=replica;Database=urponline;Username=urponline";
        Assert.Equal(replica, DatabaseSecret.BuildReadConnectionString(Config(("ConnectionStrings:PostgresLectura", replica)), Primario));
    }

    [Fact]
    public async Task El_contexto_de_lectura_no_escribe()
    {
        var opciones = new DbContextOptionsBuilder<ShopReadDbContext>().UseNpgsql("Host=replica").Options;
        await using var db = new ShopReadDbContext(opciones);
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }
}
