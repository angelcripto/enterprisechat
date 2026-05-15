using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using EnterpriseChat.Protocol.ApiKeys;
using EnterpriseChat.Tests.Server;
using FluentAssertions;

namespace EnterpriseChat.Tests.ApiKeys;

/// <summary>
/// Smoke E2E del CRUD REST: cubre el flujo completo Create → List → Get →
/// Rotate → Revoke contra el host real, autenticado como admin humano vía
/// JWT (no como PAT, para que la cobertura sea independiente de las
/// pruebas de <see cref="ApiKeyAuthenticationTests"/>).
/// </summary>
public sealed class ApiKeyAdminEndpointsTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public ApiKeyAdminEndpointsTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_devuelve_201_con_plaintext_y_aparece_en_List()
    {
        var http = await CreateAdminClientAsync();

        var create = await http.PostAsJsonAsync(
            "/admin/api-keys",
            new CreateApiKeyRequest("smoke-create", "User", ExpiresInDays: null, Notes: "ci"));

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var issued = await create.Content.ReadFromJsonAsync<IssuedApiKeyResponse>();
        issued.Should().NotBeNull();
        issued!.Plaintext.Should().StartWith("ec_pat_");
        issued.Key.Prefix.Should().StartWith("ec_pat_");
        issued.Key.Role.Should().Be("User");

        var list = await http.GetFromJsonAsync<ApiKeyListResult>("/admin/api-keys");
        list.Should().NotBeNull();
        list!.Rows.Should().Contain(r => r.Id == issued.Key.Id && r.DisplayName == "smoke-create");
        // No filtración accidental del secreto en el listado: las filas no
        // tienen plaintext y el hash no forma parte del DTO público.
        list.Rows.Should().NotContain(r => r.Prefix == issued.Plaintext);
    }

    [Fact]
    public async Task Create_con_role_invalido_devuelve_400()
    {
        var http = await CreateAdminClientAsync();

        var response = await http.PostAsJsonAsync(
            "/admin/api-keys",
            new CreateApiKeyRequest("smoke-bad-role", "Superuser", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_individual_devuelve_la_misma_fila()
    {
        var http = await CreateAdminClientAsync();
        var created = await CreateOneAsync(http, "smoke-get", "Admin");

        var fetched = await http.GetFromJsonAsync<ApiKeySummary>($"/admin/api-keys/{created.Key.Id}");

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Key.Id);
        fetched.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task Get_inexistente_devuelve_404()
    {
        var http = await CreateAdminClientAsync();

        var response = await http.GetAsync(new Uri("/admin/api-keys/999999", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rotate_emite_clave_nueva_con_RotatedFromId_y_revoca_la_anterior()
    {
        var http = await CreateAdminClientAsync();
        var original = await CreateOneAsync(http, "smoke-rotate", "User");

        var rotateResp = await http.PostAsJsonAsync(
            $"/admin/api-keys/{original.Key.Id}/rotate",
            new RotateApiKeyRequest(GraceSeconds: 0));

        rotateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await rotateResp.Content.ReadFromJsonAsync<IssuedApiKeyResponse>();
        rotated.Should().NotBeNull();
        rotated!.Plaintext.Should().NotBe(original.Plaintext);
        rotated.Key.Id.Should().NotBe(original.Key.Id);
        rotated.Key.RotatedFromId.Should().Be(original.Key.Id);

        // El listado por defecto oculta revocadas: la vieja ya no aparece;
        // la nueva sí.
        var active = await http.GetFromJsonAsync<ApiKeyListResult>("/admin/api-keys");
        active!.Rows.Should().NotContain(r => r.Id == original.Key.Id);
        active.Rows.Should().Contain(r => r.Id == rotated.Key.Id);

        // includeRevoked=true las trae todas y la vieja sale con RevokedAt set.
        var all = await http.GetFromJsonAsync<ApiKeyListResult>("/admin/api-keys?includeRevoked=true");
        var old = all!.Rows.Single(r => r.Id == original.Key.Id);
        old.RevokedAt.Should().NotBeNull();
        old.RevokeReason.Should().Be("rotated");
    }

    [Fact]
    public async Task Revoke_con_motivo_marca_la_fila_y_es_idempotente()
    {
        var http = await CreateAdminClientAsync();
        var created = await CreateOneAsync(http, "smoke-revoke-endpoint", "User");

        var first = await http.PostAsJsonAsync(
            $"/admin/api-keys/{created.Key.Id}/revoke",
            new RevokeApiKeyRequest(Reason: "leaked"));
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await http.PostAsJsonAsync(
            $"/admin/api-keys/{created.Key.Id}/revoke",
            new RevokeApiKeyRequest(Reason: null));
        second.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "una clave ya revocada no se puede volver a revocar");
    }

    [Fact]
    public async Task Delete_es_alias_de_revoke()
    {
        var http = await CreateAdminClientAsync();
        var created = await CreateOneAsync(http, "smoke-delete-endpoint", "User");

        var deleteResp = await http.DeleteAsync(new Uri($"/admin/api-keys/{created.Key.Id}", UriKind.Relative));
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var all = await http.GetFromJsonAsync<ApiKeyListResult>("/admin/api-keys?includeRevoked=true");
        var row = all!.Rows.Single(r => r.Id == created.Key.Id);
        row.RevokedAt.Should().NotBeNull();
        row.RevokeReason.Should().Be("deleted");
    }

    private async Task<IssuedApiKeyResponse> CreateOneAsync(HttpClient http, string name, string role)
    {
        var resp = await http.PostAsJsonAsync(
            "/admin/api-keys",
            new CreateApiKeyRequest(name, role, null, null));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<IssuedApiKeyResponse>())!;
    }

    private async Task<HttpClient> CreateAdminClientAsync()
    {
        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("admin", ChatServerFactory.AdminPassword));
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return client;
    }

}
