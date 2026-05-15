using System.Net;
using System.Text.Json;
using EnterpriseChat.Tests.Server;
using FluentAssertions;

namespace EnterpriseChat.Tests.ApiDocs;

/// <summary>
/// Smoke E2E de la documentación pública (fase 5): confirma que el
/// OpenAPI JSON, Swagger UI, redirect y markdowns están en pie.
/// </summary>
public sealed class ApiDocsSmokeTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public ApiDocsSmokeTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenAPI_spec_es_publica_y_devuelve_json_valido()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/docs/openapi/v1.json", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "el spec OpenAPI debe ser accesible sin autenticación para que devs externos lo lean");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("openapi").GetString().Should().StartWith("3.0",
            "Swashbuckle emite OpenAPI 3.0.x");
        root.GetProperty("info").GetProperty("title").GetString()
            .Should().Be("EnterpriseChat Server API");
        root.GetProperty("paths").EnumerateObject().Should().NotBeEmpty(
            "el spec debería listar al menos los endpoints actuales");
        // Security schemes registrados: JWT humano + PAT.
        var schemes = root.GetProperty("components").GetProperty("securitySchemes");
        schemes.TryGetProperty("JwtBearer", out _).Should().BeTrue();
        schemes.TryGetProperty("ApiKey", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Swagger_UI_responde_publicamente_en_docs_api()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/docs/api/index.html", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("swagger-ui",
            "Swagger UI inyecta su bundle bajo el id #swagger-ui — si no está, el middleware no se montó");
    }

    [Fact]
    public async Task Atajo_docs_redirige_a_docs_api()
    {
        // El cliente de tests NO sigue redirects por defecto, así vemos el 302 tal cual.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync(new Uri("/docs", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location?.OriginalString.Should().Be("/docs/api/");
    }

    [Fact]
    public async Task Markdowns_narrativos_se_sirven_con_mime_correcto()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/docs/getting-started.md", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "el .md vive en web/public/docs/ y Vite lo copia a wwwroot/docs/ en cada build");
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/markdown",
            "el FileExtensionContentTypeProvider del server mapea .md → text/markdown");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Guía rápida", "el contenido del .md debe ser el de la fase 5");
    }
}
