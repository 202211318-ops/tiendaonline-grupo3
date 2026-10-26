using Microsoft.AspNetCore.Mvc;
using TiendaOnline.Data;
using TiendaOnline.Services;
namespace TiendaOnline.Controllers;
public class CartController(CartService cart,ShopDbContext db,BusinessEvents events,StoreEventPublisher bus,BusinessMetrics metrics):Controller {
 public async Task<IActionResult> Index()=>View(await cart.ViewAsync(HttpContext.Session));
 [HttpPost,ValidateAntiForgeryToken]
 public async Task<IActionResult> Add(int id,int quantity=1) {
  if(await db.Products.FindAsync(id) is { Stock: > 0 } p) {
   cart.Add(HttpContext.Session,id,Math.Min(quantity,p.Stock));
   await events.RecordAsync("cart_add",productId:id);
   // Fase 2: contador propio del carrito con el AWS SDK (CartItemsAdded).
   await metrics.CartItemAddedAsync(HttpContext.RequestAborted);
   await bus.PublishAsync(StoreEventTypes.ProductoAgregadoAlCarrito,new{productoId=id,cantidad=Math.Min(quantity,p.Stock),
    sesion=StoreEventPublisher.HashSession(HttpContext.Session.Id),ocurrioUtc=DateTime.UtcNow},HttpContext.RequestAborted);
   TempData["Message"]="Producto agregado al carrito";
  }
  return RedirectToAction("Index","Home");
 }
 [HttpPost,ValidateAntiForgeryToken]
 public IActionResult Remove(int id) { cart.Remove(HttpContext.Session,id); return RedirectToAction("Index"); }
}
