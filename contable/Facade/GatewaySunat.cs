using System.Security.Cryptography.X509Certificates;
using URPOnline.Contable.Contratos;
using URPOnline.Contable.Sunat;

namespace URPOnline.Contable.Facade;

// Gateway: el UNICO componente que habla con SUNAT. Arma el UBL, lo firma y lo envia.
public sealed class GatewaySunat(Emisor emisor, decimal tasaIgv, X509Certificate2 certificado, string algoritmoFirma, IEnviadorSunat sunat)
{
    static readonly TimeZoneInfo Lima = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");

    public string Comprobante(Venta venta) => $"{venta.Serie}-{venta.Numero}";

    public async Task<ResultadoSunat> EmitirAsync(Venta venta, CancellationToken ct)
    {
        var fecha = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Lima);
        var xml = FacturaUbl.Crear(emisor, venta, tasaIgv, fecha);
        var firmado = FirmaUbl.Firmar(xml, certificado, algoritmoFirma);
        var r = await sunat.EnviarFacturaAsync(FacturaUbl.NombreArchivo(emisor, venta), firmado, ct);
        return new ResultadoSunat(venta.Token, Comprobante(venta), r.Aceptado, r.Codigo, r.Descripcion, r.Observaciones);
    }
}
