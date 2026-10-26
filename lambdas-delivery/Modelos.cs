namespace URPOnline.Delivery;

// Los mismos esquemas que contratos/delivery-v1.openapi.yaml (nombres en camelCase por System.Text.Json).
public sealed record ItemPedido(int PlatoId, int Cantidad);
public sealed record NuevoPedido(int RestauranteId, List<ItemPedido> Items, double Lat, double Lon);
public sealed record Pedido(int Id, string Estado, int Version, int RestauranteId, int? RepartidorId, decimal Total,
    double Lat, double Lon, DateTime ActualizadoEn);
public sealed record Oferta(int PedidoId, int Intento, string Restaurante, decimal Total);
public sealed record Cambio(int PedidoId, string Estado, DateTime? HoraDispositivo);
public sealed record LoteDeCambios(List<Cambio> Cambios);
public sealed record Posicion(int PedidoId, double Lat, double Lon, long Secuencia, DateTime HoraDispositivo);

// Mensajes internos de las colas.
public sealed record MensajePedido(int PedidoId, int Intento);
public sealed record MensajeCambio(int PedidoId, string Estado, string RepartidorSub, DateTime? HoraDispositivo);

// Entrada del simulador (invocacion manual): semilla, cuantos repartidores, cuantos minutos y la causa oculta.
public sealed record EntradaSimulador(int Semilla, int Repartidores, int Minutos, string? Causa);

public static class Reglas
{
    public static readonly string[] EstadosDelCelular = ["recogido", "entregado"];
    public const double RadioKm = 3.0;         // la base usa un radio fijo; ampliarlo es el desafio A4
    public const int MaxIntentos = 3;          // reofrecimientos de la base
    public const int RetrasoReofrecerSeg = 120;

    // Valida un pedido nuevo contra el contrato. Devuelve el motivo del rechazo o null si es valido.
    public static string? Validar(NuevoPedido? p)
    {
        if (p is null) return "El cuerpo no es un pedido valido.";
        if (p.Items is null || p.Items.Count == 0) return "El pedido no tiene platos.";
        if (p.Items.Any(i => i.Cantidad < 1 || i.Cantidad > 20)) return "Cada cantidad debe estar entre 1 y 20.";
        if (p.Lat is < -90 or > 90 || p.Lon is < -180 or > 180) return "El punto de entrega no es valido.";
        return null;
    }

    public static string? Validar(LoteDeCambios? l)
    {
        if (l?.Cambios is null || l.Cambios.Count is 0 or > 50) return "El lote debe traer entre 1 y 50 cambios.";
        if (l.Cambios.Any(c => !EstadosDelCelular.Contains(c.Estado))) return "El celular solo informa recogido o entregado.";
        return null;
    }
}
