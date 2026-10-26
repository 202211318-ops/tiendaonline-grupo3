using Microsoft.EntityFrameworkCore;
namespace TiendaOnline.Data;

// Fase 3 (B3) - Balanceo en la base de datos: las LECTURAS del catalogo van a la replica de RDS
// (ConnectionStrings:PostgresLectura); los pedidos y pagos siguen escribiendo en el primario con ShopDbContext.
// Sin replica configurada, PostgresLectura cae al primario (ver DatabaseSecret.BuildReadConnectionString).
// La replica es de solo lectura: guardar cambios con este contexto es un error de programacion y se frena aqui.
public class ShopReadDbContext(DbContextOptions<ShopReadDbContext> options) : ShopDbContext(options) {
 public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
  throw new InvalidOperationException("ShopReadDbContext lee de la replica: las escrituras van con ShopDbContext.");
 public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
  throw new InvalidOperationException("ShopReadDbContext lee de la replica: las escrituras van con ShopDbContext.");
}
