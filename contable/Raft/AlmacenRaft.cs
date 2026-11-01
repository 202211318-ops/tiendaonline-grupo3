using System.Text.Json;

namespace URPOnline.Contable.Raft;

// Estado persistente de Raft: término actual, a quién votó en ese término y el log. Raft exige guardarlo en disco
// ANTES de responder un voto o un AgregarEntradas: si el nodo se reinicia, no puede votar dos veces en el mismo
// término ni olvidar una entrada que ya confirmó. Se escribe a un archivo temporal y se reemplaza el anterior,
// para que un corte a mitad de la escritura no deje un archivo a medias.
public sealed class AlmacenRaft
{
    sealed record Estado(long Termino, string? VotoPor, List<Entrada> Log);

    readonly string ruta;
    public long Termino { get; private set; }
    public string? VotoPor { get; private set; }
    public List<Entrada> Log { get; private set; } = [];   // Log[0] es la entrada de índice 1

    public AlmacenRaft(string ruta)
    {
        this.ruta = ruta;
        if (!File.Exists(ruta)) return;
        var e = JsonSerializer.Deserialize<Estado>(File.ReadAllText(ruta))!;
        (Termino, VotoPor, Log) = (e.Termino, e.VotoPor, e.Log);
    }

    public void Guardar(long termino, string? votoPor, List<Entrada> log)
    {
        (Termino, VotoPor, Log) = (termino, votoPor, log);
        var temporal = ruta + ".tmp";
        using (var f = new FileStream(temporal, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(f, new Estado(termino, votoPor, log));
            f.Flush(flushToDisk: true);
        }
        File.Move(temporal, ruta, overwrite: true);
    }
}
