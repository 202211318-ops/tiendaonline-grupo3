using System.Collections.Concurrent;
using System.Text.Json;
using URPOnline.Contable.Contratos;
using URPOnline.Contable.Raft;

namespace URPOnline.Contable.Facade;

// El registro de comprobantes replicado con Raft (desafío G15). La venta se propone al nodo; recién cuando la
// MAYORÍA la tiene guardada, la tienda recibe «RECIBIDO». Cada nodo aplica las entradas confirmadas en orden,
// pero SOLO EL LÍDER las envía a SUNAT: así una factura nunca sale dos veces aunque haya tres nodos.
// Pendiente para el grupo (G9): si el líder cae después de confirmar y antes de enviar, el nuevo líder debe
// consultar getStatusCdr y enviar lo que falte; hoy esa entrada queda confirmada pero sin enviar.
public sealed class RegistroReplicado(GatewaySunat gateway, Action<Exception> alFallar)
{
    readonly ConcurrentDictionary<string, IResultadoComprobante> avisos = new();
    public NodoRaft? Nodo { get; set; }

    public async Task<Recepcion> RegistrarAsync(Venta venta, IResultadoComprobante aviso, CancellationToken ct)
    {
        var nodo = Nodo ?? throw new InvalidOperationException("El registro no tiene nodo Raft.");
        avisos[venta.Token] = aviso;
        try { await nodo.ProponerAsync(JsonSerializer.Serialize(venta), TimeSpan.FromSeconds(10), ct); }
        catch { avisos.TryRemove(venta.Token, out _); throw; }   // NoSoyLider: la tienda reintenta en otro nodo
        return new Recepcion(venta.Token, gateway.Comprobante(venta), "RECIBIDO");
    }

    // La máquina de estados de Raft: la llama el nodo con cada entrada confirmada, en orden.
    public void Aplicar(Entrada entrada)
    {
        if (Nodo?.Rol != Rol.Lider) return;   // los seguidores la guardan, pero no la envían
        var venta = JsonSerializer.Deserialize<Venta>(entrada.Comando)!;
        _ = Task.Run(() => EnviarAsync(venta));
    }

    async Task EnviarAsync(Venta venta)
    {
        avisos.TryRemove(venta.Token, out var aviso);
        ResultadoSunat resultado;
        try { resultado = await gateway.EmitirAsync(venta, CancellationToken.None); }
        catch (Exception e)
        {
            alFallar(e);
            resultado = new ResultadoSunat(venta.Token, gateway.Comprobante(venta), false, "ERROR", e.Message, []);
        }
        if (aviso is null) return;   // la tienda se conectó a otro nodo; el resultado queda en el log de este
        try { await aviso.NotificarResultadoAsync(resultado, CancellationToken.None); }
        catch (Exception e) { alFallar(e); }
    }
}
