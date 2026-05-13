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
    EnterpriseChat.Server.Bootstrap.InteractiveLauncher.RunIfInteractive(args, Directory.GetCurrentDirectory());

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

    // CORS: only needed for `npm run dev` of the Vue SPA (different origin).
    // Production builds copy the SPA to wwwroot and are served from the same
    // origin, so CORS is irrelevant there.
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("WebSpaDev", policy =>
        {
            var origins = builder.Configuration
                .GetSection("EnterpriseChat:Cors:DevOrigins")
                .Get<string[]>()
                ?? new[] { "http://localhost:5173", "http://127.0.0.1:5173" };
            policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

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

    // Default + static files: serves the Vue SPA bundled into wwwroot/. In dev
    // the SPA runs via `npm run dev` on its own port and hits the API via CORS;
    // wwwroot may be empty during that flow, which is fine.
    app.UseDefaultFiles();
    app.UseStaticFiles();

    if (app.Environment.IsDevelopment())
    {
        app.UseCors("WebSpaDev");
    }

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

    // SPA fallback: anything that didn't match an API route or a real static
    // file is served by index.html so the Vue Router can handle client-side
    // paths like /channels/42, /dm/7, /login, etc.
    app.MapFallbackToFile("index.html");

    var licInfo = app.Services.GetRequiredService<ILicenseValidator>().Current;
    Log.Information(
        "Edición activa: {Edition} (máx. {Max} usuarios concurrentes).",
        licInfo.Edition,
        licInfo.MaxConcurrentUsers);

    // Pre-bind check so port-in-use is reported with a friendly message
    // before Kestrel throws a noisy stack trace.
    if (!PortAvailabilityCheck.TryEnsureAvailable(app.Configuration, out _))
    {
        PortAvailabilityCheck.WaitForExitKey();
        Environment.ExitCode = 2;
        return;
    }

    StartupBanner.Register(app);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Detect "address in use" anywhere in the InnerException chain — Kestrel
    // wraps the SocketException inside IOException → AddressInUseException.
    var addrInUse = false;
    for (Exception? current = ex; current is not null; current = current.InnerException)
    {
        if (current is System.Net.Sockets.SocketException se
            && se.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
        {
            addrInUse = true;
            break;
        }
    }

    if (addrInUse)
    {
        Log.Fatal("Kestrel no pudo enlazarse al puerto (otro proceso lo está usando).");
        PortAvailabilityCheck.RenderPortBusyForCaught(app: null, config: null);
        Environment.ExitCode = 2;
    }
    else
    {
        Log.Fatal(ex, "EnterpriseChat.Server ha terminado por una excepción no controlada.");
        Environment.ExitCode = 1;
    }

    if (Environment.UserInteractive && !Console.IsOutputRedirected)
    {
        PortAvailabilityCheck.WaitForExitKey();
    }
}
finally
{
    Log.CloseAndFlush();
}

// Exposed so WebApplicationFactory<Program> in the test project can host
// this server in-process. Required because top-level statements emit
// `internal class Program` otherwise.
public partial class Program;
