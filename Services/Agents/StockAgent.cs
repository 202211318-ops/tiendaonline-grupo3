using System.Text;
using System.Text.RegularExpressions;
using TiendaOnline.Services.Llm;

namespace TiendaOnline.Services.Agents;

// Agente 1 (Stock): loop ReAct -> pensar -> actuar (herramienta) -> observar -> repetir.
// Con maximo de iteraciones, presupuesto y deteccion de bucle (StopPolicy). Propone una orden de
// compra que un humano aprueba en /admin. La traza (pensamiento/accion/observacion) queda para explicar.
public sealed record ReActStep(string Pensamiento, string Accion, string Observacion);
public sealed record StockResult(string Propuesta, IReadOnlyList<ReActStep> Traza, string MotivoParada);

public interface IStockAgent
{
    Task<StockResult> RunAsync(int productId, int maxIteraciones = 8, CancellationToken ct = default);
}

public sealed class StockAgent(LlmProviderFactory llm, IAgentTools tools) : IStockAgent
{
    private const string SystemPrompt =
        "Eres el agente de stock de URPOnline. Trabajas en ciclos ReAct. En cada turno responde EXACTAMENTE en " +
        "una de estas dos formas:\n" +
        "PENSAMIENTO: <tu razonamiento>\nACCION: ventasUltimaSemana(<id>)\n" +
        "   o bien\n" +
        "PENSAMIENTO: <tu razonamiento>\nFINAL: <propuesta de orden de compra en una linea>\n" +
        "Solo tienes la herramienta ventasUltimaSemana(id). No inventes datos: usala.";

    public async Task<StockResult> RunAsync(int productId, int maxIteraciones = 8, CancellationToken ct = default)
    {
        var policy = new StopPolicy(maxIteraciones);
        var traza = new List<ReActStep>();
        var historial = new StringBuilder($"Producto a evaluar: {productId}.\n");
        var provider = llm.CrearActivo();

        while (policy.PuedeContinuar())
        {
            var r = await provider.CompletarAsync(new[]
            {
                LlmMessage.System(SystemPrompt),
                LlmMessage.User(historial.ToString() + "\nTu turno:"),
            }, maxTokens: 300, temperatura: 0.0, ct: ct);

            var (pensamiento, accion, idTool, esFinal, final) = Parse(r.Text);

            if (esFinal)
            {
                traza.Add(new(pensamiento, "FINAL", final));
                return new(final, traza, "propuesta final");
            }

            var observacion = idTool is int id
                ? await tools.VentasUltimaSemanaAsync(id, ct)
                : "Accion no reconocida; usa ventasUltimaSemana(id) o FINAL.";
            traza.Add(new(pensamiento, accion, observacion));
            historial.Append($"\nPENSAMIENTO: {pensamiento}\nACCION: {accion}\nOBSERVACION: {observacion}\n");

            // Cuenta la vuelta y detecta bucle (misma accion + misma observacion).
            if (!policy.RegistrarVuelta(accion, observacion, r.OutputTokens))
                return new("Sin propuesta: el agente entro en bucle.", traza, policy.Motivo!);
        }
        return new("Sin propuesta: se alcanzo el limite.", traza, policy.Motivo ?? "limite");
    }

    // Parseo tolerante de PENSAMIENTO/ACCION/FINAL.
    private static (string pensamiento, string accion, int? idTool, bool esFinal, string final) Parse(string texto)
    {
        string pensamiento = "", accion = "", final = "";
        int? idTool = null; bool esFinal = false;
        foreach (var linea in texto.Split('\n'))
        {
            var l = linea.Trim();
            if (l.StartsWith("PENSAMIENTO:", StringComparison.OrdinalIgnoreCase)) pensamiento = l[12..].Trim();
            else if (l.StartsWith("FINAL:", StringComparison.OrdinalIgnoreCase)) { esFinal = true; final = l[6..].Trim(); }
            else if (l.StartsWith("ACCION:", StringComparison.OrdinalIgnoreCase))
            {
                accion = l[7..].Trim();
                var m = Regex.Match(accion, @"ventasUltimaSemana\((\d+)\)", RegexOptions.IgnoreCase);
                if (m.Success) idTool = int.Parse(m.Groups[1].Value);
            }
        }
        return (pensamiento, accion, idTool, esFinal, final);
    }
}
