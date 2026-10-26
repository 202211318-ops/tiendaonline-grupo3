using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace URPOnline.Delivery;

// El WebSocket no tiene autorizador de Cognito como la API REST: el token llega en ?token= al conectar y se
// valida aqui contra las llaves publicas (JWKS) del User Pool. Las llaves se descargan una vez por caja.
public sealed class TokenCognito(string region, string userPoolId)
{
    private static readonly HttpClient Http = new();
    private JsonWebKeySet? _llaves;
    private string Emisor => $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";

    public async Task<string?> ValidarAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        _llaves ??= new JsonWebKeySet(await Http.GetStringAsync($"{Emisor}/.well-known/jwks.json"));
        var r = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Emisor,
            IssuerSigningKeys = _llaves.GetSigningKeys(),
            ValidateAudience = false,          // el token de acceso de Cognito no trae aud; se valida emisor, firma y vencimiento
            ValidateLifetime = true,
        });
        return r.IsValid && r.Claims.TryGetValue("sub", out var sub) ? sub?.ToString() : null;
    }
}
