using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TiendaOnline.Tests;

// Caída simulada de la Lambda (fase 2): con SIMULAR_CAIDA=true no procesa ningún mensaje y los reporta
// TODOS como fallidos, para que SQS los reintente y, al agotar maxReceiveCount, los mande a la DLQ.
public class CaidaSimuladaTests
{
    private static URPOnline.Agentes.Handler HandlerCon(string? simularCaida)
    {
        var valores = new Dictionary<string, string?>();
        if (simularCaida != null) valores["SIMULAR_CAIDA"] = simularCaida;
        var servicios = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(valores).Build())
            .BuildServiceProvider();
        return new URPOnline.Agentes.Handler(servicios);
    }

    private static SQSEvent Lote(params string[] ids) =>
        new() { Records = ids.Select(id => new SQSEvent.SQSMessage { MessageId = id, Body = "{}" }).ToList() };

    [Fact]
    public async Task Con_SIMULAR_CAIDA_reporta_todos_los_mensajes_como_fallidos()
    {
        var r = await HandlerCon("true").Procesar(Lote("m1", "m2", "m3"), new Contexto());
        Assert.Equal(new[] { "m1", "m2", "m3" }, r.BatchItemFailures.Select(f => f.ItemIdentifier).ToArray());
    }

    [Fact]
    public async Task Con_SIMULAR_CAIDA_no_toca_la_base_de_datos()
    {
        // Sin IdempotencyStore registrado: si la Lambda intentara procesar, fallaría por otro motivo.
        var contexto = new Contexto();
        await HandlerCon("true").Procesar(Lote("m1"), contexto);
        Assert.Contains("SIMULAR_CAIDA", ((Registro)contexto.Logger).Texto);
    }

    private sealed class Contexto : ILambdaContext
    {
        public string AwsRequestId => "prueba";
        public IClientContext ClientContext => null!;
        public string FunctionName => "urponline-agentes";
        public string FunctionVersion => "$LATEST";
        public ICognitoIdentity Identity => null!;
        public string InvokedFunctionArn => "arn:aws:lambda:us-east-2:000000000000:function:urponline-agentes";
        public ILambdaLogger Logger { get; } = new Registro();
        public string LogGroupName => "/aws/lambda/urponline-agentes";
        public string LogStreamName => "prueba";
        public int MemoryLimitInMB => 512;
        public TimeSpan RemainingTime => TimeSpan.FromMinutes(2);
    }

    private sealed class Registro : ILambdaLogger
    {
        private readonly System.Text.StringBuilder _sb = new();
        public string Texto => _sb.ToString();
        public void Log(string message) => _sb.AppendLine(message);
        public void LogLine(string message) => _sb.AppendLine(message);
    }
}
