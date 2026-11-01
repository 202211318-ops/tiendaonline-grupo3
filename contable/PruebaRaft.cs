using System.Net;
using StreamJsonRpc;
using URPOnline.Contable.Canal;
using URPOnline.Contable.Contratos;
using URPOnline.Contable.Facade;
using URPOnline.Contable.Raft;

namespace URPOnline.Contable;

// probar-beta-raft: tres nodos del sistema contable en esta PC, hablándose por TCP (JSON-RPC + MessagePack).
// 1) se elige un líder; 2) la «tienda» factura en el líder (los seguidores responden NoSoyLider); 3) se apaga el
// líder; 4) los otros dos eligen uno nuevo y la tienda vuelve a facturar. Las dos facturas van al BETA de SUNAT.
public static class PruebaRaft
{
    sealed record Nodo(string Id, int PuertoTienda, NodoRaft Raft, CancellationTokenSource Fin, Task Escuchas);

    public static async Task<int> CorrerAsync(Configuracion cfg, GatewaySunat gateway, Action<string> log)
    {
        if (!cfg.EsBeta) { log("probar-beta-raft solo se ejecuta contra el servicio beta de SUNAT."); return 2; }
        var raft = new Dictionary<string, IPEndPoint>
        {
            ["n1"] = new(IPAddress.Loopback, 7601), ["n2"] = new(IPAddress.Loopback, 7602), ["n3"] = new(IPAddress.Loopback, 7603)
        };
        var carpeta = Path.Combine(Path.GetTempPath(), "raft-demo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        var nodos = new List<Nodo>();
        foreach (var (id, i) in raft.Keys.Select((k, i) => (k, i)))
        {
            var fin = new CancellationTokenSource();
            var (facade, nodo, escuchaRaft) = await NodoContable.IniciarAsync(id, raft, gateway, log, Path.Combine(carpeta, id + ".json"), fin.Token);
            var puerto = 7451 + i;
            var escuchaTienda = CanalJsonRpc.EscucharAsync(new IPEndPoint(IPAddress.Loopback, puerto), facade, _ => { }, fin.Token);
            nodos.Add(new Nodo(id, puerto, nodo, fin, Task.WhenAll(escuchaRaft, escuchaTienda)));
        }

        var lider = await LiderAsync(nodos);
        log($"Líder elegido: {lider.Id} (término {lider.Raft.Termino})");
        var r1 = await FacturarAsync(nodos, log, "primera");
        log($"Las tres copias del log: {string.Join(" · ", nodos.Select(n => $"{n.Id}={n.Raft.Log.Count}"))}");

        log($"Se apaga el líder {lider.Id}");
        lider.Fin.Cancel(); await lider.Raft.DisposeAsync();
        var resto = nodos.Where(n => n != lider).ToList();
        var nuevo = await LiderAsync(resto);
        log($"Nuevo líder: {nuevo.Id} (término {nuevo.Raft.Termino}); su log tiene {nuevo.Raft.Log.Count} entrada(s)");
        var r2 = await FacturarAsync(resto, log, "segunda");

        foreach (var n in resto) { n.Fin.Cancel(); await n.Raft.DisposeAsync(); }
        try { Directory.Delete(carpeta, true); } catch (IOException) { }
        return r1.Aceptado && r2.Aceptado ? 0 : 1;
    }

    static async Task<Nodo> LiderAsync(List<Nodo> nodos)
    {
        var hasta = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < hasta)
        {
            var lideres = nodos.Where(n => n.Raft.Rol == Rol.Lider).ToList();
            if (lideres.Count == 1) return lideres[0];
            await Task.Delay(50);
        }
        throw new TimeoutException("No se eligió un líder");
    }

    // La «tienda»: prueba los nodos en orden hasta que uno (el líder) acepta la venta.
    static async Task<ResultadoSunat> FacturarAsync(List<Nodo> nodos, Action<string> log, string cual)
    {
        var numero = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 99_999_999);
        var venta = new Venta(Guid.NewGuid().ToString("N"), "F001", numero, "20100066603", "CLIENTE DE PRUEBA S.A.C.", "PEN",
            [new LineaVenta("P001", "Laptop 14 pulgadas", 1, 2500.00m)]);
        foreach (var n in nodos)
        {
            var resultado = new TaskCompletionSource<ResultadoSunat>();
            var (contable, rpc) = await CanalJsonRpc.ConectarAsync("127.0.0.1", n.PuertoTienda, new Buzon(resultado), default);
            try
            {
                var recepcion = await contable.EmitirComprobanteAsync(venta, default);
                log($"Tienda -> {n.Id}: {recepcion.Comprobante} {recepcion.Estado} (replicada en la mayoría)");
                var r = await resultado.Task.WaitAsync(TimeSpan.FromSeconds(90));
                log($"Tienda <- SUNAT {(r.Aceptado ? "ACEPTO" : "RECHAZO")} la {cual} factura {r.Comprobante} · código {r.Codigo} · {r.Descripcion}");
                return r;
            }
            catch (RemoteInvocationException e) { log($"Tienda -> {n.Id}: {e.Message}"); }
            finally { rpc.Dispose(); }
        }
        throw new InvalidOperationException("Ningún nodo aceptó la venta");
    }

    sealed class Buzon(TaskCompletionSource<ResultadoSunat> t) : IResultadoComprobante
    {
        public Task NotificarResultadoAsync(ResultadoSunat r, CancellationToken ct) { t.TrySetResult(r); return Task.CompletedTask; }
    }
}
