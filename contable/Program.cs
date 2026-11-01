// SISTEMA CONTABLE DE URPONLINE — Fase 3, Bloque G (facturacion electronica con SUNAT).
//
//   tienda --JSON-RPC + MessagePack, TCP--> [firewall + NAT] --> Facade contable --> Gateway --SOAP--> SUNAT
//
// USO:
//   dotnet run --project contable -- servidor       Escucha a la tienda en Contable:Puerto.
//   dotnet run --project contable -- probar-beta    Levanta el servidor, se conecta como la tienda, emite UNA
//                                                   factura contra el servicio BETA de SUNAT e imprime la CDR.
//   dotnet run --project contable -- probar-beta-raft   Tres nodos con Raft por TCP en esta PC: factura, apaga al
//                                                   líder, se elige otro y factura de nuevo (SUNAT beta).
//   Con Raft__Id y Raft__Nodos, «servidor» corre UN nodo del registro replicado (uno por zona en AWS).
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
    if (string.IsNullOrWhiteSpace(cfg.RaftId))
    {
        await CanalJsonRpc.EscucharAsync(new IPEndPoint(IPAddress.Any, cfg.Puerto), CrearFacade, Log, fin.Token);
        return 0;
    }
    // Un nodo del sistema contable replicado con Raft (G15): escucha a la tienda en Contable:Puerto y a los
    // otros nodos en su dirección de Raft:Nodos.
    var nodos = cfg.NodosRaft();
    var (facadeRaft, nodoRaft, raftEscucha) = await NodoContable.IniciarAsync(cfg.RaftId, nodos, gateway, Log,
        Path.Combine(AppContext.BaseDirectory, $"raft-{cfg.RaftId}.json"), fin.Token);
    Log($"Nodo Raft {cfg.RaftId} · Raft en {nodos[cfg.RaftId]} · tienda en {cfg.Puerto}");
    await Task.WhenAll(raftEscucha, CanalJsonRpc.EscucharAsync(new IPEndPoint(IPAddress.Any, cfg.Puerto), facadeRaft, Log, fin.Token));
    await nodoRaft.DisposeAsync();
    return 0;
}
if (modo == "probar-beta-raft") return await PruebaRaft.CorrerAsync(cfg, gateway, Log);
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
