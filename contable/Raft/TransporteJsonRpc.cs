using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using StreamJsonRpc;

namespace URPOnline.Contable.Raft;

// Los nodos del sistema contable se hablan por JSON-RPC + MessagePack sobre TCP, como la tienda con el contable.
// Pendiente para el grupo: TLS 1.3 mutuo entre nodos (G2) y la NACL que los separa por zona (G4).
public sealed class TransporteJsonRpc(IReadOnlyDictionary<string, IPEndPoint> direcciones) : ITransporteRaft, IDisposable
{
    readonly ConcurrentDictionary<string, (JsonRpc Rpc, INodoRaftRpc Proxy)> conexiones = new();

    static IJsonRpcMessageHandler Handler(Stream s) => new LengthHeaderMessageHandler(s, s, new MessagePackFormatter());

    async Task<INodoRaftRpc> ProxyAsync(string destino, CancellationToken ct)
    {
        if (conexiones.TryGetValue(destino, out var c) && !c.Rpc.IsDisposed) return c.Proxy;
        var cliente = new TcpClient();
        await cliente.ConnectAsync(direcciones[destino], ct);
        var rpc = new JsonRpc(Handler(cliente.GetStream()));
        var proxy = rpc.Attach<INodoRaftRpc>();
        rpc.StartListening();
        rpc.Disconnected += (_, _) => { conexiones.TryRemove(destino, out _); cliente.Dispose(); };
        conexiones[destino] = (rpc, proxy);
        return proxy;
    }

    async Task<T> LlamarAsync<T>(string destino, Func<INodoRaftRpc, Task<T>> llamada, CancellationToken ct)
    {
        try { return await llamada(await ProxyAsync(destino, ct)); }
        catch { if (conexiones.TryRemove(destino, out var c)) c.Rpc.Dispose(); throw; }
    }

    public Task<RespuestaVoto> PedirVotoAsync(string destino, PedidoVoto p, CancellationToken ct) =>
        LlamarAsync(destino, n => n.PedirVotoAsync(p, ct), ct);
    public Task<RespuestaAgregar> AgregarEntradasAsync(string destino, PedidoAgregar p, CancellationToken ct) =>
        LlamarAsync(destino, n => n.AgregarEntradasAsync(p, ct), ct);

    // Lado servidor: atiende los RPC de Raft que llegan a este nodo.
    public static async Task EscucharAsync(IPEndPoint punto, NodoRaft nodo, CancellationToken ct)
    {
        var oyente = new TcpListener(punto);
        oyente.Start();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var cliente = await oyente.AcceptTcpClientAsync(ct);
                var rpc = new JsonRpc(Handler(cliente.GetStream()));
                rpc.AddLocalRpcTarget<INodoRaftRpc>(nodo, null);
                rpc.Disconnected += (_, _) => cliente.Dispose();
                rpc.StartListening();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { oyente.Stop(); }
    }

    public void Dispose() { foreach (var c in conexiones.Values) c.Rpc.Dispose(); }
}
