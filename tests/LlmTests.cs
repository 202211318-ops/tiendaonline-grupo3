using TiendaOnline.Services.Llm;
using Xunit;

namespace TiendaOnline.Tests;

public class LlmTests
{
    [Fact]
    public void Enmascarar_nunca_muestra_la_clave_completa()
    {
        var clave = "AIzaSyABCDEFGHIJKLMNOP";
        var masc = LlmSettingsStore.Enmascarar(clave);
        Assert.DoesNotContain("BCDEFGHIJKLM", masc);   // no aparece el centro
        Assert.StartsWith("AIza", masc);
        Assert.EndsWith("MNOP", masc);
        Assert.Contains("*", masc);
    }

    [Fact]
    public void Enmascarar_vacio_es_vacio()
    {
        Assert.Equal("", LlmSettingsStore.Enmascarar(null));
        Assert.Equal("", LlmSettingsStore.Enmascarar(""));
    }

    [Theory]
    [InlineData("HTTP 401: invalid api key", "clave")]
    [InlineData("HTTP 404: model not found", "modelo")]
    [InlineData("HTTP 429: quota exceeded", "cuota")]
    public void Pista_traduce_el_error_del_proveedor(string error, string esperadoContiene)
    {
        var pista = LlmConnectionTester.Pista(error).ToLowerInvariant();
        Assert.Contains(esperadoContiene, pista);
    }

    [Fact]
    public void Catalogo_solo_trae_proveedores_abiertos_o_gratuitos()
    {
        // El catalogo no debe incluir proveedores cerrados de pago como Claude u OpenAI.
        var ruta = Path.Combine(FindContentRoot(), "Data", "llm", "modelos.json");
        var json = File.ReadAllText(ruta).ToLowerInvariant();
        Assert.DoesNotContain("\"anthropic\"", json);
        Assert.DoesNotContain("\"openai\"", json);
        Assert.Contains("google", json);
        Assert.Contains("deepseek", json);
    }

    private static string FindContentRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "TiendaOnline.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? Directory.GetCurrentDirectory();
    }
}
