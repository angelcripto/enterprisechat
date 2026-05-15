using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EnterpriseChat.Server.ApiKeys;

/// <summary>
/// Handler que valida un PAT y construye un <see cref="ClaimsPrincipal"/>
/// sintético compatible con el resto del pipeline (mismos nombres de claim
/// que JwtTokenIssuer: <c>sub</c>, <c>jti</c>, <c>ClaimTypes.Role</c>) para
/// que <c>RequireRole("Admin")</c> y <c>SubClaimUserIdProvider</c> funcionen
/// sin cambios.
///
/// El <c>sub</c> sintético tiene formato <c>apikey:&lt;id&gt;</c> en lugar de
/// un userId numérico. Los endpoints que parseen <c>sub</c> como entero
/// (DMs, /me/inbox, hub SignalR) fallarán al hacerlo — por diseño: las PAT
/// son tokens de servicio, no impersonan un usuario humano.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ApiKeyService _service;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiKeyService service)
        : base(options, logger, encoder)
    {
        _service = service;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ExtractToken(Context.Request);
        if (token is null)
        {
            // Sin token PAT presente: dejar que JwtBearer (u otros schemes)
            // lo intenten. NoResult NO marca el endpoint como rechazado.
            return AuthenticateResult.NoResult();
        }

        var ip = Context.Connection.RemoteIpAddress?.ToString();
        var key = await _service.ResolveAsync(token, ip, Context.RequestAborted);
        if (key is null)
        {
            // El cliente envió un PAT, pero no es válido (no existe, está
            // revocado o expirado). Hay que fallar explícitamente para que
            // se devuelva 401 — si devolviésemos NoResult, el pipeline
            // intentaría JWT con el mismo plaintext y respondería con un
            // mensaje confuso ("malformed JWT").
            return AuthenticateResult.Fail("API key inválida, revocada o caducada.");
        }

        var claims = new[]
        {
            // sub sintético: `apikey:N` deja claro a cualquier endpoint
            // downstream que esto NO es un userId numérico de la tabla Users.
            new Claim(JwtRegisteredClaimNames.Sub, $"apikey:{key.Id}"),
            new Claim(JwtRegisteredClaimNames.Jti, $"apikey:{key.Id}"),
            new Claim(JwtRegisteredClaimNames.Name, key.DisplayName),
            new Claim(ClaimTypes.Role, key.Role.ToString()),
            new Claim(ApiKeyAuthenticationDefaults.KeyTypeClaim, ApiKeyAuthenticationDefaults.KeyTypeValue)
        };

        var identity = new ClaimsIdentity(
            claims,
            authenticationType: ApiKeyAuthenticationDefaults.Scheme,
            nameType: JwtRegisteredClaimNames.Name,
            roleType: ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Busca el PAT en:
    /// <list type="number">
    ///   <item>Header <c>Authorization: Bearer ec_pat_…</c>.</item>
    ///   <item>Query <c>?api_key=ec_pat_…</c> sólo si el path es <c>/files</c>
    ///         o <c>/hubs</c> (mismo criterio que el fallback de JwtBearer).</item>
    /// </list>
    /// Devuelve <c>null</c> si no hay ningún token PAT presente, sin distinguir
    /// "no enviado" de "enviado pero no es PAT" — eso lo decide el caller.
    /// </summary>
    private static string? ExtractToken(HttpRequest request)
    {
        const string BearerPrefix = "Bearer ";
        var auth = request.Headers.Authorization.ToString();
        if (auth.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = auth[BearerPrefix.Length..].Trim();
            if (candidate.StartsWith(ApiKeyAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        var path = request.Path;
        if (path.StartsWithSegments("/hubs") || path.StartsWithSegments("/files"))
        {
            var qs = request.Query["api_key"].ToString();
            if (qs.StartsWith(ApiKeyAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal))
            {
                return qs;
            }
        }

        return null;
    }
}
