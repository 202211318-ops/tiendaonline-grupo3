namespace TiendaOnline.Services.Agents;

// Un agente es un loop: piensa, actua, observa y repite. Lo peligroso es no saber cuando parar.
// StopPolicy reune los controles de parada (spec fase 2, categoria 1): maximo de iteraciones,
// presupuesto de tokens y DETECCION DE BUCLE (si repite la misma accion con el mismo resultado, para).
// Es logica pura y se prueba sola, sin red.
public sealed class StopPolicy(int maxIteraciones = 8, int presupuestoTokens = 20_000)
{
    private int _iteraciones;
    private int _tokensGastados;
    private readonly HashSet<string> _vistos = new();

    public int Iteraciones => _iteraciones;
    public int TokensGastados => _tokensGastados;
    public string? Motivo { get; private set; }

    // Devuelve true si se debe CONTINUAR; false si hay que parar (y deja el motivo).
    public bool PuedeContinuar()
    {
        if (_iteraciones >= maxIteraciones) { Motivo = $"maximo de iteraciones ({maxIteraciones})"; return false; }
        if (_tokensGastados >= presupuestoTokens) { Motivo = $"presupuesto de tokens ({presupuestoTokens})"; return false; }
        return true;
    }

    // Registra una vuelta: cuenta la iteracion, suma tokens y detecta bucle.
    // Si la (accion + resultado) ya se habia visto, marca bucle y pide parar.
    public bool RegistrarVuelta(string accion, string resultado, int tokens)
    {
        _iteraciones++;
        _tokensGastados += tokens;
        var firma = accion.Trim() + "=>" + resultado.Trim();
        if (!_vistos.Add(firma))
        {
            Motivo = "deteccion de bucle: misma accion con el mismo resultado";
            return false;   // es bucle: parar
        }
        return true;        // vuelta nueva: seguir (si PuedeContinuar lo permite)
    }
}
