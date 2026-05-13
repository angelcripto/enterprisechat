using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Server.Auth;
using EnterpriseChat.Server.Bootstrap;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Admin;
using EnterpriseChat.Server.Files;
using EnterpriseChat.Server.Hubs;
using EnterpriseChat.Server.Licensing;
using EnterpriseChat.Server.Rooms;
using EnterpriseChat.Server.Search;
using EnterpriseChat.Server.Users;
using Serilog;

// Two-stage Serilog init: bootstrap logger first so anything that fails before
// the host is built (config load, plugin scan, port binding) still gets logged.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("EnterpriseChat.Server arrancando.");

    var builder = WebApplication.CreateBuilder(args);

    // Same binary runs as console (dev), Windows Service or systemd unit.
    builder.Host.UseWindowsService(o => o.ServiceName = "EnterpriseChat");
    builder.Host.UseSystemd();

    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddSignalR();
    builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, EnterpriseChat.Server.Auth.SubClaimUserIdProvider>();
    builder.Services.AddChatPersistence(builder.Configuration);
    builder.Services.AddChatAuth(builder.Configuration);

    // Licensing: load Pro plugin if present, otherwise fall back to FreeLicenseValidator.
    builder.Services.AddEnterpriseChatLicensing(builder.Configuration, builder.Environment);
    builder.Services.AddSingleton<ConcurrentSessionCounter>();

    var app = builder.Build();

    await app.Services.InitializeChatDatabaseAsync();
    await AdminSeeder.SeedAdminIfEmptyAsync(
        app.Services,
        app.Configuration,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap"));
    await LicenseStartupRestorer.RestoreAsync(
        app.Services,
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Licensing"));

    app.UseSerilogRequestLogging();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
    app.MapGet("/license", (ILicenseValidator licensing) => Results.Ok(licensing.Current));
    app.MapAuthEndpoints();
    app.MapUserEndpoints();
    app.MapAdminEndpoints();
    app.MapRoomEndpoints();
    app.MapSearchEndpoints();
    app.MapFileEndpoints();
    app.MapLicenseAdminEndpoints();
    app.MapHub<ChatHub>("/hubs/chat");

    var licInfo = app.Services.GetRequiredService<ILicenseValidator>().Current;
    Log.Information(
        "Edición activa: {Edition} (máx. {Max} usuarios concurrentes).",
        licInfo.Edition,
        licInfo.MaxConcurrentUsers);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "EnterpriseChat.Server ha terminado por una excepción no controlada.");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

// Exposed so WebApplicationFactory<Program> in the test project can host
// this server in-process. Required because top-level statements emit
// `internal class Program` otherwise.
public partial class Program;
