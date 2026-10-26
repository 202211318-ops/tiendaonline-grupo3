using System.Text.Json;
using Amazon.Lambda.Core;
using Amazon.SQS.Model;

namespace URPOnline.Delivery;

// Simulador de repartidores (§9.2): N repartidores falsos caminan al azar alrededor de Miraflores y envían su GPS
// cada 2 segundos a la MISMA cola FIFO que los celulares reales, en lotes de 10. La semilla hace la corrida
// repetible; la causa inyecta una falla para que el grupo la diagnostique (D5):
//   duplicados  → cada posición se envía dos veces (con distinta deduplicación)
//   desorden    → dentro de cada tanda, las posiciones salen en orden aleatorio
//   silencio    → a mitad de la corrida, un 30 % de los repartidores deja de transmitir
public sealed class Simulador(Infra infra)
{
    private const double LatCentro = -12.1211, LonCentro = -77.0297;   // Miraflores
    private const int PasoSeg = 2;

    public async Task<string> CorrerAsync(EntradaSimulador e, ILambdaContext ctx)
    {
        var n = Math.Clamp(e.Repartidores, 1, 1000);
        var minutos = Math.Clamp(e.Minutos, 1, 14);                  // la Lambda dura como máximo 15 minutos
        var azar = new Random(e.Semilla);
        var pos = Enumerable.Range(0, n).Select(_ => (Lat: LatCentro + (azar.NextDouble() - 0.5) * 0.04,
                                                      Lon: LonCentro + (azar.NextDouble() - 0.5) * 0.04)).ToArray();
        var callados = e.Causa == "silencio" ? Enumerable.Range(0, n).Where(_ => azar.NextDouble() < 0.3).ToHashSet() : [];
        var vueltas = minutos * 60 / PasoSeg;
        long enviados = 0;
        for (var v = 0; v < vueltas; v++)
        {
            var tanda = new List<SendMessageBatchRequestEntry>();
            for (var i = 0; i < n; i++)
            {
                if (v > vueltas / 2 && callados.Contains(i)) continue;
                pos[i] = (pos[i].Lat + (azar.NextDouble() - 0.5) * 0.0008, pos[i].Lon + (azar.NextDouble() - 0.5) * 0.0008);
                var cuerpo = JsonSerializer.Serialize(new Posicion(0, pos[i].Lat, pos[i].Lon, v, DateTime.UtcNow), Infra.Json);
                tanda.Add(Entrada(i, cuerpo));
                if (e.Causa == "duplicados") tanda.Add(Entrada(i, cuerpo));
            }
            if (e.Causa == "desorden") tanda = tanda.OrderBy(_ => azar.Next()).ToList();
            foreach (var lote in tanda.Chunk(10))
            {
                await infra.Sqs.SendMessageBatchAsync(new SendMessageBatchRequest { QueueUrl = infra.Config("Colas__Posiciones"), Entries = lote.ToList() });
                enviados += lote.Length;
            }
            await Task.Delay(TimeSpan.FromSeconds(PasoSeg));
        }
        var resumen = $"Simulador: semilla {e.Semilla}, {n} repartidores, {minutos} min, causa {e.Causa ?? "ninguna"}, {enviados} posiciones";
        ctx.Logger.LogInformation(resumen);
        return resumen;
    }

    private static SendMessageBatchRequestEntry Entrada(int repartidor, string cuerpo) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        MessageBody = cuerpo,
        MessageGroupId = $"sim-{repartidor}",
        MessageDeduplicationId = Guid.NewGuid().ToString("N"),
        MessageAttributes = new Dictionary<string, MessageAttributeValue>
            { ["repartidor"] = new() { DataType = "String", StringValue = $"sim-{repartidor}" } },
    };
}
