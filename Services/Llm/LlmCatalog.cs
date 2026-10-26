using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiendaOnline.Services.Llm;

// El catalogo de /admin/modelos, leido de Data/llm/modelos.json. Solo lectura.
public sealed class LlmModel
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = "";
    [JsonPropertyName("gratis")] public bool Gratis { get; set; }
}

public sealed class LlmProviderInfo
{
    [JsonPropertyName("clave")] public string Clave { get; set; } = "";
    [JsonPropertyName("nombre")] public string Nombre { get; set; } = "";
    [JsonPropertyName("base_url")] public string BaseUrl { get; set; } = "";
    [JsonPropertyName("donde_key")] public string DondeKey { get; set; } = "";
    [JsonPropertyName("modelos")] public List<LlmModel> Modelos { get; set; } = new();
}

public sealed class LlmCatalog
{
    private readonly List<LlmProviderInfo> _proveedores;

    public LlmCatalog(IWebHostEnvironment env)
    {
        // Data/llm/modelos.json se copia al publicar (ver TiendaOnline.csproj).
        var ruta = Path.Combine(env.ContentRootPath, "Data", "llm", "modelos.json");
        using var fs = File.OpenRead(ruta);
        using var doc = JsonDocument.Parse(fs);
        var raiz = doc.RootElement.GetProperty("proveedores").GetRawText();
        _proveedores = JsonSerializer.Deserialize<List<LlmProviderInfo>>(raiz) ?? new();
    }

    public IReadOnlyList<LlmProviderInfo> Proveedores => _proveedores;

    public LlmProviderInfo? Proveedor(string clave) =>
        _proveedores.FirstOrDefault(p => p.Clave == clave);

    // El base_url de un proveedor; para Jev lo define el grupo (puede venir vacio).
    public string? BaseUrl(string clave) => Proveedor(clave)?.BaseUrl;

    public bool ModeloValido(string clave, string modeloId) =>
        Proveedor(clave)?.Modelos.Any(m => m.Id == modeloId) ?? false;
}
