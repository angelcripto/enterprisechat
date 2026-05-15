using System.Net;
using System.Net.Http.Headers;
using EnterpriseChat.Server.ApiKeys;
using EnterpriseChat.Server.Data.Entities;
using EnterpriseChat.Tests.Server;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseChat.Tests.ApiKeys;

/// <summary>
/// Smoke E2E del middleware contra el host real. Validamos que el
/// pipeline (UseAuthentication + UseAuthorization + policy AdminOnly +
/// rate limiter + endpoint group) acepta o rechaza correctamente las PATs.
/// </summary>
public sealed class ApiKeyAuthenticationTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public ApiKeyAuthenticationTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sin_token_un_endpoint_admin_devuelve_401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/admin/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PAT_Admin_pasa_policy_AdminOnly()
    {
        var (_, plaintext) = await IssuePatAsync("smoke-admin-pat", UserRole.Admin);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        var response = await client.GetAsync(new Uri("/admin/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PAT_User_recibe_403_en_endpoint_admin()
    {
        var (_, plaintext) = await IssuePatAsync("smoke-user-pat", UserRole.User);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", plaintext);

        var response = await client.GetAsync(new Uri("/admin/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PAT_invalida_devuelve_401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ec_pat_NoExisteEsteToken_xyz");

        var response = await client.GetAsync(new Uri("/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PAT_no_es_aceptada_en_el_hub_aunque_lleve_rol_Admin()
    {
        // El hub fija el scheme a JWT explícitamente; un PAT presentado por
        // query string debe quedarse en la puerta con 401 — no llegar al
        // OnConnectedAsync donde GetUserId() ya no podría parsearlo.
        var (_, plaintext) = await IssuePatAsync("smoke-hub-pat", UserRole.Admin);
        var client = _factory.CreateClient();

        var response = await client.GetAsync(
            new Uri($"/hubs/chat?access_token={Uri.EscapeDataString(plaintext)}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<(ApiKey Record, string Plaintext)> IssuePatAsync(string displayName, UserRole role)
    {
        var svc = _factory.Services.GetRequiredService<ApiKeyService>();
        var issued = await svc.IssueAsync(displayName, role, createdByUserId: null);
        return (issued.Record, issued.Plaintext);
    }
}
