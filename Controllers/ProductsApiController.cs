using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TiendaOnline.Data;
namespace TiendaOnline.Controllers;

// Fase 3 - API JSON del catalogo: la usa la prueba automatica del blue/green (k8s/bluegreen-analisis.yaml)
public record ProductDto(int Id,string Name,string Description,decimal Price,int Stock,string Category);

[ApiController]
[Route("api/v1/products")]
public class ProductsApiController(ShopDbContext db) : ControllerBase {
 [HttpGet]
 public async Task<ActionResult<IReadOnlyList<ProductDto>>> List(string? category,string? q,CancellationToken ct) {
  var products=db.Products.AsNoTracking().AsQueryable();
  if(!string.IsNullOrWhiteSpace(category)) products=products.Where(x=>x.Category==category);
  if(!string.IsNullOrWhiteSpace(q)) { var term=q.Trim().ToLower(); products=products.Where(x=>x.Name.ToLower().Contains(term) || x.Description.ToLower().Contains(term)); }
  return await products.OrderBy(x=>x.Name).Take(100)
   .Select(x=>new ProductDto(x.Id,x.Name,x.Description,x.Price,x.Stock,x.Category)).ToListAsync(ct);
 }

 [HttpGet("{id:int}")]
 public async Task<ActionResult<ProductDto>> Get(int id,CancellationToken ct) {
  var p=await db.Products.AsNoTracking().Where(x=>x.Id==id)
   .Select(x=>new ProductDto(x.Id,x.Name,x.Description,x.Price,x.Stock,x.Category)).FirstOrDefaultAsync(ct);
  return p is null ? NotFound() : p;
 }
}
