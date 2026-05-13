using System.Text;
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

            // SignalR needs the bearer in the `access_token` query string because
            // browser WebSocket APIs cannot set custom headers on the upgrade.
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = ctx =>
                {
                    var accessToken = ctx.Request.Query["access_token"].ToString();
                    var path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        ctx.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        }
    }
}
