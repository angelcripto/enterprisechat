using System.Net.Http.Headers;
using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using EnterpriseChat.Protocol.Admin;
using EnterpriseChat.Protocol.Rooms;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace EnterpriseChat.Tests.Server;

public sealed class RoomsIntegrationTests : IClassFixture<LicensedChatServerFactory>
{
    private readonly LicensedChatServerFactory _factory;

    public RoomsIntegrationTests(LicensedChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Two_users_can_chat_in_a_shared_room()
    {
        var (anaToken, anaId) = await SetupAndLoginAsync("ana", "anita123");
        var (luisToken, luisId) = await SetupAndLoginAsync("luis", "luispass");

        await using var anaHub = BuildHub(anaToken);
        await using var luisHub = BuildHub(luisToken);

        var luisGot = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        luisHub.On<ChatMessage>(nameof(IChatClient.OnMessageReceived), msg =>
        {
            if (msg.RoomId is not null) { luisGot.TrySetResult(msg); }
        });

        await anaHub.StartAsync();
        await luisHub.StartAsync();

        var roomId = await anaHub.InvokeAsync<int>("CreateRoom", "general", false);
        roomId.Should().BeGreaterThan(0);

        await luisHub.InvokeAsync("JoinRoom", roomId);

        var serverId = await anaHub.InvokeAsync<long>("SendRoomMessage", roomId, "hola sala");
        serverId.Should().BeGreaterThan(0);

        var delivered = await Task.WhenAny(luisGot.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        delivered.Should().BeSameAs(luisGot.Task);

        var received = await luisGot.Task;
        received.Body.Should().Be("hola sala");
        received.RoomId.Should().Be(roomId);
        received.FromUserId.Should().Be(anaId);
    }

    [Fact]
    public async Task Non_member_cannot_send_to_room()
    {
        var (anaToken, _) = await SetupAndLoginAsync("ana3", "ana3pass");
        var (luisToken, _) = await SetupAndLoginAsync("luis3", "luis3pass");

        await using var anaHub = BuildHub(anaToken);
        await using var luisHub = BuildHub(luisToken);
        await anaHub.StartAsync();
        await luisHub.StartAsync();

        var roomId = await anaHub.InvokeAsync<int>("CreateRoom", "privadita", true);

        var act = async () => await luisHub.InvokeAsync<long>("SendRoomMessage", roomId, "irrumpiendo");
        await act.Should().ThrowAsync<HubException>()
            .Where(ex => ex.Message.Contains("miembro", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Listing_rooms_returns_membership_flag()
    {
        var (anaToken, _) = await SetupAndLoginAsync("ana2", "anapass");
        var (luisToken, _) = await SetupAndLoginAsync("luis2", "luispass");

        await using var anaHub = BuildHub(anaToken);
        await anaHub.StartAsync();
        var roomId = await anaHub.InvokeAsync<int>("CreateRoom", "general-public", false);

        var luisHttp = await CreateAuthedHttpAsync(luisToken);
        var resp = await luisHttp.GetAsync(new Uri("/rooms", UriKind.Relative));
        resp.EnsureSuccessStatusCode();
        var rooms = await resp.Content.ReadFromJsonAsync<List<RoomSummary>>();

        rooms.Should().NotBeNull();
        var room = rooms!.SingleOrDefault(r => r.Id == roomId);
        room.Should().NotBeNull();
        room!.IsMember.Should().BeFalse();
    }

    private async Task<(string Token, int UserId)> SetupAndLoginAsync(string username, string password)
    {
        var adminHttp = await CreateAuthedHttpAsync(await GetAdminTokenAsync());
        var createResp = await adminHttp.PostAsJsonAsync("/admin/users",
            new CreateUserRequest(username, password, $"FN {username}", null, null));
        if (!createResp.IsSuccessStatusCode && createResp.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                $"Setup failed: create user '{username}' returned {(int)createResp.StatusCode}.");
        }

        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        return (payload!.AccessToken, payload.UserId);
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/auth/login",
            new LoginRequest("admin", ChatServerFactory.AdminPassword));
        var payload = await login.Content.ReadFromJsonAsync<LoginResponse>();
        return payload!.AccessToken;
    }

    private Task<HttpClient> CreateAuthedHttpAsync(string token)
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
