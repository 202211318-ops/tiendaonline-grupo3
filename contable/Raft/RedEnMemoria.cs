namespace URPOnline.Contable.Raft;

// Una red simulada entre nodos del mismo proceso, para las pruebas y para el probador del desafío G16:
// se puede apagar un nodo y partir la red (los nodos de un lado no llegan a los del otro).
public sealed class RedEnMemoria : ITransporteRaft
{
    readonly Dictionary<string, NodoRaft> nodos = [];
    readonly HashSet<string> apagados = [];
    readonly HashSet<(string, string)> cortes = [];
    readonly object llave = new();

    public void Registrar(NodoRaft nodo) { lock (llave) nodos[nodo.Id] = nodo; }
    public void Apagar(string id) { lock (llave) apagados.Add(id); }
    public void Encender(string id) { lock (llave) apagados.Remove(id); }

    // Parte la red en dos lados: nadie del lado A llega a nadie del lado B.
    public void Partir(IEnumerable<string> ladoA, IEnumerable<string> ladoB)
    {
        lock (llave)
            foreach (var a in ladoA) foreach (var b in ladoB) { cortes.Add((a, b)); cortes.Add((b, a)); }
    }
    public void Sanar() { lock (llave) cortes.Clear(); }

    // El transporte de UN nodo: sabe quién envía, para aplicar los cortes.
    public ITransporteRaft Para(string origen) => new Transporte(this, origen);

    NodoRaft Destino(string origen, string destino)
    {
        lock (llave)
        {
            if (apagados.Contains(origen) || apagados.Contains(destino) || cortes.Contains((origen, destino)))
                throw new IOException($"Sin ruta de {origen} a {destino}");
            return nodos[destino];
        }
    }

    Task<RespuestaVoto> ITransporteRaft.PedirVotoAsync(string d, PedidoVoto p, CancellationToken ct) => throw new InvalidOperationException("Usar Para(origen).");
    Task<RespuestaAgregar> ITransporteRaft.AgregarEntradasAsync(string d, PedidoAgregar p, CancellationToken ct) => throw new InvalidOperationException("Usar Para(origen).");

    sealed class Transporte(RedEnMemoria red, string origen) : ITransporteRaft
    {
        public Task<RespuestaVoto> PedirVotoAsync(string destino, PedidoVoto p, CancellationToken ct) =>
            red.Destino(origen, destino).PedirVotoAsync(p, ct);
        public Task<RespuestaAgregar> AgregarEntradasAsync(string destino, PedidoAgregar p, CancellationToken ct) =>
            red.Destino(origen, destino).AgregarEntradasAsync(p, ct);
    }
}
