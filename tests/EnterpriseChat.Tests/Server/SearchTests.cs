using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using EnterpriseChat.Protocol.Admin;
using EnterpriseChat.Protocol.Search;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace EnterpriseChat.Tests.Server;

public sealed class SearchTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public SearchTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_finds_own_dm_message_by_keyword()
    {
        var anaToken = await CreateAndLoginAsync("anaSearch", "anaSearchPass");
        var luisToken = await CreateAndLoginAsync("luisSearch", "luisSearchPass");

        var anaId = await GetUserIdAsync(anaToken);
        var luisId = await GetUserIdAsync(luisToken);

        await using var anaHub = BuildHub(anaToken);
        await using var luisHub = BuildHub(luisToken);
        await anaHub.StartAsync();
        await luisHub.StartAsync();

        await anaHub.InvokeAsync<long>("SendDirectMessage", luisId, "El presupuesto trimestral incluye material");
        await luisHub.InvokeAsync<long>("SendDirectMessage", anaId, "Confirmo presupuesto recibido");

        var http = await AuthedHttpAsync(anaToken);
        var response = await http.GetAsync(new Uri("/search?q=presupuesto&limit=10", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        payload.Should().NotBeNull();
        payload!.Hits.Should().HaveCountGreaterThanOrEqualTo(2);
        payload.Hits.Should().OnlyContain(h => h.Body.Contains("presupuesto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Search_does_not_return_messages_outside_visibility()
    {
        var (anaToken, anaId) = await CreateLoginGetIdAsync("anaIso", "anaIsoPass");
        var (luisToken, _) = await CreateLoginGetIdAsync("luisIso", "luisIsoPass");
        var (mariaToken, mariaId) = await CreateLoginGetIdAsync("mariaIso", "mariaIsoPass");

        await using var anaHub = BuildHub(anaToken);
        await using var luisHub = BuildHub(luisToken);
        await anaHub.StartAsync();
        await luisHub.StartAsync();

        // Ana ↔ Luis exchange with a unique token.
        await anaHub.InvokeAsync<long>("SendDirectMessage", await GetUserIdAsync(luisToken), "topsecretZZZ entre nosotros");

        var mariaHttp = await AuthedHttpAsync(mariaToken);
        var response = await mariaHttp.GetAsync(new Uri("/search?q=topsecretZZZ&limit=10", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<SearchResponse>();
        payload!.Hits.Should().BeEmpty("Maria has no business reading Ana↔Luis DMs");
    }

    [Fact]
    public async Task Search_with_too_short_query_returns_400()
    {
        var http = await AuthedHttpAsync(await GetAdminTokenAsync());
        var response = await http.GetAsync(new Uri("/search?q=a", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<string> CreateAndLoginAsync(string username, string password)
    {
        var adminHttp = await AuthedHttpAsync(await GetAdminTokenAsync());
        var createResp = await adminHttp.PostAsJsonAsync("/admin/users",
            new CreateUserRequest(username, password, $"FN {username}", null, null));
        if (!createResp.IsSuccessStatusCode && createResp.StatusCode != HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException($"Setup failed: create user '{username}' returned {(int)createResp.StatusCode}.");
        }

        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        return payload!.AccessToken;
    }

    private async Task<(string Token, int UserId)> CreateLoginGetIdAsync(string username, string password)
    {
        var token = await CreateAndLoginAsync(username, password);
        return (token, await GetUserIdAsync(token));
    }

    private async Task<int> GetUserIdAsync(string token)
    {
        var http = await AuthedHttpAsync(token);
        // Trick: hit /license to confirm the token, then decode sub from the JWT.
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        return int.Parse(jwt.Claims.First(c => c.Type == "sub").Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/auth/login",
            new LoginRequest("admin", ChatServerFactory.AdminPassword));
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        return payload!.AccessToken;
    }

    private Task<HttpClient> AuthedHttpAsync(string token)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Task.FromResult(http);
    }

    private HubConnection BuildHub(string token)
    {
        var server = _factory.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/chat"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }
}
