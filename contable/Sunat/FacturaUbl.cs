using System.Globalization;
using System.Xml.Linq;
using URPOnline.Contable.Contratos;

namespace URPOnline.Contable.Sunat;

public sealed record Emisor(string Ruc, string RazonSocial);

// Arma el XML UBL 2.1 de una factura (tipo 01, venta interna 0101) gravada con IGV.
// Estructura segun las guias XML de SUNAT (cpe.sunat.gob.pe). La firma se agrega despues (FirmaUbl)
// dentro de ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent, que aqui queda vacio.
public static class FacturaUbl
{
    static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    static readonly XNamespace Ds = "http://www.w3.org/2000/09/xmldsig#";

    public static string NombreArchivo(Emisor emisor, Venta venta) => $"{emisor.Ruc}-01-{venta.Serie}-{venta.Numero}";

    public static XDocument Crear(Emisor emisor, Venta venta, decimal tasaIgv, DateTime fechaLima)
    {
        var lineas = venta.Lineas.Select((l, i) => CalcularLinea(l, i + 1, tasaIgv)).ToList();
        var baseTotal = lineas.Sum(l => l.Base);
        var igvTotal = lineas.Sum(l => l.Igv);
        var id = $"{venta.Serie}-{venta.Numero}";
        var root = new XElement(Inv + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac), new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XAttribute(XNamespace.Xmlns + "ext", Ext), new XAttribute(XNamespace.Xmlns + "ds", Ds),
            new XElement(Ext + "UBLExtensions", new XElement(Ext + "UBLExtension", new XElement(Ext + "ExtensionContent"))),
            new XElement(Cbc + "UBLVersionID", "2.1"),
            new XElement(Cbc + "CustomizationID", "2.0"),
            new XElement(Cbc + "ID", id),
            new XElement(Cbc + "IssueDate", fechaLima.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XElement(Cbc + "IssueTime", fechaLima.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(Cbc + "InvoiceTypeCode", new XAttribute("listID", "0101"), "01"),
            new XElement(Cbc + "DocumentCurrencyCode", venta.Moneda),
            Firmante(emisor, id),
            Parte("AccountingSupplierParty", emisor.Ruc, emisor.RazonSocial, conDomicilio: true),
            Parte("AccountingCustomerParty", venta.ClienteRuc, venta.ClienteRazonSocial, conDomicilio: false),
            new XElement(Cac + "PaymentTerms", new XElement(Cbc + "ID", "FormaPago"), new XElement(Cbc + "PaymentMeansID", "Contado")),
            Impuesto(baseTotal, igvTotal, venta.Moneda, porcentaje: null),
            new XElement(Cac + "LegalMonetaryTotal",
                Monto("LineExtensionAmount", baseTotal, venta.Moneda),
                Monto("TaxInclusiveAmount", baseTotal + igvTotal, venta.Moneda),
                Monto("PayableAmount", baseTotal + igvTotal, venta.Moneda)),
            lineas.Select(l => Linea(l, venta.Moneda, tasaIgv)));
        return new XDocument(new XDeclaration("1.0", "UTF-8", "no"), root);
    }

    sealed record Calculada(int Orden, LineaVenta Venta, decimal Base, decimal Igv);

    static Calculada CalcularLinea(LineaVenta l, int orden, decimal tasaIgv)
    {
        var baseLinea = Math.Round(l.Cantidad * l.ValorUnitario, 2, MidpointRounding.AwayFromZero);
        return new Calculada(orden, l, baseLinea, Math.Round(baseLinea * tasaIgv, 2, MidpointRounding.AwayFromZero));
    }

    static XElement Linea(Calculada l, string moneda, decimal tasaIgv) =>
        new(Cac + "InvoiceLine",
            new XElement(Cbc + "ID", l.Orden),
            new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", "NIU"), Dec(l.Venta.Cantidad)),
            Monto("LineExtensionAmount", l.Base, moneda),
            new XElement(Cac + "PricingReference", new XElement(Cac + "AlternativeConditionPrice",
                Monto("PriceAmount", Math.Round(l.Venta.ValorUnitario * (1 + tasaIgv), 2, MidpointRounding.AwayFromZero), moneda),
                new XElement(Cbc + "PriceTypeCode", "01"))),
            Impuesto(l.Base, l.Igv, moneda, porcentaje: tasaIgv * 100),
            new XElement(Cac + "Item", new XElement(Cbc + "Description", l.Venta.Descripcion),
                new XElement(Cac + "SellersItemIdentification", new XElement(Cbc + "ID", l.Venta.Codigo))),
            new XElement(Cac + "Price", Monto("PriceAmount", l.Venta.ValorUnitario, moneda)));

    // IGV (codigo de tributo 1000, VAT). En la linea va el porcentaje y la afectacion 10 (gravado - operacion onerosa).
    static XElement Impuesto(decimal baseImponible, decimal igv, string moneda, decimal? porcentaje)
    {
        var categoria = new XElement(Cac + "TaxCategory");
        if (porcentaje is not null)
        {
            categoria.Add(new XElement(Cbc + "Percent", Dec(porcentaje.Value)));
            categoria.Add(new XElement(Cbc + "TaxExemptionReasonCode", "10"));
        }
        categoria.Add(new XElement(Cac + "TaxScheme", new XElement(Cbc + "ID", "1000"),
            new XElement(Cbc + "Name", "IGV"), new XElement(Cbc + "TaxTypeCode", "VAT")));
        return new XElement(Cac + "TaxTotal", Monto("TaxAmount", igv, moneda),
            new XElement(Cac + "TaxSubtotal", Monto("TaxableAmount", baseImponible, moneda), Monto("TaxAmount", igv, moneda), categoria));
    }

    static XElement Firmante(Emisor emisor, string id) =>
        new(Cac + "Signature", new XElement(Cbc + "ID", id),
            new XElement(Cac + "SignatoryParty",
                new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", emisor.Ruc)),
                new XElement(Cac + "PartyName", new XElement(Cbc + "Name", emisor.RazonSocial))),
            new XElement(Cac + "DigitalSignatureAttachment",
                new XElement(Cac + "ExternalReference", new XElement(Cbc + "URI", "#" + FirmaUbl.IdFirma))));

    // schemeID 6 = RUC (catalogo 06 de SUNAT).
    static XElement Parte(string rol, string ruc, string razonSocial, bool conDomicilio)
    {
        var legal = new XElement(Cac + "PartyLegalEntity", new XElement(Cbc + "RegistrationName", razonSocial));
        if (conDomicilio) legal.Add(new XElement(Cac + "RegistrationAddress", new XElement(Cbc + "AddressTypeCode", "0000")));
        return new XElement(Cac + rol, new XElement(Cac + "Party",
            new XElement(Cac + "PartyIdentification", new XElement(Cbc + "ID", new XAttribute("schemeID", "6"), ruc)),
            legal));
    }

    static XElement Monto(string nombre, decimal valor, string moneda) =>
        new(Cbc + nombre, new XAttribute("currencyID", moneda), Dec(valor));

    static string Dec(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
}
