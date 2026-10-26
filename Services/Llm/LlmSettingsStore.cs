using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;

namespace TiendaOnline.Services.Llm;

// Lo que se guarda: el proveedor y el modelo elegidos, la base_url de Jev (si se usa) y la API key
// de cada proveedor. TODO vive en UN secreto de AWS Secrets Manager (Llm:SecretId, p. ej.
// "urponline/llm"); las keys nunca salen en claro a la pantalla, solo enmascaradas.
// En desarrollo (sin Llm:SecretId) se cae a un archivo local, para poder probar sin AWS.
public sealed class LlmSettings
{
    public string Provider { get; set; } = "google";
    public string Model { get; set; } = "gemma-2-9b-it";
    public string JevBaseUrl { get; set; } = "";
    public Dictionary<string, string> Keys { get; set; } = new();  // clave_proveedor -> api_key
}

public sealed class LlmSettingsStore
{
    private readonly IAmazonSecretsManager? _sm;
    private readonly string? _secretId;
    private readonly string _rutaLocal;
    private readonly object _lock = new();

    public LlmSettingsStore(IConfiguration config, IWebHostEnvironment env, IServiceProvider sp)
    {
        _secretId = config["Llm:SecretId"];
        // Solo se resuelve el cliente de Secrets Manager si hay un secreto configurado.
        _sm = string.IsNullOrWhiteSpace(_secretId) ? null : sp.GetService(typeof(IAmazonSecretsManager)) as IAmazonSecretsManager;
        _rutaLocal = Path.Combine(env.ContentRootPath, "Data", "llm", "settings.local.json");
    }

    public LlmSettings Leer()
    {
        lock (_lock)
        {
            if (_sm is null || string.IsNullOrWhiteSpace(_secretId))
                return LeerLocal();
            try
            {
                var r = _sm.GetSecretValueAsync(new GetSecretValueRequest { SecretId = _secretId })
                          .GetAwaiter().GetResult();
                return Deserializar(r.SecretString);
            }
            catch (ResourceNotFoundException)
            {
                return new LlmSettings();
            }
        }
    }

    public void Guardar(LlmSettings s)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(s);
            if (_sm is null || string.IsNullOrWhiteSpace(_secretId))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_rutaLocal)!);
                File.WriteAllText(_rutaLocal, json);
                return;
            }
            try
            {
                _sm.PutSecretValueAsync(new PutSecretValueRequest { SecretId = _secretId, SecretString = json })
                   .GetAwaiter().GetResult();
            }
            catch (ResourceNotFoundException)
            {
                _sm.CreateSecretAsync(new CreateSecretRequest { Name = _secretId, SecretString = json })
                   .GetAwaiter().GetResult();
            }
        }
    }

    // Enmascara una key para mostrarla: "AIza****wXyz" (nunca la key completa).
    public static string Enmascarar(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        if (key.Length <= 8) return new string('*', key.Length);
        return key[..4] + new string('*', Math.Min(8, key.Length - 8)) + key[^4..];
    }

    private LlmSettings LeerLocal()
    {
        if (!File.Exists(_rutaLocal)) return new LlmSettings();
        return Deserializar(File.ReadAllText(_rutaLocal));
    }

    private static LlmSettings Deserializar(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new LlmSettings();
        return JsonSerializer.Deserialize<LlmSettings>(json) ?? new LlmSettings();
    }
}
