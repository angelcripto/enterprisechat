using EnterpriseChat.Licensing.Abstractions;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

namespace EnterpriseChat.Server.Bootstrap;

/// <summary>
/// Renders a Spectre panel summarising the server state right after Kestrel
/// finishes binding. Shows which licence is active, the listening URLs and
/// the path to the SQLite database. Skipped when running headless (no TTY).
/// </summary>
internal static class StartupBanner
{
    public static void Register(WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() =>
        {
            try
            {
                Render(app);
            }
            catch
            {
                // Banner is decorative; never fail the host because of it.
            }
        });
    }

    private static void Render(WebApplication app)
    {
        var validator = app.Services.GetRequiredService<ILicenseValidator>();
        var info = validator.Current;
        var environment = app.Environment.EnvironmentName;

        var editionColor = info.Edition == LicenseEdition.Pro ? "green" : "yellow";
        var editionTag = info.Edition == LicenseEdition.Pro ? "PRO" : "FREE";

        var urls = string.Join(", ", app.Urls.DefaultIfEmpty("http://0.0.0.0:5080"));

        var lines = new List<string>
        {
            $"[bold]Modo:[/]      [cyan]{environment}[/]",
            $"[bold]Edición:[/]   [{editionColor}]{editionTag}[/]  ·  cap [bold]{info.MaxConcurrentUsers}[/] usuarios concurrentes",
        };

        if (info.LicensedTo is not null)
        {
            lines.Add($"[bold]A nombre:[/]  {Markup.Escape(info.LicensedTo)}");
        }
        if (info.ExpiresAt is { } exp)
        {
            var daysLeft = (int)Math.Round((exp - DateTimeOffset.UtcNow).TotalDays);
            var expColor = daysLeft < 30 ? "red" : "grey";
            lines.Add($"[bold]Expira:[/]    {exp.ToLocalTime():dd/MM/yyyy}  ([{expColor}]{daysLeft} días[/])");
        }
        else if (info.Edition == LicenseEdition.Free)
        {
            lines.Add("[bold]Expira:[/]    [grey]nunca (Free)[/]");
        }

        lines.Add($"[bold]Escucha:[/]   [link={urls}]{urls}[/]");
        lines.Add(string.Empty);
        lines.Add("[grey]Endpoints: /healthz · /license · /auth/login · /admin/license · /hubs/chat[/]");

        var content = string.Join('\n', lines);
        var panel = new Panel(new Markup(content))
        {
            Header = new PanelHeader(" EnterpriseChat Server arrancado ", Justify.Center),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(foreground: Color.Aqua),
            Padding = new Padding(2, 1, 2, 1)
        };
        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
}
