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

    public async Task<Recepcion?> EmitirAsync(Order pedido, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config["Contable:Host"])) return null;
        try
        {
            var conexion = await ConexionAsync(ct);
            return await conexion.EmitirComprobanteAsync(ArmarVenta(pedido), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // La venta ya está guardada: si el contable no responde, se registra y se reintenta en el próximo
            // pedido con una conexión nueva. Que ninguna factura quede sin emitir es el desafío G9 (outbox).
            log.LogError(e, "No se pudo enviar el pedido {Pedido} al sistema contable", pedido.Id);
            await CerrarAsync();
            return null;
        }
    }

    async Task<IContabilidad> ConexionAsync(CancellationToken ct)
    {
        await cerrojo.WaitAsync(ct);
        try
        {
            if (contable is not null && rpc is { IsDisposed: false }) return contable;
            var (c, r) = await CanalJsonRpc.ConectarAsync(config["Contable:Host"]!, config.GetValue("Contable:Puerto", 7443), aviso, ct);
            (contable, rpc) = (c, r);
            log.LogInformation("Conectado al sistema contable {Host}", config["Contable:Host"]);
            return c;
        }
        finally { cerrojo.Release(); }
    }

    async Task CerrarAsync()
    {
        await cerrojo.WaitAsync();
        try { rpc?.Dispose(); (contable, rpc) = (null, null); }
        finally { cerrojo.Release(); }
    }

    public ValueTask DisposeAsync() { rpc?.Dispose(); return ValueTask.CompletedTask; }
}
