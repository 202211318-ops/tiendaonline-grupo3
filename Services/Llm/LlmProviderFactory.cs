using System.Diagnostics;

namespace TiendaOnline.Services.Llm;

// Construye el ILlmProvider a partir de lo configurado en /admin/modelos. Resuelve juntos el modelo,
// la base_url (del catalogo, o la propia del grupo para Jev) y la API key (de Secrets Manager).
public sealed class LlmProviderFactory
{
    private readonly IHttpClientFactory _http;
    private readonly LlmCatalog _catalogo;
    private readonly LlmSettingsStore _store;

    public LlmProviderFactory(IHttpClientFactory http, LlmCatalog catalogo, LlmSettingsStore store)
    {
        _http = http;
        _catalogo = catalogo;
        _store = store;
    }

    // El proveedor activo (el elegido en la pantalla).
    public ILlmProvider CrearActivo()
    {
        var s = _store.Leer();
        return Crear(s.Provider, s.Model, s);
    }

    // Un proveedor para un modelo concreto (lo usan los agentes: cada uno puede correr con otro modelo).
    public ILlmProvider Crear(string provider, string modelo, LlmSettings? settings = null)
    {
        var s = settings ?? _store.Leer();
        var baseUrl = provider == "jev" ? s.JevBaseUrl : (_catalogo.BaseUrl(provider) ?? "");
        s.Keys.TryGetValue(provider, out var key);
        return new OpenAiCompatibleLlmProvider(_http.CreateClient("llm"), baseUrl, key ?? "", modelo);
    }
}

// Resultado de "Probar conexion": nunca lanza; devuelve lo que paso en palabras del propio proveedor.
public record LlmTestResult(bool Ok, string Provider, string Modelo, long Ms, string Respuesta, string Error, string Pista);

public sealed class LlmConnectionTester
{
    private readonly LlmProviderFactory _factory;
    private readonly LlmSettingsStore _store;
    private const string Pregunta = "Responde unicamente con la palabra OK.";

    public LlmConnectionTester(LlmProviderFactory factory, LlmSettingsStore store)
    {
        _factory = factory;
        _store = store;
    }

    public async Task<LlmTestResult> ProbarAsync(CancellationToken ct = default)
    {
        var s = _store.Leer();
        s.Keys.TryGetValue(s.Provider, out var key);
        if (string.IsNullOrWhiteSpace(key))
            return new(false, s.Provider, s.Model, 0, "", $"No hay clave de {s.Provider} guardada.",
                       "Ponla en API Keys, arriba, y vuelve a pulsar Probar.");

        var sw = Stopwatch.StartNew();
        try
        {
            var r = await _factory.CrearActivo().CompletarAsync(
                new[] { LlmMessage.User(Pregunta) }, maxTokens: 50, temperatura: 0.0, ct: ct);
            sw.Stop();
            if (string.IsNullOrWhiteSpace(r.Text))
                return new(true, s.Provider, s.Model, sw.ElapsedMilliseconds, "", "",
                           "Contesto, pero sin texto. La conexion y la clave funcionan.");
            return new(true, s.Provider, s.Model, sw.ElapsedMilliseconds, Recorta(r.Text), "", "");
        }
        catch (Exception e)
        {
            sw.Stop();
            return new(false, s.Provider, s.Model, sw.ElapsedMilliseconds, "", Recorta(e.Message), Pista(e.Message));
        }
    }

    private static string Recorta(string s) => s.Length <= 300 ? s : s[..300];

    public static string Pista(string msg)
    {
        var m = (msg ?? "").ToLowerInvariant();
        if (m.Contains("401") || m.Contains("403") || m.Contains("unauthor") || m.Contains("api key"))
            return "La clave no la acepta el proveedor: revisala o genera una nueva.";
        if (m.Contains("404") || m.Contains("not found"))
            return "El proveedor responde, pero ese modelo no esta disponible para tu cuenta: elige otro.";
        if (m.Contains("429") || m.Contains("quota") || m.Contains("rate"))
            return "La clave es valida pero se agoto la cuota gratuita. Espera o usa otro modelo abierto.";
        if (m.Contains("timeout") || m.Contains("timed out"))
            return "El proveedor no contesto a tiempo. Puede estar saturado.";
        if (m.Contains("base_url"))
            return "Falta la base_url del proveedor (para Jev, pon la de tu propio endpoint).";
        return "Error del proveedor: el texto de arriba es lo que devolvio.";
    }
}
