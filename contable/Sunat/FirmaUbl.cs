using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace URPOnline.Contable.Sunat;

// Firma XMLDSig "enveloped" del comprobante, como pide el Manual del Programador de SUNAT (punto 3.2):
// se firma el documento completo (Reference URI="") y la ds:Signature va dentro de
// ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent.
// Desafio G7: el ejemplo del manual usa rsa-sha1; aqui el algoritmo es un parametro (Sunat:AlgoritmoFirma).
// Desafio G8: hoy la clave sale del certificado; el grupo la mueve a la caja HSM (SoftHSM2 + Pkcs11Interop).
public static class FirmaUbl
{
    public const string IdFirma = "SignatureSP";

    public static byte[] Firmar(XDocument factura, X509Certificate2 certificado, string algoritmo)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        using (var lector = factura.CreateReader()) doc.Load(lector);

        using var clave = certificado.GetRSAPrivateKey() ?? throw new InvalidOperationException("El certificado no trae clave privada RSA.");
        var firma = new SignedXml(doc) { SigningKey = clave };
        firma.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        firma.SignedInfo.SignatureMethod = algoritmo == "sha1" ? SignedXml.XmlDsigRSASHA1Url : SignedXml.XmlDsigRSASHA256Url;
        var referencia = new Reference(string.Empty)
        {
            DigestMethod = algoritmo == "sha1" ? SignedXml.XmlDsigSHA1Url : SignedXml.XmlDsigSHA256Url
        };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        firma.AddReference(referencia);
        var info = new KeyInfo();
        info.AddClause(new KeyInfoX509Data(certificado));
        firma.KeyInfo = info;
        firma.Signature.Id = IdFirma;
        firma.ComputeSignature();

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2");
        var contenedor = doc.SelectSingleNode("//ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent", ns)
            ?? throw new InvalidOperationException("La factura no trae ext:ExtensionContent.");
        contenedor.AppendChild(doc.ImportNode(firma.GetXml(), true));

        using var salida = new MemoryStream();
        using (var escritor = XmlWriter.Create(salida, new XmlWriterSettings { Encoding = new UTF8Encoding(false) })) doc.Save(escritor);
        return salida.ToArray();
    }

    // La usa la prueba y la puede usar el grupo para verificar lo que va a enviar.
    public static bool Verificar(byte[] xmlFirmado, X509Certificate2 certificado)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(new MemoryStream(xmlFirmado));
        var nodo = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (nodo.Count != 1) return false;
        var firma = new SignedXml(doc);
        firma.LoadXml((XmlElement)nodo[0]!);
        using var publica = certificado.GetRSAPublicKey()!;
        return firma.CheckSignature(publica);
    }
}
