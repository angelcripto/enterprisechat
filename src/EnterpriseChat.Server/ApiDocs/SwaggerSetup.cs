using System.Reflection;
using EnterpriseChat.Server.ApiKeys;
using Microsoft.OpenApi.Models;

namespace EnterpriseChat.Server.ApiDocs;

/// <summary>
/// Wiring de Swashbuckle: OpenAPI 3.0 en <c>/docs/openapi/v1.json</c> y
/// Swagger UI público (sin auth) en <c>/docs/api</c>. La descripción del
/// scheme y los tags se enriquece con markdown que Swagger UI renderiza
/// nativamente, así que la spec sirve a la vez de docs narrativos.
/// </summary>
internal static class SwaggerSetup
{
    public const string DocumentName = "v1";
    public const string SwaggerJsonRoute = "docs/openapi/{documentName}.json";
    public const string SwaggerUiPrefix = "docs/api";

    public static IServiceCollection AddEnterpriseChatSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = "EnterpriseChat Server API",
                Version = "v1",
                Description = ApiDescription,
                Contact = new OpenApiContact
                {
                    Name = "EnterpriseChat",
                    Url = new Uri("https://enterprisechat.es"),
                },
                License = new OpenApiLicense
                {
                    Name = "AGPLv3 o comercial",
                    Url = new Uri("https://www.gnu.org/licenses/agpl-3.0.html"),
                },
            });

            // Esquemas de seguridad: JWT humano y PAT (ec_pat_*). Ambos se
            // presentan como `Authorization: Bearer …` — el servidor los
            // distingue por el prefijo del token (PolicyScheme JwtOrApiKey).
            c.AddSecurityDefinition("JwtBearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "JWT humano emitido por `POST /auth/login`. Caduca según la config del servidor (60 min por defecto)."
            });
            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "ec_pat_*",
                In = ParameterLocation.Header,
                Description = "Personal Access Token (PAT). Genérala desde **Administración → API keys** o `POST /admin/api-keys`. Sólo se entrega en claro una vez."
            });

            // Por defecto exigimos uno de los dos schemes. Los endpoints
            // públicos (`/healthz`, `/license`, `/auth/login`, `/docs/*`)
            // se marcan con `.AllowAnonymous()` y Swashbuckle omite el
            // candado automáticamente.
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "JwtBearer" } }] = Array.Empty<string>(),
                [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }] = Array.Empty<string>(),
            });

            // Descripciones de los tags. Cada `MapGroup(...).WithTags("X")`
            // se asocia a uno; el orden visual en Swagger UI sigue este
            // listado.
            c.TagActionsBy(api => new[]
            {
                api.GroupName
                    ?? api.ActionDescriptor.EndpointMetadata
                        .OfType<TagsAttribute>()
                        .FirstOrDefault()?.Tags.FirstOrDefault()
                    ?? api.RelativePath?.Split('/').FirstOrDefault()
                    ?? "Default"
            });

            // XML doc para que <summary>, <remarks>, <param>, <returns>
            // se rendericen en Swagger UI. Si el .xml no existe (build
            // de tests, por ejemplo), Swashbuckle continúa sin él.
            var xml = Path.Combine(AppContext.BaseDirectory,
                $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml))
            {
                c.IncludeXmlComments(xml, includeControllerXmlComments: true);
            }

            // Excluimos del spec los endpoints con [FromForm] IFormFile:
            // Swashbuckle 6.x peta al generar parámetros porque la
            // inferencia automática para multipart no soporta este patrón
            // en minimal APIs. Los endpoints siguen funcionando (REST OK),
            // solo no aparecen en Swagger UI. Documentados a mano en
            // getting-started.md mientras tanto.
            c.DocInclusionPredicate((_, api) =>
                !api.ParameterDescriptions.Any(p => p.Type == typeof(IFormFile)));
        });

        return services;
    }


    /// <summary>
    /// Monta Swagger JSON + UI con prefijos custom. Se llama DESPUÉS de
    /// <c>UseStaticFiles</c> y ANTES de <c>MapFallbackToFile</c>: si no,
    /// el SPA Vue se traga <c>/docs/api</c> y devuelve <c>index.html</c>.
    /// </summary>
    public static WebApplication UseEnterpriseChatSwagger(this WebApplication app)
    {
        app.UseSwagger(c => c.RouteTemplate = SwaggerJsonRoute);
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint($"/docs/openapi/{DocumentName}.json", "EnterpriseChat API v1");
            c.RoutePrefix = SwaggerUiPrefix;
            c.DocumentTitle = "EnterpriseChat — Documentación de la API";
            c.DefaultModelsExpandDepth(1);
            c.DisplayRequestDuration();
        });
        return app;
    }

    /// <summary>
    /// Markdown que aparece en la cabecera de Swagger UI. Markdown estándar
    /// (CommonMark) — Swagger UI lo renderiza nativamente.
    /// </summary>
    private const string ApiDescription = """
        API HTTP del servidor de chat **EnterpriseChat**. Diseñada para que
        developers externos construyan su propio cliente (CLI, bot, dashboard
        custom) sin depender del cliente WPF oficial.

        ## Autenticación

        Hay dos vías equivalentes para cualquier endpoint anotado con candado:

        - **JWT humano** — `POST /auth/login` con usuario y contraseña → JWT
          de TTL corto. Útil para clientes que representan a un usuario
          concreto (ven sus DMs, su inbox, etc.) y para usar el hub SignalR.
        - **PAT** — un Personal Access Token con prefijo `ec_pat_*` creado
          desde *Administración → API keys* (o `POST /admin/api-keys`).
          Pensado para integraciones de servicio: bots, CI, dashboards.
          Tienen rol `User` o `Admin` y se rotan/revocan desde la misma UI.

        Ambos viajan como `Authorization: Bearer <token>`. El servidor los
        distingue por el prefijo (`ec_pat_` ⇒ PAT, lo demás ⇒ JWT) y
        construye el `ClaimsPrincipal` con el rol correspondiente.

        ## Rate limits

        Las PAT están limitadas a **60 req/min** por clave (ventana fija).
        Al exceder el cap responde **`429 Too Many Requests`** con header
        `Retry-After: 60`. Las peticiones autenticadas con JWT humano no
        están limitadas por nuestro middleware.

        ## SignalR (chat en tiempo real)

        El hub `/hubs/chat` **solo acepta JWT humano**, no PAT. Las claves
        de servicio no tienen un userId numérico real y el hub depende de
        él para enrutar mensajes. Detalles del protocolo del hub en
        [`/docs/signalr-hub.md`](/docs/signalr-hub.md).

        ## Más documentación

        - [Guía rápida](/docs/getting-started.md)
        - [Autenticación al detalle](/docs/authentication.md)
        - [Eventos y métodos del hub SignalR](/docs/signalr-hub.md)
        - [Códigos de error](/docs/errors.md)
        """;
}
