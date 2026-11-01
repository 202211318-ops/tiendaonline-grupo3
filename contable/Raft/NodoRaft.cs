namespace URPOnline.Contable.Raft;

public enum Rol { Seguidor, Candidato, Lider }

// Un nodo de Raft (Ongaro y Ousterhout, 2014, figura 2). Un solo cerrojo protege todo el estado: más lento que lo
// óptimo, pero fácil de razonar. Los RPC a los demás nodos se mandan FUERA del cerrojo.
public sealed class NodoRaft : INodoRaftRpc, IAsyncDisposable
{
    public string Id { get; }
    readonly IReadOnlyList<string> pares;
    readonly ITransporteRaft red;
    readonly AlmacenRaft disco;
    readonly Action<Entrada> aplicar;               // la máquina de estados: en URPOnline, enviar a SUNAT
    readonly SemaphoreSlim cerrojo = new(1, 1);
    readonly CancellationTokenSource fin = new();
    readonly Random azar;
    readonly int eleccionMinMs, eleccionMaxMs, latidoMs;

    // Estado persistente (en disco antes de responder) y volátil.
    long termino; string? votoPor; List<Entrada> log;
    long commit, aplicado;
    public Rol Rol { get; private set; } = Rol.Seguidor;
    public string? LiderConocido { get; private set; }
    DateTime vence;
    readonly Dictionary<string, long> siguiente = [], igual = [];
    readonly Dictionary<long, TaskCompletionSource<bool>> esperando = [];
    Task? bucle;

    public long Termino => termino;
    public long Commit => commit;
    public IReadOnlyList<Entrada> Log => log;
    int Mayoria => (pares.Count + 1) / 2 + 1;
    long UltimoIndice => log.Count;
    long TerminoEn(long i) => i == 0 ? 0 : log[(int)i - 1].Termino;

    public NodoRaft(string id, IReadOnlyList<string> pares, ITransporteRaft red, AlmacenRaft disco, Action<Entrada> aplicar,
        int eleccionMinMs = 150, int eleccionMaxMs = 300, int latidoMs = 50, int? semilla = null)
    {
        (Id, this.pares, this.red, this.disco, this.aplicar) = (id, pares, red, disco, aplicar);
        (this.eleccionMinMs, this.eleccionMaxMs, this.latidoMs) = (eleccionMinMs, eleccionMaxMs, latidoMs);
        azar = semilla is null ? new Random() : new Random(semilla.Value);
        (termino, votoPor, log) = (disco.Termino, disco.VotoPor, [.. disco.Log]);
        ReiniciarEleccion();
    }

    public void Iniciar() => bucle = Task.Run(() => BucleAsync(fin.Token));

    void ReiniciarEleccion() => vence = DateTime.UtcNow.AddMilliseconds(azar.Next(eleccionMinMs, eleccionMaxMs));
    void Persistir() => disco.Guardar(termino, votoPor, log);

    // Término mayor gana: se actualiza y se pasa a seguidor (llamar con el cerrojo tomado).
    void VerTermino(long otro)
    {
        if (otro <= termino) return;
        (termino, votoPor) = (otro, null);
        Persistir();
        if (Rol == Rol.Lider) CancelarEsperas();
        Rol = Rol.Seguidor;
    }

