using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiendaOnline.Services.Llm;

// Llama a cualquier modelo por la API compatible con OpenAI: POST {base_url}/chat/completions con
// Authorization: Bearer {key}. Gemma/Gemini (Google), Nemotron (NVIDIA), DeepSeek y Jev se llaman
// todos asi; lo unico que cambia es base_url, la key y el nombre del modelo.
public sealed class OpenAiCompatibleLlmProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;

    public string Modelo { get; }

    public OpenAiCompatibleLlmProvider(HttpClient http, string baseUrl, string apiKey, string modelo)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        Modelo = modelo;
    }

    public async Task<LlmResult> CompletarAsync(
        IReadOnlyList<LlmMessage> mensajes, int maxTokens = 1024, double temperatura = 0.0,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_baseUrl))
            throw new InvalidOperationException("El proveedor no tiene base_url configurada.");

        var cuerpo = new ChatRequest
        {
            Model = Modelo,
            Temperature = temperatura,
            MaxTokens = maxTokens,
            Messages = mensajes.Select(m => new ChatMessage { Role = m.Role, Content = m.Content }).ToList(),
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(cuerpo, options: JsonOpts),
        };
        if (!string.IsNullOrWhiteSpace(_apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var resp = await _http.SendAsync(req, ct);
        var texto = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {Recorta(texto)}", null, resp.StatusCode);

        var r = JsonSerializer.Deserialize<ChatResponse>(texto, JsonOpts);
        var contenido = r?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
        return new LlmResult(contenido.Trim(), r?.Usage?.PromptTokens ?? 0, r?.Usage?.CompletionTokens ?? 0);
    }

    private static string Recorta(string s) => s.Length <= 400 ? s : s[..400];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("messages")] public List<ChatMessage> Messages { get; set; } = new();
        [JsonPropertyName("temperature")] public double Temperature { get; set; }
        [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
        [JsonPropertyName("usage")] public Usage? Usage { get; set; }
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }

    private sealed class Usage
    {
        [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
        [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    }
}
