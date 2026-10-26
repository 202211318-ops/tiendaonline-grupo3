using TiendaOnline.Services.Llm;

namespace TiendaOnline.Services.Agents;

// Agente 3 (Soporte): conversacional con RESPUESTA FUNDAMENTADA en el RAG estructural de los SLA y
// con los guardrails de 3 capas. Responde citando la seccion usada; si el contexto no responde,
// dice "no tengo esa informacion" en lugar de inventar (ahi aparecen las alucinaciones).
public sealed record SupportAnswer(bool Answered, string Text, IReadOnlyList<string> Sources, string? BlockedBy);

public sealed class SupportAgent(
    SlaSectionStore sla,
    LlmProviderFactory llm,
    InputGuard inputGuard,
    OutputGuard outputGuard)
{
    private const string System =
        "Eres el asistente de soporte de la tienda URPOnline. Responde SOLO sobre productos, pedidos y el " +
        "contrato de nivel de servicio (SLA) del producto, usando UNICAMENTE el contexto que se te entrega. " +
        "Cita la seccion que usaste entre parentesis, por ejemplo (seccion: garantia). Si el contexto no " +
        "contiene la respuesta, responde exactamente: 'No tengo esa informacion en el contrato.' No inventes.";

    public async Task<SupportAnswer> AnswerAsync(int productId, string pregunta, int topK = 3, CancellationToken ct = default)
    {
        // Capa 2: filtro de entrada.
        var inp = inputGuard.Check(pregunta);
        if (inp.Blocked)
            return new(false, "Lo siento, no puedo ayudarte con eso.", Array.Empty<string>(), inp.Rule);

        // RAG estructural: recupera las secciones del SLA de ESE producto.
        var hits = await sla.RetrieveAsync(productId, pregunta, topK, ct: ct);
        var secciones = hits.Select(h => h.SectionType).Distinct().ToList();

        if (hits.Count == 0)
            return new(true, "No tengo esa informacion en el contrato.", secciones, null);

        var contexto = string.Join("\n", hits.Select(h => $"[{h.SectionType}] {h.Content}"));
        var mensajes = new[]
        {
            LlmMessage.System(System),
            LlmMessage.User($"Contexto (secciones del SLA del producto):\n{contexto}\n\nPregunta del cliente: {pregunta}"),
        };

        var r = await llm.CrearActivo().CompletarAsync(mensajes, maxTokens: 400, temperatura: 0.0, ct: ct);

        // Capa 3: filtro de salida (cita obligatoria + sin datos de otros clientes).
        var outp = outputGuard.Check(r.Text, secciones);
        if (outp.Blocked)
            return new(false, "No puedo entregar esa respuesta.", secciones, outp.Rule);

        return new(true, r.Text, secciones, null);
    }
}
