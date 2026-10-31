using MessagePack;
using StreamJsonRpc;

namespace URPOnline.Contable.Contratos;

// Contrato del canal tienda -> sistema contable (JSON-RPC + MessagePack sobre TCP).
// La tienda llama EmitirComprobanteAsync; el sistema contable responde "recibido" en el acto y,
// cuando SUNAT contesta, llama NotificarResultadoAsync POR EL MISMO SOCKET (no abre otra conexion:
// con un NAT en el medio, una conexion nueva desde el contable hacia la tienda no tendria ruta).
[JsonRpcContract]
public partial interface IContabilidad
{
    Task<Recepcion> EmitirComprobanteAsync(Venta venta, CancellationToken ct);
}

// Lo implementa la tienda: es la llamada de vuelta del sistema contable.
[JsonRpcContract]
public partial interface IResultadoComprobante
{
    Task NotificarResultadoAsync(ResultadoSunat resultado, CancellationToken ct);
}

// Montos en decimal, moneda ISO 4217. El IGV lo calcula el sistema contable, no la tienda.
[MessagePackObject]
public sealed record Venta(
    [property: Key(0)] string Token,          // unico por venta (idempotencia, desafio G9)
    [property: Key(1)] string Serie,          // F001 = factura
    [property: Key(2)] int Numero,
    [property: Key(3)] string ClienteRuc,
    [property: Key(4)] string ClienteRazonSocial,
    [property: Key(5)] string Moneda,
    [property: Key(6)] IReadOnlyList<LineaVenta> Lineas);

[MessagePackObject]
public sealed record LineaVenta(
    [property: Key(0)] string Codigo,
    [property: Key(1)] string Descripcion,
    [property: Key(2)] decimal Cantidad,
    [property: Key(3)] decimal ValorUnitario); // sin IGV

[MessagePackObject]
public sealed record Recepcion(
    [property: Key(0)] string Token,
    [property: Key(1)] string Comprobante,    // F001-1
    [property: Key(2)] string Estado);        // RECIBIDO

[MessagePackObject]
public sealed record ResultadoSunat(
    [property: Key(0)] string Token,
    [property: Key(1)] string Comprobante,
    [property: Key(2)] bool Aceptado,
    [property: Key(3)] string Codigo,         // ResponseCode de la CDR, o el codigo del SOAP Fault
    [property: Key(4)] string Descripcion,
    [property: Key(5)] IReadOnlyList<string> Observaciones);
