using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TiendaOnline.Data;
namespace TiendaOnline.Controllers;
// Pantalla /admin/compras: las ordenes de compra que PROPONE el agente de stock (loop ReAct) esperan aqui
// la decision de un humano (humano en el ciclo). Mismo control de acceso que /admin/promotions.
[Route("admin/compras")]
public class PurchasesController(ShopDbContext db,IConfiguration config):Controller {
 private bool Authorized(string? key)=> !string.IsNullOrWhiteSpace(config["Admin:AccessKey"]) && key==config["Admin:AccessKey"];
 [HttpGet]
 public async Task<IActionResult> Index([FromQuery]string? key,CancellationToken ct) {
  if(!Authorized(key)) return Unauthorized();
  ViewBag.Key=key;
  return View(await db.PurchaseProposals.AsNoTracking().OrderBy(p=>p.Estado!="PENDIENTE").ThenByDescending(p=>p.CreatedUtc).Take(100).ToListAsync(ct));
 }
 [HttpPost("decidir"),ValidateAntiForgeryToken]
 public async Task<IActionResult> Decidir([FromForm]string key,[FromForm]int id,[FromForm]bool aprobar,CancellationToken ct) {
  if(!Authorized(key)) return Unauthorized();
  var p=await db.PurchaseProposals.FindAsync([id],ct);
  if(p==null) return NotFound();
  if(p.Estado!="PENDIENTE") return BadRequest("La propuesta ya fue decidida");
  p.Estado=aprobar?"APROBADA":"RECHAZADA";p.DecididaUtc=DateTime.UtcNow;
  await db.SaveChangesAsync(ct);
  return RedirectToAction("Index",new{key});
 }
}
