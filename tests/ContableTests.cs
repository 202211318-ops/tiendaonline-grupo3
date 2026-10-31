using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using URPOnline.Contable.Canal;
using URPOnline.Contable.Contratos;
using URPOnline.Contable.Facade;
using URPOnline.Contable.Sunat;
using Xunit;

namespace TiendaOnline.Tests;

// Fase 3, Bloque G: el sistema contable (Facade + Gateway a SUNAT). Sin red: SUNAT se reemplaza por un
// enviador de prueba que solo registra lo que recibio. La prueba real contra SUNAT beta es:
//   dotnet run --project contable -- probar-beta
public class ContableTests
{
    static readonly Emisor Emisor = new("20123456789", "URPONLINE S.A.C.");
    static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    static Venta UnaVenta(int numero = 1) => new("tok-" + numero, "F001", numero, "20100066603", "CLIENTE S.A.C.", "PEN",
        [new LineaVenta("P001", "Laptop", 1, 2500.00m), new LineaVenta("P002", "Mouse", 2, 45.50m)]);

    static X509Certificate2 Certificado()
    {
        using var rsa = RSA.Create(2048);
        var pedido = new CertificateRequest("CN=PRUEBA, OU=20123456789", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = pedido.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx), null);
    }

    [Fact]
    public void La_factura_calcula_base_igv_y_total()
    {
        var xml = FacturaUbl.Crear(Emisor, UnaVenta(), 0.18m, new DateTime(2026, 10, 31, 12, 0, 0));
        string Total(string nombre) => xml.Descendants(Cbc + nombre).First().Value;
        Assert.Equal("2591.00", Total("LineExtensionAmount"));   // 2500 + 2 x 45.50
        Assert.Equal("466.38", xml.Descendants(Cbc + "TaxAmount").First().Value); // 450.00 + 16.38
        Assert.Equal("3057.38", Total("PayableAmount"));
        Assert.Equal("01", Total("InvoiceTypeCode"));
        Assert.Equal("20123456789-01-F001-1", FacturaUbl.NombreArchivo(Emisor, UnaVenta()));
    }

    [Theory]
    [InlineData("sha256")]
    [InlineData("sha1")]
    public void La_firma_va_en_ExtensionContent_y_verifica(string algoritmo)
    {
        using var cert = Certificado();
        var firmado = FirmaUbl.Firmar(FacturaUbl.Crear(Emisor, UnaVenta(), 0.18m, DateTime.Now), cert, algoritmo);
        var doc = XDocument.Load(new MemoryStream(firmado));
        XNamespace ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
        XNamespace ds = "http://www.w3.org/2000/09/xmldsig#";
        var firma = doc.Descendants(ext + "ExtensionContent").Single().Element(ds + "Signature");
        Assert.NotNull(firma);
        Assert.Equal(FirmaUbl.IdFirma, (string?)firma!.Attribute("Id"));
        Assert.True(FirmaUbl.Verificar(firmado, cert));
    }

    [Fact]
    public void Un_monto_alterado_despues_de_firmar_no_verifica()
    {
        using var cert = Certificado();
        var firmado = FirmaUbl.Firmar(FacturaUbl.Crear(Emisor, UnaVenta(), 0.18m, DateTime.Now), cert, "sha256");
        var alterado = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(firmado).Replace(">3057.38<", ">1.00<"));
        Assert.NotEqual(firmado, alterado);
        Assert.False(FirmaUbl.Verificar(alterado, cert));
    }

    [Fact]
    public void Un_SOAP_Fault_de_SUNAT_es_un_rechazo_con_su_codigo()
    {
        const string fault = """
            <soap-env:Envelope xmlns:soap-env="http://schemas.xmlsoap.org/soap/envelope/"><soap-env:Body>
            <soap-env:Fault><faultcode>soap-env:Client.0102</faultcode><faultstring>Usuario o contrasena incorrectos</faultstring></soap-env:Fault>
            </soap-env:Body></soap-env:Envelope>
            """;
        var r = ClienteBillService.Interpretar(fault);
        Assert.False(r.Aceptado);
        Assert.Equal("soap-env:Client.0102", r.Codigo);
    }

    sealed class SunatDePrueba : IEnviadorSunat
    {
        public readonly List<string> Recibidos = [];
        public Task<RespuestaSunat> EnviarFacturaAsync(string nombre, byte[] xml, CancellationToken ct)
        {
            lock (Recibidos) Recibidos.Add(nombre);
            return Task.FromResult(new RespuestaSunat(true, "0", $"La Factura {nombre} ha sido aceptada", []));
        }
    }

    sealed class Buzon(TaskCompletionSource<ResultadoSunat> t) : IResultadoComprobante
    {
        public Task NotificarResultadoAsync(ResultadoSunat r, CancellationToken ct) { t.TrySetResult(r); return Task.CompletedTask; }
    }

    [Fact]
    public async Task La_tienda_recibe_RECIBIDO_y_despues_el_resultado_por_el_mismo_socket()
    {
        using var cert = Certificado();
        var sunat = new SunatDePrueba();
        var gateway = new GatewaySunat(Emisor, 0.18m, cert, "sha256", sunat);
        var puerto = PuertoLibre();
        using var fin = new CancellationTokenSource();
        var errores = new List<Exception>();
        var servidor = CanalJsonRpc.EscucharAsync(new IPEndPoint(IPAddress.Loopback, puerto),
            cb => new FacadeContable(gateway, cb, errores.Add), _ => { }, fin.Token);

        var resultado = new TaskCompletionSource<ResultadoSunat>();
        var (contable, rpc) = await CanalJsonRpc.ConectarAsync("127.0.0.1", puerto, new Buzon(resultado), fin.Token);
        var recepcion = await contable.EmitirComprobanteAsync(UnaVenta(7), fin.Token);
        var r = await resultado.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal("RECIBIDO", recepcion.Estado);
        Assert.True(r.Aceptado);
        Assert.Equal("F001-7", r.Comprobante);
        Assert.Equal("tok-7", r.Token);
        Assert.Equal(["20123456789-01-F001-7"], sunat.Recibidos);
        Assert.Empty(errores);
        rpc.Dispose();
        fin.Cancel();
        await servidor;
    }

    static int PuertoLibre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }
}
