using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace EnterpriseChat.Tests.Server;

public sealed class ChatHubIntegrationTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public ChatHubIntegrationTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_connection_is_rejected()
    {
        await using var connection = BuildHubConnection(token: null);

        var act = async () => await connection.StartAsync();

        await act.Should().ThrowAsync<Exception>(
            "the hub is decorated with [Authorize] and rejects anonymous negotiates");
    }

    [Fact]
    public async Task Authenticated_connection_succeeds_and_DM_roundtrips()
    {
        var (token, userId) = await LoginAsync();

        ChatMessage? received = null;
        var gotMessage = new TaskCompletionSource<ChatMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = BuildHubConnection(token);
        connection.On<ChatMessage>(nameof(IChatClient.OnMessageReceived), msg =>
        {
            received = msg;
            gotMessage.TrySetResult(msg);
        });

        await connection.StartAsync();

        var serverId = await connection.InvokeAsync<long>(
            "SendDirectMessage",
            userId,
            "hola, prueba de roundtrip");

        var delivered = await Task.WhenAny(gotMessage.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        delivered.Should().BeSameAs(gotMessage.Task, "the message should be delivered within 5s");

        received.Should().NotBeNull();
        received!.Body.Should().Be("hola, prueba de roundtrip");
        received.ServerId.Should().Be(serverId);
        received.FromUserId.Should().Be(userId);
        received.ToUserId.Should().Be(userId);
    }

    private HubConnection BuildHubConnection(string? token)
    {
        var server = _factory.Server;
        var hubUri = new Uri(server.BaseAddress, "/hubs/chat");

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                if (token is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }
            })
            .Build();
    }

    private async Task<(string Token, int UserId)> LoginAsync()
    {
        var http = _factory.CreateClient();
        var response = await http.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("admin", ChatServerFactory.AdminPassword));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginResponse>();
        payload.Should().NotBeNull();
        return (payload!.AccessToken, payload.UserId);
    }
}
