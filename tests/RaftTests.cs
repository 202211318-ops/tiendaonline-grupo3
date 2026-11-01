using System.Collections.Concurrent;
using URPOnline.Contable.Raft;
using Xunit;

namespace TiendaOnline.Tests;

// Fase 3, desafío G15: el Raft del sistema contable con tres nodos en una red simulada (RedEnMemoria), con
// caídas y particiones de verdad entre nodos del mismo proceso. Cada nodo guarda su estado en un archivo real.
public class RaftTests : IAsyncLifetime
{
    readonly string carpeta = Path.Combine(Path.GetTempPath(), "raft-" + Guid.NewGuid().ToString("N"));
    readonly RedEnMemoria red = new();
    readonly Dictionary<string, NodoRaft> nodos = [];
    readonly ConcurrentDictionary<string, List<Entrada>> aplicadas = new();
    static readonly string[] Ids = ["n1", "n2", "n3"];

    public Task InitializeAsync() { Directory.CreateDirectory(carpeta); foreach (var id in Ids) Crear(id); return Task.CompletedTask; }

    void Crear(string id)
    {
        var lista = aplicadas.GetOrAdd(id, _ => []);
        var nodo = new NodoRaft(id, Ids.Where(x => x != id).ToList(), red.Para(id),
            new AlmacenRaft(Path.Combine(carpeta, id + ".json")), e => { lock (lista) lista.Add(e); },
            eleccionMinMs: 100, eleccionMaxMs: 200, latidoMs: 20);
        nodos[id] = nodo; red.Registrar(nodo); nodo.Iniciar();
    }

    public async Task DisposeAsync()
    {
        foreach (var n in nodos.Values) await n.DisposeAsync();
        try { Directory.Delete(carpeta, true); } catch (IOException) { }
    }

    async Task<NodoRaft> LiderEntre(IEnumerable<string> ids, TimeSpan? espera = null)
    {
        var hasta = DateTime.UtcNow + (espera ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < hasta)
        {
            var lideres = ids.Select(i => nodos[i]).Where(n => n.Rol == Rol.Lider).ToList();
            if (lideres.Count == 1) return lideres[0];
            await Task.Delay(20);
        }
        throw new TimeoutException("No se eligió un único líder");
    }

    async Task Esperar(Func<bool> condicion, string que)
    {
        var hasta = DateTime.UtcNow.AddSeconds(5);
        while (!condicion()) { if (DateTime.UtcNow > hasta) throw new TimeoutException(que); await Task.Delay(20); }
    }

    [Fact]
    public async Task Se_elige_un_solo_lider_y_los_demas_lo_conocen()
    {
        var lider = await LiderEntre(Ids);
        await Esperar(() => Ids.All(i => nodos[i].LiderConocido == lider.Id), "todos conocen al líder");
        Assert.Single(nodos.Values, n => n.Rol == Rol.Lider);
        Assert.All(nodos.Values, n => Assert.Equal(lider.Termino, n.Termino));
    }

    [Fact]
    public async Task Una_entrada_confirmada_se_aplica_en_los_tres_nodos_en_el_mismo_orden()
    {
        var lider = await LiderEntre(Ids);
        foreach (var f in new[] { "F001-1", "F001-2", "F001-3" }) await lider.ProponerAsync(f, TimeSpan.FromSeconds(5), default);
        await Esperar(() => Ids.All(i => aplicadas[i].Count == 3), "los tres aplican las tres");
        foreach (var i in Ids) Assert.Equal(["F001-1", "F001-2", "F001-3"], aplicadas[i].Select(e => e.Comando));
    }

    [Fact]
    public async Task Un_seguidor_no_acepta_ventas_y_dice_quien_es_el_lider()
    {
        var lider = await LiderEntre(Ids);
        var seguidor = nodos.Values.First(n => n != lider);
        await Esperar(() => seguidor.LiderConocido == lider.Id, "el seguidor conoce al líder");
        var e = await Assert.ThrowsAsync<NoSoyLiderException>(() => seguidor.ProponerAsync("F001-9", TimeSpan.FromSeconds(1), default));
        Assert.Equal(lider.Id, e.LiderConocido);
    }

    [Fact]
    public async Task Si_se_cae_el_lider_se_elige_otro_y_no_se_pierde_lo_confirmado()
    {
        var viejo = await LiderEntre(Ids);
        await viejo.ProponerAsync("F001-1", TimeSpan.FromSeconds(5), default);
        red.Apagar(viejo.Id);
        var resto = Ids.Where(i => i != viejo.Id).ToList();
        var nuevo = await LiderEntre(resto);
        Assert.True(nuevo.Termino > viejo.Termino);
        await nuevo.ProponerAsync("F001-2", TimeSpan.FromSeconds(5), default);
        await Esperar(() => resto.All(i => aplicadas[i].Count == 2), "la mayoría aplica las dos");
        foreach (var i in resto) Assert.Equal(["F001-1", "F001-2"], aplicadas[i].Select(e => e.Comando));
    }

    [Fact]
    public async Task El_lider_aislado_no_confirma_y_al_volver_la_red_se_pone_al_dia()
    {
        var aislado = await LiderEntre(Ids);
        var resto = Ids.Where(i => i != aislado.Id).ToList();
        red.Partir([aislado.Id], resto);

        // En minoría, lo que recibe no se confirma nunca: ni se aplica ni se enviaría a SUNAT.
        var sinMayoria = aislado.ProponerAsync("F001-FANTASMA", TimeSpan.FromMilliseconds(800), default);
        var nuevo = await LiderEntre(resto);
        await nuevo.ProponerAsync("F001-2", TimeSpan.FromSeconds(5), default);
        await Assert.ThrowsAnyAsync<Exception>(() => sinMayoria);
        Assert.DoesNotContain(aplicadas[aislado.Id], e => e.Comando == "F001-FANTASMA");

        red.Sanar();
        await Esperar(() => aislado.Rol == Rol.Seguidor && aislado.Log.Select(e => e.Comando).SequenceEqual(nuevo.Log.Select(e => e.Comando)),
            "el aislado copia el log del nuevo líder");
        await Esperar(() => aplicadas[aislado.Id].Any(e => e.Comando == "F001-2"), "el aislado aplica lo confirmado");
        Assert.All(Ids, i => Assert.DoesNotContain(aplicadas[i], e => e.Comando == "F001-FANTASMA"));
    }

    [Fact]
    public void El_termino_el_voto_y_el_log_sobreviven_a_un_reinicio()
    {
        var ruta = Path.Combine(carpeta, "reinicio.json");
        new AlmacenRaft(ruta).Guardar(7, "n2", [new Entrada(7, 1, "F001-1")]);
        var leido = new AlmacenRaft(ruta);
        Assert.Equal(7, leido.Termino);
        Assert.Equal("n2", leido.VotoPor);
        Assert.Equal("F001-1", Assert.Single(leido.Log).Comando);
    }
}
