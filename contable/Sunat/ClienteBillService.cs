using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace URPOnline.Contable.Sunat;

public sealed record CredencialSol(string Ruc, string Usuario, string Clave);

public sealed record RespuestaSunat(bool Aceptado, string Codigo, string Descripcion, IReadOnlyList<string> Observaciones);

public interface IEnviadorSunat
{
    Task<RespuestaSunat> EnviarFacturaAsync(string nombreArchivo, byte[] xmlFirmado, CancellationToken ct);
}

// Cliente del servicio web SOAP billService de SUNAT (Manual del Programador, puntos 2.1, 2.2 y 2.4):
//  - WS-Security con UsernameToken: Username = RUC + usuario SOL, Password = clave SOL.
//  - sendBill recibe el nombre del ZIP y su contenido; devuelve otro ZIP con la constancia (CDR).
// Desafio G9: ante un timeout NO se reenvia a ciegas; primero getStatusCdr (eso lo agrega el grupo).
public sealed class ClienteBillService(HttpClient http, Uri url, CredencialSol credencial) : IEnviadorSunat
{
    static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    static readonly XNamespace Ser = "http://service.sunat.gob.pe";
    static readonly XNamespace Wsse = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    public async Task<RespuestaSunat> EnviarFacturaAsync(string nombreArchivo, byte[] xmlFirmado, CancellationToken ct)
    {
        var sobre = new XDocument(new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soap), new XAttribute(XNamespace.Xmlns + "ser", Ser),
            new XAttribute(XNamespace.Xmlns + "wsse", Wsse),
            new XElement(Soap + "Header", new XElement(Wsse + "Security", new XElement(Wsse + "UsernameToken",
                new XElement(Wsse + "Username", credencial.Ruc + credencial.Usuario),
                new XElement(Wsse + "Password", credencial.Clave)))),
            new XElement(Soap + "Body", new XElement(Ser + "sendBill",
                new XElement("fileName", nombreArchivo + ".zip"),
                new XElement("contentFile", Convert.ToBase64String(Zip(nombreArchivo + ".xml", xmlFirmado)))))));

        using var pedido = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(sobre.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml")
        };
        pedido.Headers.Add("SOAPAction", "urn:sendBill");
        using var respuesta = await http.SendAsync(pedido, ct);
        var cuerpo = await respuesta.Content.ReadAsStringAsync(ct);
        return Interpretar(cuerpo);
    }

    // Un rechazo de estructura o de autenticacion llega como SOAP Fault; una aceptacion (o un rechazo de
    // negocio) llega como applicationResponse: el ZIP de la CDR en Base64.
    public static RespuestaSunat Interpretar(string cuerpoSoap)
    {
        var doc = XDocument.Parse(cuerpoSoap);
        var falla = doc.Descendants(Soap + "Fault").FirstOrDefault();
        if (falla is not null)
            return new RespuestaSunat(false, (string?)falla.Element("faultcode") ?? "",
                (string?)falla.Element("faultstring") ?? "", []);

        var cdrBase64 = doc.Descendants("applicationResponse").FirstOrDefault()?.Value
            ?? throw new InvalidDataException("SUNAT no devolvio ni Fault ni applicationResponse: " + Recortar(cuerpoSoap));
        var cdr = LeerCdr(Convert.FromBase64String(cdrBase64));
        var codigo = cdr.Descendants(Cbc + "ResponseCode").FirstOrDefault()?.Value ?? "";
        var descripcion = cdr.Descendants(Cbc + "Description").FirstOrDefault()?.Value ?? "";
        var notas = cdr.Descendants(Cbc + "Note").Select(n => n.Value).ToList();
        // ResponseCode 0 = aceptado; con notas = aceptado con observaciones.
        return new RespuestaSunat(codigo == "0", codigo, descripcion, notas);
    }

    static XDocument LeerCdr(byte[] zip)
    {
        using var archivo = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var xml = archivo.Entries.First(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        using var flujo = xml.Open();
        return XDocument.Load(flujo);
    }

    static byte[] Zip(string nombre, byte[] contenido)
    {
        using var salida = new MemoryStream();
        using (var archivo = new ZipArchive(salida, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entrada = archivo.CreateEntry(nombre).Open();
            entrada.Write(contenido);
        }
        return salida.ToArray();
    }

    static string Recortar(string s) => SecurityElement.Escape(s.Length > 300 ? s[..300] : s);
}
