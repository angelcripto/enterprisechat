using System.Text;
using EnterpriseChat.Server.Auth.Hashers;
using EnterpriseChat.Server.Auth.Providers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EnterpriseChat.Server.Auth;

internal static class AuthExtensions
{
    public static IServiceCollection AddChatAuth(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.AddOptions<JwtOptions>()
            .Bind(config.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<JwtTokenIssuer>();

        // Verificadores de hash para providers externos.
        services.AddSingleton<HashVerifierRegistry>();

        // Internal siempre presente. El resto los materializa el registry
        // dinámicamente a partir de AuthProviderConfig (PR 2: MySQL).
        services.AddSingleton<InternalAuthProvider>();
        services.AddSingleton<AuthProviderRegistry>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // Bind JwtBearerOptions from JwtOptions lazily so test hosts that override
        // configuration via ConfigureAppConfiguration still get picked up.
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerFromJwtOptions>();

        services.AddAuthorization();
        return services;
    }

    private sealed class ConfigureJwtBearerFromJwtOptions(IOptions<JwtOptions> jwtOptions)
        : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            if (name != JwtBearerDefaults.AuthenticationScheme)
            {
                return;
            }

            var jwt = jwtOptions.Value;

            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "name",
                RoleClaimType = System.Security.Claims.ClaimTypes.Role
            };

            // Two cases where the browser cannot send the bearer in a header
            // and we accept it via `?access_token=...` query string instead:
            //   /hubs/*  — WebSocket upgrade rejects custom headers.
            //   /files/* — <img src> / <a href> cannot set Authorization.
            // Both paths are still hit with the same auth requirements.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"].ToString();
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken)
                        && (path.StartsWithSegments("/hubs")
                            || path.StartsWithSegments("/files")))
                    {
                        ctx.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        }
    }
}
