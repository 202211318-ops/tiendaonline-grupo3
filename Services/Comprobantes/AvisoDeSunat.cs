using Microsoft.AspNetCore.SignalR;
using URPOnline.Contable.Contratos;

namespace TiendaOnline.Services.Comprobantes;

// La llamada de vuelta del sistema contable (JSON-RPC, por el mismo socket que abrió la tienda): cuando SUNAT
// responde, se publica por SignalR solo al grupo del pedido, para que nadie vea la factura de otro cliente.
public sealed class AvisoDeSunat(IHubContext<ComprobantesHub> hub, ILogger<AvisoDeSunat> log) : IResultadoComprobante
{
    public const string Evento = "ResultadoComprobante";

    public async Task NotificarResultadoAsync(ResultadoSunat r, CancellationToken ct)
    {
        if (!Facturador.PedidoDelToken(r.Token, out var pedidoId))
        {
            log.LogWarning("Resultado de SUNAT con un token que no es de un pedido: {Token}", r.Token);
            return;
        }
        log.LogInformation("SUNAT {Resultado} {Comprobante} del pedido {Pedido}: {Codigo} {Descripcion}",
            r.Aceptado ? "aceptó" : "rechazó", r.Comprobante, pedidoId, r.Codigo, r.Descripcion);
        await hub.Clients.Group(ComprobantesHub.Grupo(pedidoId)).SendAsync(Evento,
            new { r.Comprobante, r.Aceptado, r.Codigo, r.Descripcion, r.Observaciones }, ct);
    }
}
