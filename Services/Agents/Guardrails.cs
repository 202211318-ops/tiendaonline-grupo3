using System.Text.RegularExpressions;

namespace TiendaOnline.Services.Agents;

// Guardrails de 3 capas del agente de soporte (spec fase 2, categoria 1):
//  1) system prompt  -> delimita el tema y obliga a usar el contexto (lo arma SupportAgent).
//  2) filtro de ENTRADA -> rechaza fuera de tema, inapropiadas y prompt injection, antes de llamar al modelo.
//  3) filtro de SALIDA  -> exige cita de una seccion y bloquea datos de otros clientes.
// Reglas por regex, en clases separadas para poder agregar una nueva con su prueba unitaria (paso 3).

public enum GuardLayer { Input, Output }

public record GuardResult(bool Blocked, string? Rule, string? Reason)
{
    public static GuardResult Ok() => new(false, null, null);
    public static GuardResult Block(string rule, string reason) => new(true, rule, reason);
}

public sealed partial class InputGuard
{
    // Intentos de prompt injection ("ignora las instrucciones", "actua como", "revela el system prompt"...).
    [GeneratedRegex(@"(ignora|olvida).{0,20}(instruc|reglas|anterior|system)|act[uú]a como|haz de cuenta|revela.{0,20}(prompt|instruc)|jailbreak|DAN\b", RegexOptions.IgnoreCase)]
    private static partial Regex Injection();

    // Inapropiado / ofensivo (lista minima; el grupo la amplia en el paso 3).
    [GeneratedRegex(@"\b(insulto|idiota|est[uú]pido|maldito|sexual|porno)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Inapropiada();

    // Fuera de tema: precios de la competencia, politica, etc. (ejemplo del paso 3).
    [GeneratedRegex(@"precio.{0,15}(competencia|amazon|mercadolibre|ripley)|\b(pol[ií]tica|elecciones|religi[oó]n)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FueraDeTema();

    // Pedir datos de OTRO cliente ("el pedido de Juan", "los datos del cliente 42").
    [GeneratedRegex(@"(pedido|datos|direcci[oó]n|correo|tel[eé]fono).{0,25}(de|del)\s+(otro|cliente\s+\d+|[A-ZÁÉÍÓÚ][a-záéíóú]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DatosDeOtro();

    public GuardResult Check(string pregunta)
    {
        if (string.IsNullOrWhiteSpace(pregunta)) return GuardResult.Block("vacia", "La pregunta esta vacia.");
        if (Injection().IsMatch(pregunta)) return GuardResult.Block("prompt_injection", "Intento de manipular las instrucciones del agente.");
        if (Inapropiada().IsMatch(pregunta)) return GuardResult.Block("inapropiada", "Lenguaje inapropiado.");
        if (DatosDeOtro().IsMatch(pregunta)) return GuardResult.Block("datos_de_otro", "Pide datos de otro cliente.");
        if (FueraDeTema().IsMatch(pregunta)) return GuardResult.Block("fuera_de_tema", "La pregunta no es sobre productos, pedidos o el SLA de la tienda.");
        return GuardResult.Ok();
    }
}

public sealed partial class OutputGuard
{
    // Identificadores ajenos que no deben salir en la respuesta (correos, tarjetas, telefonos largos).
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}|\b\d{13,19}\b|\b9\d{8}\b")]
    private static partial Regex Pii();

    // La respuesta debe citar una seccion recuperada: "(seccion: garantia)" o "[garantia]".
    public GuardResult Check(string respuesta, IReadOnlyCollection<string> seccionesRecuperadas)
    {
        if (string.IsNullOrWhiteSpace(respuesta)) return GuardResult.Block("vacia", "Respuesta vacia.");
        if (Pii().IsMatch(respuesta)) return GuardResult.Block("pii_salida", "La respuesta contiene datos personales y se bloquea.");
        // Si hubo contexto, exige que la respuesta mencione alguna seccion recuperada (cita).
        if (seccionesRecuperadas.Count > 0)
        {
            var cita = seccionesRecuperadas.Any(s => respuesta.Contains(s, StringComparison.OrdinalIgnoreCase));
            if (!cita) return GuardResult.Block("sin_cita", "La respuesta no cita ninguna seccion del SLA recuperada.");
        }
        return GuardResult.Ok();
    }
}
