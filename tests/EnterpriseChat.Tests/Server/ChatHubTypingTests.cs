using System.Net.Http.Json;
using EnterpriseChat.Protocol;
using EnterpriseChat.Protocol.Admin;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace EnterpriseChat.Tests.Server;

/// <summary>
/// Cubre el aviso de "está escribiendo" en el hub.
///
/// El bug que motivó estos tests: el indicador se quedaba clavado para siempre
/// en el SPA porque nadie avisaba de que se había dejado de escribir — sólo
/// existía <c>Typing</c>. Se añadió <c>TypingStopped</c> como método y callback
/// SEPARADOS (no un bool en <c>OnTyping</c>) porque el cliente WPF registra
/// <c>OnTyping</c> con 3 argumentos y SignalR falla al enlazar si el servidor
/// manda 4 — en ejecución y en silencio, no en compilación.
/// </summary>
public sealed class ChatHubTypingTests : IClassFixture<LicensedChatServerFactory>
{
    private readonly LicensedChatServerFactory _factory;

    public ChatHubTypingTests(LicensedChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Typing_en_DM_llega_al_destinatario_con_el_id_del_emisor()
    {
        var (anaToken, anaId) = await CreateAndLoginAsync("anaTyping", "anaTypingPass");
        var (luisToken, luisId) = await CreateAndLoginAsync("luisTyping", "luisTypingPass");

        var got = new TaskCompletionSource<(int From, int? To, int? Room)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var luis = BuildHubConnection(luisToken);
        luis.On<int, int?, int?>(nameof(IChatClient.OnTyping),
            (from, to, room) => got.TrySetResult((from, to, room)));
        await luis.StartAsync();

        await using var ana = BuildHubConnection(anaToken);
        await ana.StartAsync();
        await ana.InvokeAsync("Typing", luisId, null);

        var done = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        done.Should().BeSameAs(got.Task, "el aviso debe llegar en menos de 5s");

        var (from, to, room) = await got.Task;
        from.Should().Be(anaId, "el receptor identifica la conversación por el emisor");
        to.Should().Be(luisId);
        room.Should().BeNull();
    }

    [Fact]
    public async Task TypingStopped_en_DM_llega_al_destinatario()
    {
        var (anaToken, anaId) = await CreateAndLoginAsync("anaStop", "anaStopPass");
        var (luisToken, luisId) = await CreateAndLoginAsync("luisStop", "luisStopPass");

        var got = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var luis = BuildHubConnection(luisToken);
        luis.On<int, int?, int?>(nameof(IChatClient.OnTypingStopped),
            (from, _, _) => got.TrySetResult(from));
        await luis.StartAsync();

        await using var ana = BuildHubConnection(anaToken);
        await ana.StartAsync();
        await ana.InvokeAsync("TypingStopped", luisId, null);

        var done = await Task.WhenAny(got.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        done.Should().BeSameAs(got.Task, "sin este aviso el indicador se queda clavado hasta que expire");
        (await got.Task).Should().Be(anaId);
    }

    /// <summary>
    /// El contrato que protege al cliente WPF: <c>OnTyping</c> debe seguir
    /// enviando EXACTAMENTE 3 argumentos. El WPF lo registra con
    /// <c>On&lt;int, int?, int?&gt;</c> y un cuarto argumento lo rompería en
    /// ejecución. Si alguien añade un parámetro, este test se pone rojo.
    /// </summary>
    [Fact]
    public async Task OnTyping_mantiene_su_firma_de_3_argumentos_para_no_romper_el_WPF()
    {
        var (anaToken, _) = await CreateAndLoginAsync("anaFirma", "anaFirmaPass");
        var (luisToken, luisId) = await CreateAndLoginAsync("luisFirma", "luisFirmaPass");

        var bound = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var luis = BuildHubConnection(luisToken);
        // Exactamente el mismo registro que hace EnterpriseChat.Client/Services/ChatClient.cs.
        luis.On<int, int?, int?>(nameof(IChatClient.OnTyping), (_, _, _) => bound.TrySetResult(true));
        luis.Closed += ex => { if (ex is not null) failed.TrySetResult(ex); return Task.CompletedTask; };
        await luis.StartAsync();

        await using var ana = BuildHubConnection(anaToken);
        await ana.StartAsync();
        await ana.InvokeAsync("Typing", luisId, null);

        var done = await Task.WhenAny(bound.Task, failed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        done.Should().BeSameAs(
            bound.Task,
            "un handler de 3 argumentos debe seguir enlazando: si OnTyping crece, el WPF deja de recibirlo");
    }

    [Fact]
    public async Task Typing_en_sala_no_se_devuelve_al_emisor()
    {
        var (anaToken, _) = await CreateAndLoginAsync("anaSala", "anaSalaPass");

        var echoed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var ana = BuildHubConnection(anaToken);
        ana.On<int, int?, int?>(nameof(IChatClient.OnTyping), (_, _, _) => echoed.TrySetResult(true));
        await ana.StartAsync();

        var roomId = await ana.InvokeAsync<int>("CreateRoom", "sala-typing", false);
        await ana.InvokeAsync("Typing", null, roomId);

        var done = await Task.WhenAny(echoed.Task, Task.Delay(TimeSpan.FromSeconds(1.5)));
        done.Should().NotBeSameAs(
            echoed.Task,
            "el emisor no debe verse a sí mismo escribiendo (Clients.GroupExcept)");
    }

    private HubConnection BuildHubConnection(string token)
    {
        var server = _factory.Server;
        var hubUri = new Uri(server.BaseAddress, "/hubs/chat");

        return new HubConnectionBuilder()
            .WithUrl(hubUri, options =>
            {
                // TestServer no habla WebSockets reales; LongPolling in-process sí.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
    }

    private async Task<(string Token, int UserId)> CreateAndLoginAsync(string username, string password)
    {
        var admin = _factory.CreateClient();
        var adminLogin = await admin.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin", ChatServerFactory.AdminPassword));
        adminLogin.EnsureSuccessStatusCode();
        var adminToken = (await adminLogin.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        admin.DefaultRequestHeaders.Authorization = new("Bearer", adminToken);

        var create = await admin.PostAsJsonAsync(
            "/admin/users", new CreateUserRequest(username, password, $"FN {username}", null, null));
        if (!create.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Setup failed: create user '{username}' returned {(int)create.StatusCode}.");
        }

        var anonymous = _factory.CreateClient();
        var login = await anonymous.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        login.EnsureSuccessStatusCode();
        var payload = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        return (payload.AccessToken, payload.UserId);
    }
}
