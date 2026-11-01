using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using URPOnline.Contable.Contratos;
using URPOnline.Contable.Facade;
using URPOnline.Contable.Raft;
using URPOnline.Contable.Sunat;
using Xunit;

namespace TiendaOnline.Tests;

// Fase 3, desafío G15: el Facade del sistema contable sobre Raft. Tres nodos en RedEnMemoria; SUNAT se reemplaza
// por un enviador de prueba que CUENTA cuántas veces le llega cada factura (la prueba real es probar-beta-raft).
public class RegistroReplicadoTests : IAsyncLifetime
{
    readonly string carpeta = Path.Combine(Path.GetTempPath(), "registro-" + Guid.NewGuid().ToString("N"));
    readonly RedEnMemoria red = new();
    readonly Dictionary<string, (NodoRaft Nodo, FacadeContable Facade)> nodos = [];
    readonly SunatQueCuenta sunat = new();
    readonly Buzon buzon = new();
    static readonly string[] Ids = ["n1", "n2", "n3"];

    sealed class SunatQueCuenta : IEnviadorSunat
    {
        public readonly List<string> Recibidas = [];
        public Task<RespuestaSunat> EnviarFacturaAsync(string nombre, byte[] xml, CancellationToken ct)
        {
            lock (Recibidas) Recibidas.Add(nombre);
            return Task.FromResult(new RespuestaSunat(true, "0", "aceptada", []));
        }
    }

    sealed class Buzon : IResultadoComprobante
    {
        public readonly TaskCompletionSource<ResultadoSunat> Resultado = new();
        public Task NotificarResultadoAsync(ResultadoSunat r, CancellationToken ct) { Resultado.TrySetResult(r); return Task.CompletedTask; }
    }

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(carpeta);
        using var rsa = RSA.Create(2048);
        using var autofirmado = new CertificateRequest("CN=PRUEBA, OU=20123456789", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var cert = X509CertificateLoader.LoadPkcs12(autofirmado.Export(X509ContentType.Pfx), null);
        var gateway = new GatewaySunat(new Emisor("20123456789", "URPONLINE S.A.C."), 0.18m, cert, "sha256", sunat);
        foreach (var id in Ids)
        {
            var registro = new RegistroReplicado(gateway, _ => { });
            var nodo = new NodoRaft(id, Ids.Where(x => x != id).ToList(), red.Para(id), new AlmacenRaft(Path.Combine(carpeta, id + ".json")),
                registro.Aplicar, eleccionMinMs: 100, eleccionMaxMs: 200, latidoMs: 20);
            registro.Nodo = nodo; red.Registrar(nodo); nodo.Iniciar();
            nodos[id] = (nodo, new FacadeContable(gateway, buzon, _ => { }, registro));
        }
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        foreach (var (n, _) in nodos.Values) await n.DisposeAsync();
        try { Directory.Delete(carpeta, true); } catch (IOException) { }
    }

    async Task<string> Lider()
    {
        var hasta = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < hasta)
        {
            var l = nodos.Where(n => n.Value.Nodo.Rol == Rol.Lider).Select(n => n.Key).ToList();
            if (l.Count == 1) return l[0];
            await Task.Delay(20);
        }
        throw new TimeoutException("sin líder");
    }

    static Venta Venta(int n) => new("pedido-" + n, "F001", n, "20100066603", "CLIENTE S.A.C.", "PEN", [new LineaVenta("P1", "Laptop", 1, 100m)]);

    [Fact]
    public async Task La_venta_se_replica_en_los_tres_nodos_y_SUNAT_la_recibe_una_sola_vez()
    {
        var lider = await Lider();
        var recepcion = await nodos[lider].Facade.EmitirComprobanteAsync(Venta(881), default);
        var resultado = await buzon.Resultado.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("RECIBIDO", recepcion.Estado);
        Assert.True(resultado.Aceptado);
        var hasta = DateTime.UtcNow.AddSeconds(5);
        while (nodos.Values.Any(n => n.Nodo.Commit < 1) && DateTime.UtcNow < hasta) await Task.Delay(20);
        Assert.All(nodos.Values, n => Assert.Equal(1, n.Nodo.Commit));      // los tres la tienen confirmada
        await Task.Delay(300);                                                // tiempo para un envío de más, si lo hubiera
        Assert.Equal(["20123456789-01-F001-881"], sunat.Recibidas);           // pero solo el líder la envió
    }

    [Fact]
    public async Task Un_seguidor_rechaza_la_venta_y_SUNAT_no_recibe_nada()
    {
        var lider = await Lider();
        var seguidor = Ids.First(i => i != lider);
        var hasta = DateTime.UtcNow.AddSeconds(5);
        while (nodos[seguidor].Nodo.LiderConocido != lider && DateTime.UtcNow < hasta) await Task.Delay(20);
        var e = await Assert.ThrowsAsync<NoSoyLiderException>(() => nodos[seguidor].Facade.EmitirComprobanteAsync(Venta(882), default));
        Assert.Equal(lider, e.LiderConocido);
        Assert.Empty(sunat.Recibidas);
    }
}
