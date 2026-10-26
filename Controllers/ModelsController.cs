using Microsoft.AspNetCore.Mvc;
using TiendaOnline.Services.Llm;

namespace TiendaOnline.Controllers;

// Pantalla /admin/modelos: elegir el modelo (abierto/gratuito), guardar la API key en Secrets Manager
// (enmascarada en pantalla) y probar la conexion. Mismo control de acceso que /admin/promotions.
[Route("admin/modelos")]
public class ModelsController(LlmCatalog catalogo, LlmSettingsStore store, LlmConnectionTester tester, IConfiguration config) : Controller
{
    private bool Authorized(string? key) =>
        !string.IsNullOrWhiteSpace(config["Admin:AccessKey"]) && key == config["Admin:AccessKey"];

    [HttpGet]
    public IActionResult Index([FromQuery] string? key)
    {
        if (!Authorized(key)) return Unauthorized();
        ViewBag.Key = key;
        ViewBag.Settings = store.Leer();
        return View(catalogo.Proveedores);
    }

    [HttpPost("usar"), ValidateAntiForgeryToken]
    public IActionResult Usar([FromForm] string key, [FromForm] string provider, [FromForm] string modelo)
    {
        if (!Authorized(key)) return Unauthorized();
        if (provider != "jev" && !catalogo.ModeloValido(provider, modelo)) return BadRequest("Modelo no soportado.");
        var s = store.Leer();
        s.Provider = provider; s.Model = modelo;
        store.Guardar(s);
        return RedirectToAction("Index", new { key });
    }

    [HttpPost("key"), ValidateAntiForgeryToken]
    public IActionResult GuardarKey([FromForm] string key, [FromForm] string provider, [FromForm] string apiKey)
    {
        if (!Authorized(key)) return Unauthorized();
        var s = store.Leer();
        if (string.IsNullOrWhiteSpace(apiKey)) s.Keys.Remove(provider);
        else s.Keys[provider] = apiKey.Trim();
        store.Guardar(s);
        return RedirectToAction("Index", new { key });
    }

    [HttpPost("jev-url"), ValidateAntiForgeryToken]
    public IActionResult GuardarJevUrl([FromForm] string key, [FromForm] string baseUrl)
    {
        if (!Authorized(key)) return Unauthorized();
        var s = store.Leer();
        s.JevBaseUrl = (baseUrl ?? "").Trim();
        store.Guardar(s);
        return RedirectToAction("Index", new { key });
    }

    [HttpPost("probar"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Probar([FromForm] string key, CancellationToken ct)
    {
        if (!Authorized(key)) return Unauthorized();
        ViewBag.Test = await tester.ProbarAsync(ct);
        ViewBag.Key = key;
        ViewBag.Settings = store.Leer();
        return View("Index", catalogo.Proveedores);
    }
}
