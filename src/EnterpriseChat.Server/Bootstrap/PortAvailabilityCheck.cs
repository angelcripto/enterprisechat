using System.Net;
using System.Net.Sockets;
using Spectre.Console;

namespace EnterpriseChat.Server.Bootstrap;

/// <summary>
/// Tries to bind a temporary listener on the configured Kestrel port before
/// the host fully boots, so that the user gets a clear "puerto ocupado"
/// message instead of a wall of stack trace. Returns <c>false</c> when the
/// port is taken; caller decides whether to wait for keypress and exit.
/// </summary>
internal static class PortAvailabilityCheck
{
    public static bool TryEnsureAvailable(IConfiguration config, out int port)
    {
        port = ExtractPort(config);
        if (port <= 0)
        {
            return true; // dynamic / unknown — let Kestrel handle it.
        }

        // Use IPAddress.Any so the precheck matches what Kestrel will try to bind
        // (0.0.0.0:port). A loopback-only listener would succeed even when the
        // port is already bound on 0.0.0.0 by another process.
        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            RenderPortBusy(port);
            return false;
        }
        catch
        {
            // Unrelated error: don't claim port-busy, let Kestrel surface the real exception.
            return true;
        }
    }

    public static void WaitForExitKey()
    {
        if (Console.IsInputRedirected || !Environment.UserInteractive)
        {
            return;
        }
        AnsiConsole.MarkupLine(string.Empty);
        AnsiConsole.MarkupLine("[grey]Pulsa cualquier tecla para cerrar esta ventana...[/]");
        try { Console.ReadKey(intercept: true); } catch (InvalidOperationException) { /* no console */ }
    }

    private static int ExtractPort(IConfiguration config)
    {
        var url = config["Kestrel:Endpoints:Http:Url"];
        if (string.IsNullOrWhiteSpace(url))
        {
            return 5080;
        }
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return parsed.IsDefaultPort && parsed.Scheme == "http" ? 80 : parsed.Port;
        }
        return 5080;
    }

    public static void RenderPortBusyForCaught(WebApplication? app, IConfiguration? config)
    {
        // Late-path fallback when Kestrel itself throws: we don't know the
        // port up-front from config, but we render a generic panel.
        var port = 0;
        if (config is not null)
        {
            port = ExtractPort(config);
        }
        else if (app is not null)
        {
            port = ExtractPort(app.Configuration);
        }
        RenderPortBusy(port > 0 ? port : 5080);
    }

    private static void RenderPortBusy(int port)
    {
        var lines = new[]
        {
            $"[bold]El puerto {port} está ocupado.[/]",
            string.Empty,
            "Otro proceso está escuchando en él (otra instancia del servidor, un IDE, otro servicio…).",
            string.Empty,
            "[bold]Soluciones:[/]",
            $"  • Cierra el proceso que ocupa el puerto.",
            $"    Windows:  [cyan]netstat -ano | findstr :{port}[/]  →  [cyan]taskkill /PID <pid> /F[/]",
            $"    Linux:    [cyan]ss -lptn 'sport = :{port}'[/]",
            $"  • O cambia el puerto en [grey]appsettings.json[/] sección [grey]Kestrel.Endpoints.Http.Url[/].",
        };

        var panel = new Panel(new Markup(string.Join('\n', lines)))
        {
            Header = new PanelHeader(" ❌ Puerto ocupado ", Justify.Center),
            Border = BoxBorder.Heavy,
            BorderStyle = new Style(foreground: Color.Red),
            Padding = new Padding(2, 1, 2, 1)
        };
        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
}
