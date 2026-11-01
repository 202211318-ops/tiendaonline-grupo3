using System.Globalization;
using StreamJsonRpc;
using TiendaOnline.Models;
using URPOnline.Contable.Canal;
using URPOnline.Contable.Contratos;

namespace TiendaOnline.Services.Comprobantes;

// Fase 3, Bloque G: la tienda le pide al sistema contable la factura de cada pedido, por JSON-RPC + MessagePack
// sobre TCP. Abre UN socket (la tienda inicia: atraviesa el firewall con NAT) y lo reutiliza; el resultado de
// SUNAT vuelve por ese mismo socket a AvisoDeSunat. Sin Contable:Host configurado no se factura (laboratorio sin
// sistema contable desplegado) y la compra sigue igual.
// Pendiente para el grupo: TLS 1.3 mutuo (G2), cifrado y firma del mensaje (G3), keepalive y reconexión (G4).
public sealed class Facturador(IConfiguration config, AvisoDeSunat aviso, ILogger<Facturador> log) : IAsyncDisposable
{
    readonly SemaphoreSlim cerrojo = new(1, 1);
    IContabilidad? contable;
    JsonRpc? rpc;

    public static string TokenDelPedido(int pedidoId) => $"pedido-{pedidoId}";
    public static bool PedidoDelToken(string token, out int pedidoId) =>
        int.TryParse(token.StartsWith("pedido-") ? token[7..] : "", out pedidoId);

    // La tienda guarda los precios con IGV incluido (Contable:PreciosIncluyenIgv); SUNAT pide el valor sin IGV.
    // La promoción ya está en Order.Total: se reparte en proporción entre las líneas.
    public Venta ArmarVenta(Order pedido)
    {
        var tasa = decimal.Parse(config["Contable:TasaIgv"] ?? "0.18", CultureInfo.InvariantCulture);
        var incluyeIgv = config.GetValue("Contable:PreciosIncluyenIgv", true);
        var bruto = pedido.Items.Sum(i => i.Quantity * i.UnitPrice);
        var factor = bruto == 0 ? 1 : pedido.Total / bruto;
        var lineas = pedido.Items.Select(i => new LineaVenta(i.ProductId.ToString(), i.ProductName, i.Quantity,
            Math.Round(i.UnitPrice * factor / (incluyeIgv ? 1 + tasa : 1), 2, MidpointRounding.AwayFromZero))).ToList();
        return new Venta(TokenDelPedido(pedido.Id), config["Contable:Serie"] ?? "F001", pedido.Id,
            config["Contable:RucCliente"] ?? "", config["Contable:RazonSocialCliente"] ?? "", "PEN", lineas);
    }

    // Contable:Host acepta un nodo («10.0.1.10») o los tres nodos con Raft («10.0.1.10,10.0.2.10,10.0.3.10»):
    // se prueban en orden hasta que uno (el líder) acepta; un seguidor responde «no es el líder».
    int actual;

    public async Task<Recepcion?> EmitirAsync(Order pedido, CancellationToken ct)
    {
        var hosts = (config["Contable:Host"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts.Length == 0) return null;
        var venta = ArmarVenta(pedido);
        for (var intento = 0; intento < hosts.Length; intento++)
        {
            var host = hosts[(actual + intento) % hosts.Length];
            try
            {
                var conexion = await ConexionAsync(host, ct);
                var recepcion = await conexion.EmitirComprobanteAsync(venta, ct);
                actual = (actual + intento) % hosts.Length;   // el próximo pedido empieza por este nodo
                return recepcion;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning("El nodo contable {Host} no aceptó el pedido {Pedido}: {Motivo}", host, pedido.Id, e.Message);
                await CerrarAsync();
            }
        }
        // La venta ya está guardada: ningún nodo la aceptó. Que ninguna factura quede sin emitir es el desafío G9 (outbox).
        log.LogError("Ningún nodo contable aceptó el pedido {Pedido}", pedido.Id);
        return null;
    }

    async Task<IContabilidad> ConexionAsync(string host, CancellationToken ct)
    {
        await cerrojo.WaitAsync(ct);
        try
        {
            if (contable is not null && rpc is { IsDisposed: false } && conectadoA == host) return contable;
            rpc?.Dispose();
            var (c, r) = await CanalJsonRpc.ConectarAsync(host, config.GetValue("Contable:Puerto", 7443), aviso, ct);
            (contable, rpc, conectadoA) = (c, r, host);
            log.LogInformation("Conectado al sistema contable {Host}", host);
            return c;
        }
        finally { cerrojo.Release(); }
    }
    string? conectadoA;

    async Task CerrarAsync()
    {
        await cerrojo.WaitAsync();
        try { rpc?.Dispose(); (contable, rpc) = (null, null); }
        finally { cerrojo.Release(); }
    }

    public ValueTask DisposeAsync() { rpc?.Dispose(); return ValueTask.CompletedTask; }
}
