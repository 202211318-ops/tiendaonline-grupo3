// SISTEMA CONTABLE DE URPONLINE — Fase 3, Bloque G (facturacion electronica con SUNAT).
//
//   tienda --JSON-RPC + MessagePack, TCP--> [firewall + NAT] --> Facade contable --> Gateway --SOAP--> SUNAT
//
// USO:
//   dotnet run --project contable -- servidor       Escucha a la tienda en Contable:Puerto.
//   dotnet run --project contable -- probar-beta    Levanta el servidor, se conecta como la tienda, emite UNA
//                                                   factura contra el servicio BETA de SUNAT e imprime la CDR.
// Nunca contra produccion de SUNAT sin el certificado digital del emisor y sin la orden del profesor.

using System.Net;
using URPOnline.Contable;
using URPOnline.Contable.Canal;
using URPOnline.Contable.Contratos;
using URPOnline.Contable.Facade;
using URPOnline.Contable.Sunat;

var modo = args.FirstOrDefault() ?? "servidor";
var cfg = Configuracion.Cargar(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
using var certificado = cfg.Certificado();
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var gateway = new GatewaySunat(cfg.Emisor, cfg.TasaIgv, certificado, cfg.AlgoritmoFirma,
    new ClienteBillService(http, cfg.UrlSunat, cfg.Credencial));
void Log(string m) => Console.WriteLine($"{DateTime.UtcNow:HH:mm:ss} {m}");
IContabilidad CrearFacade(IResultadoComprobante callback) => new FacadeContable(gateway, callback, e => Log("ERROR " + e));

Log($"SUNAT: {cfg.UrlSunat} · emisor {cfg.Emisor.Ruc} · firma {cfg.AlgoritmoFirma} · certificado {certificado.Subject}");
using var fin = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; fin.Cancel(); };

if (modo == "servidor")
{
    await CanalJsonRpc.EscucharAsync(new IPEndPoint(IPAddress.Any, cfg.Puerto), CrearFacade, Log, fin.Token);
    return 0;
}
if (modo != "probar-beta") { Console.Error.WriteLine("Modo desconocido: " + modo); return 2; }
if (!cfg.EsBeta) { Console.Error.WriteLine("probar-beta solo se ejecuta contra el servicio beta de SUNAT."); return 2; }

var servidor = CanalJsonRpc.EscucharAsync(new IPEndPoint(IPAddress.Loopback, cfg.Puerto), CrearFacade, Log, fin.Token);
var resultado = new TaskCompletionSource<ResultadoSunat>();
var (contable, rpc) = await CanalJsonRpc.ConectarAsync("127.0.0.1", cfg.Puerto, new ResultadoEnConsola(resultado), fin.Token);
var numero = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 99_999_999);
var venta = new Venta(Guid.NewGuid().ToString("N"), "F001", numero, "20100066603", "CLIENTE DE PRUEBA S.A.C.", "PEN",
    [new LineaVenta("P001", "Laptop 14 pulgadas", 1, 2500.00m), new LineaVenta("P002", "Mouse inalambrico", 2, 45.50m)]);
var recepcion = await contable.EmitirComprobanteAsync(venta, fin.Token);
Log($"Tienda <- {recepcion.Comprobante}: {recepcion.Estado}");
var r = await resultado.Task.WaitAsync(TimeSpan.FromSeconds(90));
Log($"Tienda <- SUNAT {(r.Aceptado ? "ACEPTO" : "RECHAZO")} {r.Comprobante} · codigo {r.Codigo} · {r.Descripcion}");
foreach (var o in r.Observaciones) Log("  observacion: " + o);
rpc.Dispose();
fin.Cancel();
await servidor;
return r.Aceptado ? 0 : 1;

sealed class ResultadoEnConsola(TaskCompletionSource<ResultadoSunat> destino) : IResultadoComprobante
{
    public Task NotificarResultadoAsync(ResultadoSunat resultado, CancellationToken ct)
    {
        destino.TrySetResult(resultado);
        return Task.CompletedTask;
    }
}
