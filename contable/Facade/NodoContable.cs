using System.Net;
using URPOnline.Contable.Contratos;
using URPOnline.Contable.Raft;

namespace URPOnline.Contable.Facade;

// Arma un nodo del sistema contable replicado (G15): el nodo Raft, su transporte JSON-RPC hacia los otros nodos
// y la fábrica de Facades que la tienda usa en cada conexión.
public static class NodoContable
{
    public static async Task<(Func<IResultadoComprobante, IContabilidad> Facade, NodoRaft Nodo, Task Escucha)> IniciarAsync(
        string id, IReadOnlyDictionary<string, IPEndPoint> nodos, GatewaySunat gateway, Action<string> log, string archivo,
        CancellationToken ct)
    {
        var registro = new RegistroReplicado(gateway, e => log($"[{id}] ERROR {e.Message}"));
        var transporte = new TransporteJsonRpc(nodos);
        var nodo = new NodoRaft(id, nodos.Keys.Where(k => k != id).ToList(), transporte, new AlmacenRaft(archivo), registro.Aplicar);
        registro.Nodo = nodo;
        var escucha = TransporteJsonRpc.EscucharAsync(nodos[id], nodo, ct);
        nodo.Iniciar();
        await Task.Yield();
        return (cb => new FacadeContable(gateway, cb, e => log($"[{id}] ERROR {e.Message}"), registro), nodo, escucha);
    }
}
