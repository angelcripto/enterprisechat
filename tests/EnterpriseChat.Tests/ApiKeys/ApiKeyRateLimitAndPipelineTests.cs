using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using EnterpriseChat.Server.ApiKeys;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Tests.Server;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseChat.Tests.ApiKeys;

/// <summary>
/// Casos del pipeline que faltaban tras las fases 2 y 3: rate limit por
/// PAT, fallback de query string en <c>/files</c>, caducidad/revocación
/// vía REST (no solo testeada a nivel servicio) y resistencia a
/// tampering del token.
/// </summary>
public sealed class ApiKeyRateLimitAndPipelineTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public ApiKeyRateLimitAndPipelineTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Rate_limit_PAT_bloquea_la_peticion_61_con_429_y_retry_after_60()
    {
        var (_, plaintext) = await IssuePatAsync("smoke-ratelimit", UserRole.User);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        // El endpoint /license es público y barato — sirve para 61 hits
        // sin tocar BD pesada. La policy `unlimited` del rate limiter
        // sólo se activa cuando el caller NO es PAT, así que aquí sí
        // contamos contra el bucket de 60 req/min.
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 61; i++)
        {
            var resp = await client.GetAsync(new Uri("/license", UriKind.Relative));
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rejected = resp;
                break;
            }
            resp.EnsureSuccessStatusCode();
        }

        rejected.Should().NotBeNull("la petición 61 dentro de la misma ventana debe ser rechazada");
        rejected!.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(60),
            "Retry-After se anuncia explícitamente desde ApiKeyAuthExtensions.OnRejected");
    }

    [Fact]
    public async Task Rate_limit_no_aplica_a_JWT_humano()
    {
        var jwt = await GetAdminTokenAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        // El bucket de 60 RPM es per-PAT; un humano con JWT cae en la
        // partición `unlimited`, no debe haber rechazos.
        for (var i = 0; i < 65; i++)
        {
            var resp = await client.GetAsync(new Uri("/license", UriKind.Relative));
            resp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"petición {i + 1}/65 con JWT humano no debería verse afectada por el bucket de PAT");
        }
    }

    [Fact]
    public async Task PAT_en_query_string_de_files_es_procesada_por_el_handler()
    {
        // El endpoint /files/{id} requiere un userId humano (parsea `sub`
        // como int), así que una PAT acabará rechazada por el endpoint
        // mismo — pero ANTES de eso el handler de auth tiene que haber
        // procesado el token desde ?api_key=. Lo verificamos observando
        // que LastUsedAt queda escrito en BD: si está, el handler corrió.
        var (record, plaintext) = await IssuePatAsync("smoke-files-query", UserRole.User);
        var client = _factory.CreateClient();

        var path = $"/files/999999?api_key={Uri.EscapeDataString(plaintext)}";
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        // El endpoint rechaza por sub no-numérico (Unauthorized) — eso
        // es esperado y NO el foco del test.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);

        // Lo importante: el handler dejó huella en BD. Si LastUsedAt
        // siguiera null, el fallback de query no se aplicó.
        await using var db = await CreateDbAsync();
        var fresh = await db.ApiKeys.SingleAsync(k => k.Id == record.Id);
        fresh.LastUsedAt.Should().NotBeNull(
            "el handler debe leer el token desde ?api_key= cuando el path es /files");
    }

    [Fact]
    public async Task PAT_caducada_devuelve_401_a_traves_del_pipeline()
    {
        var (record, plaintext) = await IssuePatAsync("smoke-expired-pipeline", UserRole.User);

        // Forzamos la caducidad en BD (el servicio nunca permitiría una
        // expiresAt en el pasado al crear).
        await using (var db = await CreateDbAsync())
        {
            var row = await db.ApiKeys.SingleAsync(k => k.Id == record.Id);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        var response = await client.GetAsync(new Uri("/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PAT_revocada_devuelve_401_a_traves_del_pipeline()
    {
        var (record, plaintext) = await IssuePatAsync("smoke-revoked-pipeline", UserRole.User);
        var service = _factory.Services.GetRequiredService<ApiKeyService>();
        await service.RevokeAsync(record.Id, actorUserId: null, reason: "test");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);
        var response = await client.GetAsync(new Uri("/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PAT_alterada_un_solo_caracter_es_rechazada()
    {
        var (_, plaintext) = await IssuePatAsync("smoke-tamper", UserRole.User);

        // Bit-flip del último char del secreto. SHA-256 cambia
        // radicalmente → no encuentra fila en BD → 401.
        var lastChar = plaintext[^1];
        var swapped = lastChar == 'A' ? 'B' : 'A';
        var tampered = plaintext[..^1] + swapped;

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        var response = await client.GetAsync(new Uri("/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // --- Helpers ---

    private async Task<(ApiKey Record, string Plaintext)> IssuePatAsync(string displayName, UserRole role)
    {
        var svc = _factory.Services.GetRequiredService<ApiKeyService>();
        var issued = await svc.IssueAsync(displayName, role, createdByUserId: null);
        return (issued.Record, issued.Plaintext);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("admin", ChatServerFactory.AdminPassword));
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        return payload!.AccessToken;
    }

    private async Task<ChatDbContext> CreateDbAsync()
    {
        var factory = _factory.Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        return await factory.CreateDbContextAsync();
    }
}
