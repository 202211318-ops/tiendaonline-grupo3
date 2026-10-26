using Microsoft.AspNetCore.Mvc;
using TiendaOnline.Services.Comida;

namespace TiendaOnline.Controllers;

// Pestaña 🍔 Comida de la tienda (fase 2, §9.1) y API que API Gateway reenvía a la tienda (contrato delivery-v1:
// /v1/restaurantes, /v1/restaurantes/{id}/platos, /v1/platos/buscar y /v1/soporte llegan aquí por HTTP_PROXY).
// La base es de solo lectura en la web: pedir desde la pestaña Comida por la cola es el desafío A1.
public class ComidaController(ComidaStore comida, SoporteDelivery soporte) : Controller
{
    [HttpGet("/comida")]
    public async Task<IActionResult> Index(int? restaurante, string? q, CancellationToken ct)
    {
        var restaurantes = await comida.RestaurantesAsync(ct);
        var elegido = restaurante ?? restaurantes.FirstOrDefault()?.Id;
        ViewBag.Restaurantes = restaurantes;
        ViewBag.Elegido = elegido;
        ViewBag.Consulta = q;
        ViewBag.Encontrados = string.IsNullOrWhiteSpace(q) ? null : await comida.BuscarAsync(q, 10, ct);
        return View(elegido is int id ? await comida.PlatosAsync(id, ct) : new List<Plato>());
    }

    [HttpGet("/api/comida/restaurantes")]
    public async Task<IActionResult> Restaurantes(CancellationToken ct) => Json(await comida.RestaurantesAsync(ct));

    [HttpGet("/api/comida/restaurantes/{id:int}/platos")]
    public async Task<IActionResult> Platos(int id, CancellationToken ct) => Json(await comida.PlatosAsync(id, ct));

    [HttpGet("/api/comida/buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length > 200)
            return Problem(title: "La búsqueda debe tener entre 1 y 200 caracteres", statusCode: 400);
        var encontrados = await comida.BuscarAsync(q, 10, ct);
        return Json(encontrados.Select(e => new
        {
            e.Plato.Id, e.Plato.RestauranteId, e.Plato.Nombre, e.Plato.Descripcion, e.Plato.Precio,
            e.Plato.Disponible, e.Plato.Ingredientes, e.Plato.Alergenos, coseno = Math.Round(e.Coseno, 4),
        }));
    }

    public sealed record MensajeSoporte(int PedidoId, string Texto);

    [HttpPost("/api/comida/soporte")]
    [IgnoreAntiforgeryToken] // lo llama API Gateway (JSON), no un formulario del navegador
    public async Task<IActionResult> Soporte([FromBody] MensajeSoporte mensaje, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mensaje.Texto) || mensaje.Texto.Length > 1000)
            return Problem(title: "El mensaje debe tener entre 1 y 1000 caracteres", statusCode: 400);
        var r = await soporte.ResponderAsync(mensaje.Texto, ct);
        return Json(new { respuesta = r.Respuesta, seccion = r.Seccion });
    }
}
