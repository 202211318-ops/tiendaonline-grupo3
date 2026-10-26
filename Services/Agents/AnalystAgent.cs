using TiendaOnline.Services.Llm;

namespace TiendaOnline.Services.Agents;

// Agente 4 (Analista): loop de HIPOTESIS y VERIFICACION. Propone una causa de las cancelaciones,
// la comprueba contra el resumen SQL (agregado de todos los clientes), y si no alcanza plantea otra.
// Razona sobre el RESUMEN, no sobre cada evento. Emite UNA alerta clara (en produccion, por SNS).
public sealed record AnalystResult(string Causa, string Evidencia, int Hipotesis, string Alerta, string MotivoParada);

public interface IAnalystAgent
{
    Task<AnalystResult> RunAsync(int ventanaMinutos = 15, int maxHipotesis = 4, CancellationToken ct = default);
}

public sealed class AnalystAgent(LlmProviderFactory llm, IAgentTools tools) : IAnalystAgent
{
    public async Task<AnalystResult> RunAsync(int ventanaMinutos = 15, int maxHipotesis = 4, CancellationToken ct = default)
    {
        var resumen = await tools.ResumenCancelacionesAsync(ventanaMinutos, ct);
        var provider = llm.CrearActivo();
        var policy = new StopPolicy(maxHipotesis);
        string ultimaCausa = "sin determinar", ultimaEvidencia = resumen;

        while (policy.PuedeContinuar())
        {
            var r = await provider.CompletarAsync(new[]
            {
                LlmMessage.System(
                    "Eres el agente analista de URPOnline. A partir del resumen SQL de cancelaciones, propon la CAUSA mas " +
                    "probable y verifica si el resumen la respalda. Responde EXACTAMENTE:\n" +
                    "CAUSA: <causa>\nEVIDENCIA: <que dato del resumen la respalda>\nCONFIANZA: alta|baja"),
                LlmMessage.User($"Resumen SQL (ventana {ventanaMinutos} min): {resumen}"),
            }, maxTokens: 250, temperatura: 0.0, ct: ct);

            var (causa, evidencia, alta) = Parse(r.Text);
            ultimaCausa = causa; ultimaEvidencia = evidencia;

            if (!policy.RegistrarVuelta(causa, evidencia, r.OutputTokens))
                break; // bucle: misma causa y evidencia

            if (alta)
            {
                var alerta = $"ALERTA: la causa probable de las cancelaciones es «{causa}». Evidencia: {evidencia}.";
                return new(causa, evidencia, policy.Iteraciones, alerta, "confianza alta");
            }
        }
        return new(ultimaCausa, ultimaEvidencia, policy.Iteraciones,
                   $"ALERTA (confianza baja): posible causa «{ultimaCausa}».", policy.Motivo ?? "limite");
    }

    private static (string causa, string evidencia, bool alta) Parse(string texto)
    {
        string causa = "sin determinar", evidencia = ""; bool alta = false;
        foreach (var linea in texto.Split('\n'))
        {
            var l = linea.Trim();
            if (l.StartsWith("CAUSA:", StringComparison.OrdinalIgnoreCase)) causa = l[6..].Trim();
            else if (l.StartsWith("EVIDENCIA:", StringComparison.OrdinalIgnoreCase)) evidencia = l[10..].Trim();
            else if (l.StartsWith("CONFIANZA:", StringComparison.OrdinalIgnoreCase)) alta = l[10..].Trim().StartsWith("alta", StringComparison.OrdinalIgnoreCase);
        }
        return (causa, evidencia, alta);
    }
}
