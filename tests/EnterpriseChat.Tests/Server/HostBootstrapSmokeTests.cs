using System.Net;
using System.Net.Http.Json;
using EnterpriseChat.Licensing.Abstractions;
using FluentAssertions;

namespace EnterpriseChat.Tests.Server;

/// <summary>
/// In-process smoke tests for the server host: builds the real
/// <see cref="WebApplication"/> the same way `dotnet run` does and hits the
/// health and license endpoints. Fails fast if DI wiring, Serilog config or
/// SignalR registration regress.
/// </summary>
public sealed class HostBootstrapSmokeTests : IClassFixture<ChatServerFactory>
{
    private readonly ChatServerFactory _factory;

    public HostBootstrapSmokeTests(ChatServerFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Healthz_returns_ok()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<HealthzResponse>();
        payload.Should().NotBeNull();
        payload!.Status.Should().Be("ok");
    }

    [Fact]
    public async Task License_endpoint_reports_Free_edition_by_default()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/license", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var info = await response.Content.ReadFromJsonAsync<LicenseInfo>();
        info.Should().NotBeNull();
        info!.Edition.Should().Be(LicenseEdition.Free);
        info.MaxConcurrentUsers.Should().Be(FreeLicenseValidator.FreeUserCap);
    }

    private sealed record HealthzResponse(string Status);
}
