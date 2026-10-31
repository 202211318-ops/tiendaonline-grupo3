using URPOnline.Contable.Contratos;

namespace URPOnline.Contable.Facade;

// Facade del sistema contable: la tienda solo ve esta puerta. Recibe la venta, responde "RECIBIDO" en el acto
// y, cuando SUNAT contesta, avisa a la tienda por el MISMO canal (callback = proxy sobre la misma conexion).
// Pendiente para el grupo: outbox y token repetido (G9), cifrado y firma del mensaje (G3), sello por fila (G11).
public sealed class FacadeContable(GatewaySunat gateway, IResultadoComprobante callback, Action<Exception> alFallar) : IContabilidad
{
    public Task<Recepcion> EmitirComprobanteAsync(Venta venta, CancellationToken ct)
    {
        if (venta.Lineas.Count == 0) throw new ArgumentException("La venta no tiene lineas.");
        _ = Task.Run(() => EnviarYAvisarAsync(venta), CancellationToken.None);
        return Task.FromResult(new Recepcion(venta.Token, gateway.Comprobante(venta), "RECIBIDO"));
    }

    async Task EnviarYAvisarAsync(Venta venta)
    {
        try
        {
            var resultado = await gateway.EmitirAsync(venta, CancellationToken.None);
            await callback.NotificarResultadoAsync(resultado, CancellationToken.None);
        }
        catch (Exception e)
        {
            // No se silencia: se reporta, y la tienda recibe el error para no quedarse esperando.
            alFallar(e);
            try
            {
                await callback.NotificarResultadoAsync(new ResultadoSunat(venta.Token, gateway.Comprobante(venta),
                    false, "ERROR", e.Message, []), CancellationToken.None);
            }
            catch (Exception aviso) { alFallar(aviso); }
        }
    }
}
