using Microsoft.AspNetCore.SignalR;

namespace TiendaOnline.Services.Comprobantes;

// Fase 3, desafío G10: el navegador se conecta a este hub y recibe, sin recargar, si SUNAT aceptó o rechazó
// la factura de su pedido. Con varios pods, el backplane Redis (Program.cs) hace que el aviso llegue aunque
// el navegador esté conectado a otro pod que el que recibió el resultado.
public class ComprobantesHub : Hub
{
    public const string Ruta = "/hubs/comprobantes";
    public const string ClaveSesion = "mis-pedidos";   // la misma que usa CheckoutController

    public static string Grupo(int pedidoId) => $"pedido-{pedidoId}";

    // Un navegador solo puede seguir los pedidos creados en SU sesión (la misma regla que para cancelarlos).
    public async Task<bool> SeguirPedido(int pedidoId)
    {
        var sesion = Context.GetHttpContext()?.Session;
        if (sesion is null || !EsDeLaSesion(sesion.GetString(ClaveSesion), pedidoId)) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, Grupo(pedidoId));
        return true;
    }

    public static bool EsDeLaSesion(string? pedidosDeLaSesion, int pedidoId) =>
        (pedidosDeLaSesion ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(pedidoId.ToString());
}
