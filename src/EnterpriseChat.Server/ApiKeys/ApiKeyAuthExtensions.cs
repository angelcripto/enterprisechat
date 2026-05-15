using System.Globalization;
using System.Threading.RateLimiting;
using EnterpriseChat.Server.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace EnterpriseChat.Server.ApiKeys;

/// <summary>
/// Wiring del módulo de API keys: registra el scheme, reconfigura la
/// authorization policy default para aceptar JWT o PAT, añade una policy
/// nombrada <c>AdminOnly</c> que pasa cualquiera de los dos schemes con
/// rol Admin, y monta un rate limiter global de 60 req/min por PAT.
///
/// Se invoca desde <c>AuthExtensions.AddChatAuth</c> tras configurar
/// JwtBearer para no duplicar imports en Program.cs.
/// </summary>
internal static class ApiKeyAuthExtensions
{
    /// <summary>Permisos por defecto del bucket fijo por PAT.</summary>
    public const int RateLimitPermitsPerMinute = 60;

    public static AuthenticationBuilder AddApiKeyAuth(this AuthenticationBuilder builder)
    {
        return builder.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationDefaults.Scheme,
            displayName: "API key (PAT)",
            configureOptions: _ => { });
    }

    /// <summary>
    /// Registra la policy nombrada <c>AdminOnly</c>. No fuerza schemes
    /// porque el default ya es el PolicyScheme <c>JwtOrApiKey</c>, que
    /// reenvía a JwtBearer o a ApiKey según el header — y desde ahí
    /// <c>RequireRole</c> ve el claim del principal correcto.
    /// </summary>
    public static IServiceCollection AddApiKeyAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(o =>
        {
            o.AddPolicy(ApiKeyAuthenticationDefaults.AdminPolicy, p => p
                .RequireAuthenticatedUser()
                .RequireRole(UserRole.Admin.ToString()));
        });
        return services;
    }

    /// <summary>
    /// Rate limiter global: 60 req/min por PAT, ilimitado para JWT humano
    /// y peticiones anónimas. La policy se evalúa dentro del pipeline tras
    /// <c>UseAuthentication</c>/<c>UseAuthorization</c>, así que el principal
    /// ya está poblado y podemos leer el claim <c>key_type</c>.
    /// </summary>
    public static IServiceCollection AddApiKeyRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var keyType = ctx.User.FindFirst(ApiKeyAuthenticationDefaults.KeyTypeClaim)?.Value;
                if (keyType != ApiKeyAuthenticationDefaults.KeyTypeValue)
                {
                    // JWT humano o petición anónima: sin límite por nuestra
                    // parte (Kestrel ya tiene su propio backpressure).
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                // Particionamos por jti, que para una PAT es `apikey:<id>`,
                // así que cada clave tiene su propio cubo independiente.
                var partitionKey = ctx.User.FindFirst("jti")?.Value ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RateLimitPermitsPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0
                });
            });

            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = (rejectionCtx, _) =>
            {
                // Las RFC dicen "Retry-After: segundos". Para un bucket fijo
                // el peor caso es esperar Window entero, devolvemos eso.
                rejectionCtx.HttpContext.Response.Headers.RetryAfter =
                    TimeSpan.FromMinutes(1).TotalSeconds.ToString(CultureInfo.InvariantCulture);
                return ValueTask.CompletedTask;
            };
        });
        return services;
    }
}
