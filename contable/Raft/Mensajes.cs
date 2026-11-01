using MessagePack;
using StreamJsonRpc;

namespace URPOnline.Contable.Raft;

// Los dos RPC de Raft (Ongaro y Ousterhout, «In Search of an Understandable Consensus Algorithm», 2014, figura 2):
// RequestVote (aquí PedirVoto) y AppendEntries (aquí AgregarEntradas, que con la lista vacía es el latido).
// Viajan entre los nodos del sistema contable por JSON-RPC + MessagePack, el mismo canal que usa la tienda.

[MessagePackObject]
public sealed record Entrada([property: Key(0)] long Termino, [property: Key(1)] long Indice, [property: Key(2)] string Comando);

[MessagePackObject]
public sealed record PedidoVoto([property: Key(0)] long Termino, [property: Key(1)] string Candidato,
    [property: Key(2)] long UltimoIndice, [property: Key(3)] long UltimoTermino);

[MessagePackObject]
public sealed record RespuestaVoto([property: Key(0)] long Termino, [property: Key(1)] bool Concedido);

[MessagePackObject]
public sealed record PedidoAgregar([property: Key(0)] long Termino, [property: Key(1)] string Lider,
    [property: Key(2)] long IndicePrevio, [property: Key(3)] long TerminoPrevio,
    [property: Key(4)] IReadOnlyList<Entrada> Entradas, [property: Key(5)] long CommitDelLider);

[MessagePackObject]
public sealed record RespuestaAgregar([property: Key(0)] long Termino, [property: Key(1)] bool Exito,
    [property: Key(2)] long UltimoIndiceIgual);

// Lo que cada nodo expone a los demás.
[JsonRpcContract]
public partial interface INodoRaftRpc
{
    Task<RespuestaVoto> PedirVotoAsync(PedidoVoto pedido, CancellationToken ct);
    Task<RespuestaAgregar> AgregarEntradasAsync(PedidoAgregar pedido, CancellationToken ct);
}

// Cómo un nodo llega a los demás: por TCP (TransporteJsonRpc) o por una red simulada (RedEnMemoria).
public interface ITransporteRaft
{
    Task<RespuestaVoto> PedirVotoAsync(string destino, PedidoVoto pedido, CancellationToken ct);
    Task<RespuestaAgregar> AgregarEntradasAsync(string destino, PedidoAgregar pedido, CancellationToken ct);
}

public sealed class NoSoyLiderException(string? liderConocido)
    : Exception(liderConocido is null ? "Este nodo no es el líder y no conoce al líder." : $"Este nodo no es el líder; el líder es {liderConocido}.")
{
    public string? LiderConocido { get; } = liderConocido;
}
