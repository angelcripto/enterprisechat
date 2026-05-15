using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using EnterpriseChat.Protocol.Admin;
using FluentAssertions;

namespace EnterpriseChat.Tests.Server;

public sealed class AdminEndpointsTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public AdminEndpointsTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Admin_can_list_users()
    {
        var http = await CreateAuthedClientAsAdminAsync();

        var response = await http.GetAsync(new Uri("/admin/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<AdminUserListResult>();
        page.Should().NotBeNull();
        page!.Rows.Should().ContainSingle(u => u.Username == "admin");
    }

    [Fact]
    public async Task Admin_can_create_user_and_user_can_login()
    {
        var http = await CreateAuthedClientAsAdminAsync();

        var create = await http.PostAsJsonAsync(
            "/admin/users",
            new CreateUserRequest(
                Username: "ana",
                Password: "anita123",
                FullName: "Ana García",
                Email: "ana@empresa.local",
                DepartmentId: null,
                Role: "User"));

        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("ana", "anita123"));

        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        payload!.Username.Should().Be("ana");
        payload.Role.Should().Be("User");
    }

    [Fact]
    public async Task Creating_duplicate_username_returns_409()
    {
        var http = await CreateAuthedClientAsAdminAsync();

        await http.PostAsJsonAsync(
            "/admin/users",
            new CreateUserRequest("dup", "passw0rd", "Dup User", null, null));

        var second = await http.PostAsJsonAsync(
            "/admin/users",
            new CreateUserRequest("dup", "passw0rd", "Dup User", null, null));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Non_admin_gets_403_on_admin_endpoints()
    {
        var adminHttp = await CreateAuthedClientAsAdminAsync();
        await adminHttp.PostAsJsonAsync(
            "/admin/users",
            new CreateUserRequest("luis", "luispass", "Luis Cano", null, null));

        var userHttp = await CreateAuthedClientAsync("luis", "luispass");

        var response = await userHttp.GetAsync(new Uri("/admin/users", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_gets_401_on_admin_endpoints()
    {
        var http = _factory.CreateClient();
        var response = await http.GetAsync(new Uri("/admin/users", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_reset_password()
    {
        var adminHttp = await CreateAuthedClientAsAdminAsync();
        var create = await adminHttp.PostAsJsonAsync(
            "/admin/users",
            new CreateUserRequest("pedro", "old-pass", "Pedro Ruiz", null, null));
        var created = await create.Content.ReadFromJsonAsync<AdminUserDetail>();

        var reset = await adminHttp.PostAsJsonAsync(
            $"/admin/users/{created!.Id}/reset-password",
            new ResetPasswordRequest("new-pass-9876"));
        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var anonymous = _factory.CreateClient();
        var loginOld = await anonymous.PostAsJsonAsync("/auth/login", new LoginRequest("pedro", "old-pass"));
        loginOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var loginNew = await anonymous.PostAsJsonAsync("/auth/login", new LoginRequest("pedro", "new-pass-9876"));
        loginNew.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_self()
    {
        var http = await CreateAuthedClientAsAdminAsync();
        var listResp = await http.GetAsync(new Uri("/admin/users", UriKind.Relative));
        var page = await listResp.Content.ReadFromJsonAsync<AdminUserListResult>();
        var admin = page!.Rows.Single(u => u.Username == "admin");

        var deactivate = await http.DeleteAsync(new Uri($"/admin/users/{admin.Id}", UriKind.Relative));

        deactivate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_can_create_and_list_departments()
    {
        var http = await CreateAuthedClientAsAdminAsync();

        var create = await http.PostAsJsonAsync(
            "/admin/departments",
            new CreateDepartmentRequest("Soporte"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);

        var listResp = await http.GetAsync(new Uri("/admin/departments", UriKind.Relative));
        var deps = await listResp.Content.ReadFromJsonAsync<List<DepartmentSummary>>();
        deps.Should().Contain(d => d.Name == "Soporte");
    }

    private async Task<HttpClient> CreateAuthedClientAsAdminAsync()
        => await CreateAuthedClientAsync("admin", ChatServerFactory.AdminPassword);

    private async Task<HttpClient> CreateAuthedClientAsync(string username, string password)
    {
        var http = _factory.CreateClient();
        var login = await http.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.AccessToken);
        return http;
    }
}
