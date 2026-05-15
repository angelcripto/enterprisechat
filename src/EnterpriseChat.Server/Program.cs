using EnterpriseChat.Licensing.Abstractions;
using EnterpriseChat.Server.Auth;
using EnterpriseChat.Server.Bootstrap;
using EnterpriseChat.Server.Crypto;
using EnterpriseChat.Server.Data;
using EnterpriseChat.Server.Admin;
using EnterpriseChat.Server.Engagement;
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
    var resetRequested = EnterpriseChat.Server.Bootstrap.AdminPasswordResetCli.TryExtractPassword(args, out var newAdminPwd, out var remainingArgs);
    args = remainingArgs;
    if (resetRequested)
    {
        Environment.ExitCode = await EnterpriseChat.Server.Bootstrap.AdminPasswordResetCli.RunAsync(newAdminPwd);
        return;
    }

    EnterpriseChat.Server.Bootstrap.InteractiveLauncher.RunIfInteractive(args, Directory.GetCurrentDirectory());

    Log.Information("EnterpriseChat.Server arrancando.");

    // Cuando el server corre como Windows Service, sc.exe no fija el
    // WorkingDirectory: el SCM arranca el proceso con CurrentDirectory =
    // C:\Windows\System32. Eso rompe la resolución relativa de wwwroot/,
    // data/chat.db y los logs configurados con paths relativos. Forzamos
    // ContentRootPath = AppContext.BaseDirectory (la carpeta donde está
    // el .exe / .dll del server) para que las paths relativas funcionen
    // igual que en Linux (systemd ya pone WorkingDirectory=/opt/enterprisechat).
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    Directory.SetCurrentDirectory(AppContext.BaseDirectory);

    // Same binary runs as console (dev), Windows Service or systemd unit.
    builder.Host.UseWindowsService(o => o.ServiceName = "EnterpriseChat");
    builder.Host.UseSystemd();

    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Master key del crypto symmetric. Tiene que estar lista ANTES de
    // que se registren providers externos: cifran credenciales con ella
    // y abortar tarde implicaría un fallo en la primera petición admin.
    using (var bootstrapLoggerFactory = LoggerFactory.Create(b => b.AddSerilog(Log.Logger)))
    {
        var masterKeyBytes = MasterKeyInitializer.EnsureMasterKey(
            builder.Configuration,
            builder.Environment,
            bootstrapLoggerFactory.CreateLogger("Bootstrap.MasterKey"));
        builder.Services.AddSingleton(new AppCrypto(masterKeyBytes));
    }

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
    await app.Services.GetRequiredService<EnterpriseChat.Server.Auth.Providers.AuthProviderRegistry>()
        .ReloadAsync();
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

    app.UseRateLimiter();

    app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
    app.MapGet("/license", (ILicenseValidator licensing) => Results.Ok(licensing.Current));
    app.MapAuthEndpoints();
    app.MapUserEndpoints();
    app.MapAdminEndpoints();
    app.MapAuthProviderAdminEndpoints();
    app.MapRoomEndpoints();
    app.MapSearchEndpoints();
    app.MapFileEndpoints();
    app.MapLicenseAdminEndpoints();
    app.MapEngagementEndpoints();
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
