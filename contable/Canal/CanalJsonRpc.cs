using System.Net;
using System.Net.Sockets;
using StreamJsonRpc;
using URPOnline.Contable.Contratos;

namespace URPOnline.Contable.Canal;

// Canal binario tienda <-> sistema contable: JSON-RPC sobre TCP, cada mensaje con un prefijo de longitud
// de 4 bytes big-endian (LengthHeaderMessageHandler) y serializado con MessagePack.
// Pendiente para el grupo: TLS 1.3 mutuo con SslStream (G2) y MessagePackSecurity.UntrustedData con
// tamano maximo de mensaje (G1). SslStream es un Stream: se pone entre el NetworkStream y el handler.
public static class CanalJsonRpc
{
    static IJsonRpcMessageHandler Handler(Stream flujo) => new LengthHeaderMessageHandler(flujo, flujo, new MessagePackFormatter());

    // Lado sistema contable: acepta conexiones y por cada una crea su Facade con el callback de ESA conexion.
    public static async Task EscucharAsync(IPEndPoint punto, Func<IResultadoComprobante, IContabilidad> crearFacade,
        Action<string> log, CancellationToken ct)
    {
        var oyente = new TcpListener(punto);
        oyente.Start();
        log($"Sistema contable escuchando en {oyente.LocalEndpoint}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var cliente = await oyente.AcceptTcpClientAsync(ct);
                _ = AtenderAsync(cliente, crearFacade, log);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { oyente.Stop(); }
    }

    static async Task AtenderAsync(TcpClient cliente, Func<IResultadoComprobante, IContabilidad> crearFacade, Action<string> log)
    {
        using (cliente)
        {
            var remoto = cliente.Client.RemoteEndPoint;
            log($"Conexion de {remoto}");
            using var rpc = new JsonRpc(Handler(cliente.GetStream()));
            var callback = rpc.Attach<IResultadoComprobante>();
            rpc.AddLocalRpcTarget<IContabilidad>(crearFacade(callback), null);
            rpc.StartListening();
            try { await rpc.Completion; }
            catch (Exception e) { log($"Conexion de {remoto} terminada con error: {e.Message}"); }
            log($"Conexion de {remoto} cerrada");
        }
    }

    // Lado tienda: abre la conexion (la tienda inicia: atraviesa el NAT) y publica su callback en el mismo socket.
    public static async Task<(IContabilidad Contable, JsonRpc Rpc)> ConectarAsync(string host, int puerto,
        IResultadoComprobante alRecibirResultado, CancellationToken ct)
    {
        var cliente = new TcpClient();
        await cliente.ConnectAsync(host, puerto, ct);
        var rpc = new JsonRpc(Handler(cliente.GetStream()));
        rpc.AddLocalRpcTarget<IResultadoComprobante>(alRecibirResultado, null);
        var contable = rpc.Attach<IContabilidad>();
        rpc.StartListening();
        rpc.Disconnected += (_, _) => cliente.Dispose();
        return (contable, rpc);
    }
}
