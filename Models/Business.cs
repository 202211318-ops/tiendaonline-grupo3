using System.ComponentModel.DataAnnotations;
namespace TiendaOnline.Models;
// Fase 2 - Orden de compra que PROPONE el agente de stock y que un humano aprueba o rechaza en /admin/compras.
public class PurchaseProposal {
 public int Id {get;set;}
 public int ProductId {get;set;}
 [MaxLength(120)] public string ProductName {get;set;}="";
 public int StockAlProponer {get;set;}
 [MaxLength(1000)] public string Propuesta {get;set;}="";
 [MaxLength(4000)] public string Traza {get;set;}="";       // pensamiento/accion/observacion del loop ReAct
 [MaxLength(20)] public string Estado {get;set;}="PENDIENTE"; // PENDIENTE | APROBADA | RECHAZADA
 public DateTime CreatedUtc {get;set;}=DateTime.UtcNow;
 public DateTime? DecididaUtc {get;set;}
}
public class Promotion {
 public int Id {get;set;}
 [MaxLength(100)] public string Code {get;set;}="";
 [MaxLength(180)] public string Label {get;set;}="";
 public decimal DiscountPercent {get;set;}
 public DateTime StartsUtc {get;set;}
 public DateTime EndsUtc {get;set;}
 public bool Enabled {get;set;}=true;
}
public class BusinessEvent {
 public long Id {get;set;}
 [MaxLength(60)] public string EventType {get;set;}="";
 [MaxLength(80)] public string SessionKey {get;set;}="";
 [MaxLength(60)] public string? Campaign {get;set;}
 [MaxLength(2)] public string? Country {get;set;}
 public int? ProductId {get;set;}
 public int? OrderId {get;set;}
 public decimal? Amount {get;set;}
 public DateTime CreatedUtc {get;set;}=DateTime.UtcNow;
}
public class SupplierImport {
 public int Id {get;set;}
 public DateTime ImportedUtc {get;set;}=DateTime.UtcNow;
 [MaxLength(250)] public string Source {get;set;}="";
 public int Accepted {get;set;}
 public int Rejected {get;set;}
}
