using TiendaOnline.Services.Llm;

namespace TiendaOnline.Services.Agents;

// Agente 2 (Recuperacion de carrito): loop GENERADOR y CRITICO. Uno escribe el correo de oferta,
// otro lo revisa contra las reglas del negocio (descuento maximo, tono, sin datos personales) y el
// generador corrige, hasta que el critico aprueba o se agota el maximo de vueltas.
public sealed record CartOffer(bool Aprobado, string Correo, int Vueltas, IReadOnlyList<string> Observaciones);

public interface ICartAgent
{
    Task<CartOffer> RunAsync(string contextoCliente, int descuentoMaximo = 15, int maxVueltas = 3, CancellationToken ct = default);
}

public sealed class CartAgent(LlmProviderFactory llm) : ICartAgent
{
    public async Task<CartOffer> RunAsync(string contextoCliente, int descuentoMaximo = 15, int maxVueltas = 3, CancellationToken ct = default)
    {
        var provider = llm.CrearActivo();
        var obs = new List<string>();
        string correo = "";
        string critica = "primera version";

        for (int vuelta = 1; vuelta <= maxVueltas; vuelta++)
        {
            // Generador
            var gen = await provider.CompletarAsync(new[]
            {
                LlmMessage.System(
                    $"Eres el generador de correos de recuperacion de carrito de URPOnline. Escribe un correo breve, " +
                    $"amable y en espanol, con UNA oferta cuyo descuento no supere {descuentoMaximo}%. No incluyas datos " +
                    $"personales (correos, telefonos, tarjetas). Devuelve solo el texto del correo."),
                LlmMessage.User($"Cliente: {contextoCliente}\nObservaciones del critico a corregir: {critica}"),
            }, maxTokens: 350, temperatura: 0.3, ct: ct);
            correo = gen.Text;

            // Critico
            var crit = await provider.CompletarAsync(new[]
            {
                LlmMessage.System(
                    $"Eres el critico. Revisa el correo contra las reglas: descuento <= {descuentoMaximo}%, tono amable, " +
                    "sin datos personales. Responde EXACTAMENTE 'APROBADO' si cumple, o 'CORREGIR: <que falla>' si no."),
                LlmMessage.User(correo),
            }, maxTokens: 120, temperatura: 0.0, ct: ct);

            var veredicto = crit.Text.Trim();
            obs.Add($"Vuelta {vuelta}: {veredicto}");
            if (veredicto.StartsWith("APROBADO", StringComparison.OrdinalIgnoreCase))
                return new(true, correo, vuelta, obs);
            critica = veredicto;
        }
        return new(false, correo, maxVueltas, obs);
    }
}
