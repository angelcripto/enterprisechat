using System.Net;
using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using FluentAssertions;

namespace EnterpriseChat.Tests.Server;

public sealed class LoginEndpointTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public LoginEndpointTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_with_seeded_admin_returns_jwt()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("admin", ChatServerFactory.AdminPassword));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        payload.Should().NotBeNull();
        payload!.Username.Should().Be("admin");
        payload.Role.Should().Be("Admin");
        payload.AccessToken.Should().NotBeNullOrWhiteSpace();
        payload.AccessToken.Split('.').Should().HaveCount(3, "a JWT has three dot-separated segments");
        payload.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("admin", "not-the-real-password"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_unknown_user_returns_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("ghost", "whatever"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_with_empty_credentials_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("", ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
