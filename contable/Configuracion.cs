using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using URPOnline.Contable.Sunat;

namespace URPOnline.Contable;

// Configuracion desde appsettings.json; cada valor se puede reemplazar con una variable de entorno
// (Sunat__Clave, Sunat__CertificadoClave...). En AWS la clave SOL y el PIN del HSM van en Secrets Manager (G8).
// Las credenciales de appsettings.json son las PUBLICAS del servicio beta de SUNAT ([RUC]MODDATOS / MODDATOS).
public sealed record Configuracion(int Puerto, Uri UrlSunat, Emisor Emisor, CredencialSol Credencial,
    decimal TasaIgv, string AlgoritmoFirma, string CertificadoPfx, string CertificadoClave)
{
    public bool EsBeta => UrlSunat.Host.StartsWith("e-beta.", StringComparison.OrdinalIgnoreCase);

    public static Configuracion Cargar(string ruta)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(ruta));
        string Valor(string seccion, string clave) =>
            Environment.GetEnvironmentVariable($"{seccion}__{clave}")
            ?? json.RootElement.GetProperty(seccion).GetProperty(clave).ToString();
        var ruc = Valor("Sunat", "Ruc");
        return new Configuracion(
            int.Parse(Valor("Contable", "Puerto"), CultureInfo.InvariantCulture),
            new Uri(Valor("Sunat", "Url")),
            new Emisor(ruc, Valor("Sunat", "RazonSocial")),
            new CredencialSol(ruc, Valor("Sunat", "Usuario"), Valor("Sunat", "Clave")),
            decimal.Parse(Valor("Sunat", "TasaIgv"), CultureInfo.InvariantCulture),
            Valor("Sunat", "AlgoritmoFirma"),
            Valor("Sunat", "CertificadoPfx"),
            Valor("Sunat", "CertificadoClave"));
    }

    // Produccion: el certificado digital del emisor, comunicado a SUNAT (Manual, punto 3.1).
    // Beta: SUNAT no exige certificado registrado (Manual, punto 2.3), asi que sin PFX se genera uno
    // autofirmado con el RUC en el campo OU, como pide el manual. Fuera del beta, sin PFX no arranca.
    public X509Certificate2 Certificado()
    {
        if (!string.IsNullOrWhiteSpace(CertificadoPfx))
            return X509CertificateLoader.LoadPkcs12FromFile(CertificadoPfx, CertificadoClave);
        if (!EsBeta)
            throw new InvalidOperationException("Sin Sunat:CertificadoPfx solo se puede usar el servicio beta de SUNAT.");
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest($"CN={Emisor.RazonSocial} BETA, OU={Emisor.Ruc}, O={Emisor.RazonSocial}, C=PE",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var cert = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }
}
