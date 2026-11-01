using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TiendaOnline.Services.Comprobantes;
using URPOnline.Contable.Contratos;
using Xunit;

namespace TiendaOnline.Tests;

// Fase 3, desafío G10: el hub real de la tienda (ComprobantesHub + AvisoDeSunat) en un servidor Kestrel real,
// con sesión y clientes SignalR reales por WebSockets. Sin base de datos: la sesión se arma con una ruta de prueba
// que hace lo mismo que el checkout (guardar el pedido en «mis-pedidos»).
public class SignalRTests : IAsyncLifetime
{
    WebApplication app = null!;
    string url = "";

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<AvisoDeSunat>();
        app = builder.Build();
        app.UseSession();
        app.MapGet("/pedido/{id:int}", (int id, HttpContext c) => { c.Session.SetString(ComprobantesHub.ClaveSesion, id.ToString()); return "ok"; });
        app.MapHub<ComprobantesHub>(ComprobantesHub.Ruta);
        await app.StartAsync();
        url = app.Urls.First();
    }

    public async Task DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }

    async Task<(HubConnection Conexion, List<string> Avisos)> Navegador(int? pedidoDeSuSesion)
    {
        var cookies = new CookieContainer();
        if (pedidoDeSuSesion is int id)
        {
            using var http = new HttpClient(new HttpClientHandler { CookieContainer = cookies });
            Assert.Equal("ok", await http.GetStringAsync($"{url}/pedido/{id}"));
        }
        var avisos = new List<string>();
        var conexion = new HubConnectionBuilder()
            .WithUrl(url + ComprobantesHub.Ruta, o => o.Cookies = cookies)
            .Build();
        conexion.On<Dictionary<string, object>>(AvisoDeSunat.Evento, r => { lock (avisos) avisos.Add(r["comprobante"].ToString()!); });
        await conexion.StartAsync();
        return (conexion, avisos);
    }

    [Fact]
    public async Task El_resultado_de_SUNAT_llega_solo_al_navegador_dueno_del_pedido()
    {
        var (duenio, avisosDuenio) = await Navegador(pedidoDeSuSesion: 881);
        var (otro, avisosOtro) = await Navegador(pedidoDeSuSesion: null);

        Assert.True(await duenio.InvokeAsync<bool>("SeguirPedido", 881));
        Assert.False(await otro.InvokeAsync<bool>("SeguirPedido", 881));       // no es de su sesión

        var aviso = app.Services.GetRequiredService<AvisoDeSunat>();
        await aviso.NotificarResultadoAsync(new ResultadoSunat("pedido-881", "F001-881", true, "0", "aceptada", []), default);

        var hasta = DateTime.UtcNow.AddSeconds(5);
        while (avisosDuenio.Count == 0 && DateTime.UtcNow < hasta) await Task.Delay(20);
        await Task.Delay(300);
        Assert.Equal(["F001-881"], avisosDuenio);
        Assert.Empty(avisosOtro);
        await duenio.DisposeAsync(); await otro.DisposeAsync();
    }

    [Fact]
    public void Un_pedido_solo_es_de_la_sesion_que_lo_creo()
    {
        Assert.True(ComprobantesHub.EsDeLaSesion("880,881", 881));
        Assert.False(ComprobantesHub.EsDeLaSesion("8810", 881));
        Assert.False(ComprobantesHub.EsDeLaSesion(null, 881));
    }
}
