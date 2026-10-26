using Microsoft.EntityFrameworkCore;
using Npgsql;
using TiendaOnline.Data;
using TiendaOnline.Services.Agents;
using TiendaOnline.Services.Llm;

namespace TiendaOnline.Services.Comida;

public sealed record RespuestaSoporte(string Respuesta, string? Seccion);

// Soporte del delivery, BASE del curso (§9.9.2): RAG por coseno sobre la politica del delivery, con los mismos
// guardrails y el mismo cliente del modelo que el agente de soporte de la tienda. El enrutador (SQL / RAG /
// persona), la verificacion con Jaccard, el humano en el ciclo y la traza del agente son el desafio D4.
public sealed class SoporteDelivery(ShopDbContext db, EmbeddingService embeddings, LlmProviderFactory llm,
    InputGuard inputGuard, OutputGuard outputGuard)
{
    private const string Sistema =
        "Eres el soporte del delivery de URPOnline. Responde SOLO con la politica que se te entrega como contexto. " +
        "Cita la seccion que usaste entre parentesis, por ejemplo (seccion: reembolsos). Si el contexto no contiene " +
        "la respuesta, responde exactamente: 'No tengo esa informacion en la politica.' No inventes.";

    public async Task<RespuestaSoporte> ResponderAsync(string texto, CancellationToken ct = default)
    {
        var entrada = inputGuard.Check(texto);
        if (entrada.Blocked) return new("Lo siento, no puedo ayudarte con eso.", null);
        if (!embeddings.IsAvailable) return new("El soporte no está disponible: falta el modelo de embeddings.", null);

        await using var cmd = new NpgsqlCommand("""
            SELECT seccion, contenido FROM politica_delivery
            WHERE embedding IS NOT NULL
            ORDER BY embedding <=> @q::vector
            LIMIT 2
            """, await OpenAsync(ct));
        cmd.Parameters.AddWithValue("q", ComidaStore.ALiteral(embeddings.Embed(texto)));
        var secciones = new List<(string Seccion, string Contenido)>();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) secciones.Add((r.GetString(0), r.GetString(1)));
        if (secciones.Count == 0) return new("No tengo esa informacion en la politica.", null);

        var contexto = string.Join("\n", secciones.Select(s => $"[{s.Seccion}] {s.Contenido}"));
        LlmResult resultado;
        try
        {
            resultado = await llm.CrearActivo().CompletarAsync(
                [LlmMessage.System(Sistema), LlmMessage.User($"Contexto (politica del delivery):\n{contexto}\n\nMensaje del cliente: {texto}")],
                maxTokens: 400, temperatura: 0.0, ct: ct);
        }
        catch (HttpRequestException e)
        {
            // Sin API key (o con una invalida) el proveedor rechaza la llamada: se dice que falta, no se responde 500.
            var motivo = e.Message.Split('\n')[0];
            return new($"El soporte no está disponible: el modelo no respondió ({motivo}). Revisa la API key en /admin/modelos.", secciones[0].Seccion);
        }

        var salida = outputGuard.Check(resultado.Text, secciones.Select(s => s.Seccion).ToList());
        return salida.Blocked ? new("No puedo entregar esa respuesta.", null) : new(resultado.Text, secciones[0].Seccion);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }
}
