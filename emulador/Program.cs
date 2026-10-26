// EMULADOR DE INTRUSION — INOFENSIVO. Fase 2, categoria 3 (Seguridad: defensa proactiva).
//
// QUE ES: una herramienta de laboratorio que CADA GRUPO ejecuta contra SU PROPIA infraestructura
// para comprobar que su monitoreo DETECTA una intrusion. NO ataca a terceros, NO explota nada,
// NO exfiltra datos. Solo GENERA LAS SENALES que una defensa bien montada debe cazar, y las imprime
// en claro (no es sigiloso: el objetivo es que se vea).
//
// Lo que hace, todo inofensivo:
//   1) Consume CPU unos segundos (simula el minero)                 -> pico de CPU en CloudWatch.
//   2) Intenta conectar a 203.0.113.10:3333 (IP RESERVADA para       -> intento de salida en VPC Flow Logs.
//      documentacion, RFC 5737: no enruta a ningun sitio real).
//   3) LEE los NOMBRES de variables de entorno de credenciales       -> acceso al entorno, sin enviar nada.
//      (no su valor, y no las manda a ningun lado).
//   4) Deja un archivo marcador en el frontend                       -> "control tomado" visible, sin romper nada.
//
// Variante B (Mythos): ademas intenta una conexion TLS a un host con nombre que no coincide con el
// certificado y registra la ANOMALIA de certificado; NO rompe ningun cifrado (eso es solo teoria:
// "cosechar ahora, descifrar despues"; RSA/ECC serian el blanco, AES-256 y SHA-256 resisten).
//
// USO:  dotnet run --project emulador -- [A|B] [ruta_marcador]
//       A = troyano/minero (por defecto) · B = Mythos
// El grupo lo ejecuta en su EC2/entorno; NUNCA contra sistemas de terceros.

using System.Diagnostics;
using System.Net.Sockets;

var variante = (args.Length > 0 ? args[0] : "A").ToUpperInvariant();
var marcador = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "urponline_emulador_marker.txt");

void Log(string paso, string detalle) =>
    Console.WriteLine($"[emulador][{DateTime.UtcNow:HH:mm:ss}] {paso}: {detalle}");

Log("INICIO", $"Emulador de intrusion INOFENSIVO, variante {variante}. No ataca a terceros ni exfiltra datos.");

// 1) CPU unos segundos (simula el minero).
Log("CPU", "Consumiendo CPU ~5 s (simula el minero). Debe verse como pico en CloudWatch.");
var sw = Stopwatch.StartNew();
double x = 0;
while (sw.Elapsed < TimeSpan.FromSeconds(5)) x += Math.Sqrt(sw.ElapsedTicks % 7919 + 1);
Log("CPU", $"Fin (checksum {x:F0}).");

// 2) Intento de salida a IP reservada de documentacion (no enruta a ningun sitio real).
await IntentarSalida("203.0.113.10", 3333);
await IntentarSalida("203.0.113.10", 51820); // simula intento contra el NAT

// 3) Lee los NOMBRES de variables de credenciales, sin su valor y sin enviarlas.
var sospechosas = new[] { "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN",
                          "GEMINI_API_KEY", "NVIDIA_API_KEY", "DEEPSEEK_API_KEY", "DB_PASSWORD" };
var presentes = sospechosas.Where(v => Environment.GetEnvironmentVariable(v) != null).ToList();
Log("LECTURA_ENTORNO", presentes.Count == 0
    ? "No hay variables de credenciales en el entorno (bien: usan rol IAM / Secrets Manager)."
    : $"Variables de credenciales PRESENTES (solo nombres, no se leen valores ni se envian): {string.Join(", ", presentes)}");

// Variante B (Mythos): anomalia de certificado/TLS. NO rompe cifrado.
if (variante == "B")
{
    Log("MYTHOS", "Simulando anomalia de certificado/TLS (indicador de 'descifrado cuantico'). NO se rompe ningun cifrado.");
    await IntentarSalida("203.0.113.11", 443);
    Log("MYTHOS", "Teoria para la sustentacion: cosechar ahora, descifrar despues; RSA/ECC son el blanco teorico, AES-256 y SHA-256 resisten.");
}

// 4) Marcador en el frontend ("control tomado"), inofensivo.
try
{
    File.WriteAllText(marcador, $"Emulador de intrusion (variante {variante}) paso por aqui a las {DateTime.UtcNow:O}. Inofensivo.\n");
    Log("MARCADOR", $"Escrito marcador inofensivo en {marcador}");
}
catch (Exception e) { Log("MARCADOR", $"No se pudo escribir el marcador: {e.GetType().Name}"); }

Log("FIN", "Emulador terminado. Revisa tus alarmas, Flow Logs, CloudTrail y Reachability Analyzer: tu defensa deberia haberlo detectado.");

async Task IntentarSalida(string host, int puerto)
{
    Log("SALIDA", $"Intentando conectar a {host}:{puerto} (IP reservada de documentacion; no enruta).");
    try
    {
        using var cliente = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await cliente.ConnectAsync(host, puerto, cts.Token);
        Log("SALIDA", $"Conectado a {host}:{puerto} (inesperado en un entorno bien aislado).");
    }
    catch (OperationCanceledException) { Log("SALIDA", $"Sin respuesta de {host}:{puerto} (lo esperado: la salida esta bloqueada o no enruta)."); }
    catch (Exception e) { Log("SALIDA", $"Bloqueado/err a {host}:{puerto}: {e.GetType().Name} (lo esperado si la defensa corta la salida)."); }
}
