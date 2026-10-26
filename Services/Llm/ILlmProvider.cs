namespace TiendaOnline.Services.Llm;

// Un mensaje de la conversacion con el modelo (role: system | user | assistant).
public record LlmMessage(string Role, string Content)
{
    public static LlmMessage System(string c) => new("system", c);
    public static LlmMessage User(string c) => new("user", c);
    public static LlmMessage Assistant(string c) => new("assistant", c);
}

// Resultado de una llamada: el texto y cuanto costo en tokens (0 si el proveedor no lo informa).
public record LlmResult(string Text, int InputTokens, int OutputTokens);

// La interfaz comun a todos los proveedores. Cada agente recibe un ILlmProvider (inyeccion de
// dependencias): manana otro modelo = otra clase que implemente esto, sin tocar a quien llama.
// Todos los modelos de la fase 2 son abiertos/gratuitos y se llaman por API compatible con OpenAI,
// asi que hay UNA sola implementacion (OpenAiCompatibleLlmProvider) parametrizada por base_url y key.
public interface ILlmProvider
{
    // El modelo que esta corriendo (para trazas y para la pantalla).
    string Modelo { get; }

    Task<LlmResult> CompletarAsync(
        IReadOnlyList<LlmMessage> mensajes,
        int maxTokens = 1024,
        double temperatura = 0.0,
        CancellationToken ct = default);
}
