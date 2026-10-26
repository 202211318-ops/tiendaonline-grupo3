using URPOnline.Delivery;
using Xunit;

namespace TiendaOnline.Tests;

// Reglas de entrada de las Lambdas del delivery (lógica pura, sin AWS ni base de datos).
public class DeliveryReglasTests
{
    private static NuevoPedido Pedido(params (int Plato, int Cantidad)[] items) =>
        new(1, items.Select(i => new ItemPedido(i.Plato, i.Cantidad)).ToList(), -12.12, -77.03);

    [Fact]
    public void Un_pedido_valido_pasa() => Assert.Null(Reglas.Validar(Pedido((1, 2), (3, 1))));

    [Fact]
    public void Un_pedido_sin_platos_se_rechaza() => Assert.NotNull(Reglas.Validar(Pedido()));

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void La_cantidad_fuera_de_1_a_20_se_rechaza(int cantidad) => Assert.NotNull(Reglas.Validar(Pedido((1, cantidad))));

    [Fact]
    public void Un_punto_de_entrega_imposible_se_rechaza() =>
        Assert.NotNull(Reglas.Validar(new NuevoPedido(1, [new ItemPedido(1, 1)], 95, -77)));

    [Fact]
    public void El_celular_solo_informa_recogido_o_entregado()
    {
        Assert.Null(Reglas.Validar(new LoteDeCambios([new Cambio(1, "recogido", null), new Cambio(1, "entregado", null)])));
        Assert.NotNull(Reglas.Validar(new LoteDeCambios([new Cambio(1, "aceptado", null)])));
        Assert.NotNull(Reglas.Validar(new LoteDeCambios([])));
    }
}