    async Task BucleAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Rol rol; bool eleccion;
            await cerrojo.WaitAsync(ct);
            try { rol = Rol; eleccion = rol != Rol.Lider && DateTime.UtcNow >= vence; }
            finally { cerrojo.Release(); }
            if (rol == Rol.Lider) await ReplicarAsync(ct);
            else if (eleccion) await EleccionAsync(ct);
            try { await Task.Delay(rol == Rol.Lider ? latidoMs : 10, ct); } catch (OperationCanceledException) { }
        }
    }

    // ── Elección ──
    async Task EleccionAsync(CancellationToken ct)
    {
        PedidoVoto pedido;
        await cerrojo.WaitAsync(ct);
        try
        {
            Rol = Rol.Candidato; termino++; votoPor = Id; LiderConocido = null;
            Persistir(); ReiniciarEleccion();
            pedido = new PedidoVoto(termino, Id, UltimoIndice, TerminoEn(UltimoIndice));
        }
        finally { cerrojo.Release(); }

        var votos = 1;
        var respuestas = pares.Select(p => Intentar(() => red.PedirVotoAsync(p, pedido, ct))).ToList();
        foreach (var r in await Task.WhenAll(respuestas))
        {
            if (r is null) continue;
            await cerrojo.WaitAsync(ct);
            try
            {
                VerTermino(r.Termino);
                if (Rol != Rol.Candidato || termino != pedido.Termino) return;   // ya cambió algo
                if (r.Concedido && ++votos >= Mayoria) { HacerseLider(); return; }
            }
            finally { cerrojo.Release(); }
        }
    }

    void HacerseLider()
    {
        Rol = Rol.Lider; LiderConocido = Id;
        foreach (var p in pares) { siguiente[p] = UltimoIndice + 1; igual[p] = 0; }
    }

    public async Task<RespuestaVoto> PedirVotoAsync(PedidoVoto p, CancellationToken ct)
    {
        await cerrojo.WaitAsync(ct);
        try
        {
            VerTermino(p.Termino);
            var logAlDia = p.UltimoTermino > TerminoEn(UltimoIndice)
                || (p.UltimoTermino == TerminoEn(UltimoIndice) && p.UltimoIndice >= UltimoIndice);
            var concede = p.Termino == termino && (votoPor is null || votoPor == p.Candidato) && logAlDia;
            if (concede) { votoPor = p.Candidato; Persistir(); ReiniciarEleccion(); }
            return new RespuestaVoto(termino, concede);
        }
        finally { cerrojo.Release(); }
    }

    // ── Replicación (el líder) ──
    async Task ReplicarAsync(CancellationToken ct)
    {
        var envios = new List<Task>();
        await cerrojo.WaitAsync(ct);
        try
        {
            if (Rol != Rol.Lider) return;
            foreach (var p in pares)
            {
                var previo = siguiente[p] - 1;
                var pedido = new PedidoAgregar(termino, Id, previo, TerminoEn(previo),
                    log.Skip((int)previo).ToList(), commit);
                envios.Add(EnviarAsync(p, pedido, ct));
            }
        }
        finally { cerrojo.Release(); }
        await Task.WhenAll(envios);
    }

    async Task EnviarAsync(string par, PedidoAgregar pedido, CancellationToken ct)
    {
        var r = await Intentar(() => red.AgregarEntradasAsync(par, pedido, ct));
        if (r is null) return;
        await cerrojo.WaitAsync(ct);
        try
        {
            VerTermino(r.Termino);
            if (Rol != Rol.Lider || termino != pedido.Termino) return;
            if (r.Exito)
            {
                igual[par] = Math.Max(igual[par], r.UltimoIndiceIgual);
                siguiente[par] = igual[par] + 1;
                AvanzarCommit();
            }
            else siguiente[par] = Math.Max(1, Math.Min(siguiente[par] - 1, r.UltimoIndiceIgual + 1));   // retrocede
        }
        finally { cerrojo.Release(); }
    }

    // Una entrada N se confirma si está en la mayoría y es del término actual (figura 2, «Rules for Servers»).
    void AvanzarCommit()
    {
        for (var n = UltimoIndice; n > commit; n--)
        {
            var copias = 1 + pares.Count(p => igual[p] >= n);
            if (copias >= Mayoria && TerminoEn(n) == termino) { commit = n; Aplicar(); return; }
        }
    }

    public async Task<RespuestaAgregar> AgregarEntradasAsync(PedidoAgregar p, CancellationToken ct)
    {
        await cerrojo.WaitAsync(ct);
        try
        {
            VerTermino(p.Termino);
            if (p.Termino < termino) return new RespuestaAgregar(termino, false, 0);
            Rol = Rol.Seguidor; LiderConocido = p.Lider; ReiniciarEleccion();
            if (p.IndicePrevio > UltimoIndice || TerminoEn(p.IndicePrevio) != p.TerminoPrevio)
                return new RespuestaAgregar(termino, false, Math.Min(UltimoIndice, p.IndicePrevio - 1));
            var cambio = false;
            foreach (var e in p.Entradas)
            {
                if (e.Indice <= UltimoIndice && TerminoEn(e.Indice) != e.Termino)
                { log.RemoveRange((int)e.Indice - 1, log.Count - (int)e.Indice + 1); cambio = true; }   // borra el conflicto
                if (e.Indice > UltimoIndice) { log.Add(e); cambio = true; }
            }
            if (cambio) Persistir();
            var ultimoNuevo = p.IndicePrevio + p.Entradas.Count;
            if (p.CommitDelLider > commit) { commit = Math.Min(p.CommitDelLider, ultimoNuevo); Aplicar(); }
            return new RespuestaAgregar(termino, true, ultimoNuevo);
        }
        finally { cerrojo.Release(); }
    }

    void Aplicar()
    {
        while (aplicado < commit)
        {
            aplicado++;
            aplicar(log[(int)aplicado - 1]);
            if (esperando.Remove(aplicado, out var t)) t.TrySetResult(true);
        }
    }

    // ── Lo que usa el sistema contable ──
    // Solo el líder acepta comandos. Devuelve cuando la entrada está confirmada por la mayoría y aplicada.
    public async Task<Entrada> ProponerAsync(string comando, TimeSpan espera, CancellationToken ct)
    {
        Entrada entrada; TaskCompletionSource<bool> listo;
        await cerrojo.WaitAsync(ct);
        try
        {
            if (Rol != Rol.Lider) throw new NoSoyLiderException(LiderConocido);
            entrada = new Entrada(termino, UltimoIndice + 1, comando);
            log.Add(entrada); Persistir();
            listo = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            esperando[entrada.Indice] = listo;
            if (pares.Count == 0) AvanzarCommit();
        }
        finally { cerrojo.Release(); }
        if (!await listo.Task.WaitAsync(espera, ct)) throw new NoSoyLiderException(LiderConocido);
        return entrada;
    }

    void CancelarEsperas()
    {
        foreach (var t in esperando.Values) t.TrySetResult(false);   // perdió el liderazgo: no se sabe si se confirmará
        esperando.Clear();
    }

    static async Task<T?> Intentar<T>(Func<Task<T>> llamada) where T : class
    {
        try { return await llamada(); }
        catch (Exception e) when (e is not OperationCanceledException) { return null; }   // nodo caído o red cortada
    }

    public async ValueTask DisposeAsync()
    {
        fin.Cancel();
        if (bucle is not null) try { await bucle; } catch (OperationCanceledException) { }
    }
}
