using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;

namespace EnterpriseChat.Server.Bootstrap;

/// <summary>
/// Pretty TUI bootstrap shown when the server is launched without flags from
/// an interactive console. Lets the operator pick between Development (test)
/// and Production modes and auto-generates the secrets needed for that
/// environment when they are missing.
///
/// Skipped automatically when:
///   - running as Windows Service / systemd (no TTY).
///   - ASPNETCORE_ENVIRONMENT is already set.
///   - any CLI arg includes `--service` or `--no-interactive`.
/// </summary>
internal static class InteractiveLauncher
{
    public static void RunIfInteractive(string[] args, string contentRoot)
    {
        if (!ShouldRun(args))
        {
            return;
        }

        AnsiConsole.Clear();
        RenderBanner();

        var mode = AskMode();
        var environment = mode switch
        {
            ServerMode.Test => "Development",
            ServerMode.Prod => "Production",
            _ => "Development"
        };

        EnsureSecrets(environment, contentRoot);

        AnsiConsole.MarkupLine(string.Empty);
        AnsiConsole.Write(new Rule($"[bold green]Arrancando en modo[/] [bold yellow]{environment}[/]")
            .Justify(Justify.Left)
            .RuleStyle("grey"));
        AnsiConsole.MarkupLine($"  [grey]Servidor:[/] [bold]http://localhost:5080[/]");
        AnsiConsole.MarkupLine($"  [grey]Healthz:[/]  [bold]http://localhost:5080/healthz[/]");
        AnsiConsole.MarkupLine($"  [grey]License:[/]  [bold]GET /license  ·  POST /admin/license[/]");
        AnsiConsole.MarkupLine(string.Empty);

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
    }

    private static bool ShouldRun(string[] args)
    {
        if (Environment.UserInteractive == false)
        {
            return false;
        }
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")))
        {
            return false;
        }
        foreach (var a in args)
        {
            if (a.Equals("--service", StringComparison.OrdinalIgnoreCase)
                || a.Equals("--no-interactive", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static void RenderBanner()
    {
        AnsiConsole.Write(new FigletText("EnterpriseChat")
            .Centered()
            .Color(Color.Aqua));

        var subtitle = new Panel(
            new Markup("[grey]Servidor de mensajería corporativa · open source · v0.1.0-alpha.3[/]"))
        {
            Border = BoxBorder.None,
            Padding = new Padding(0, 0, 0, 1)
        };
        AnsiConsole.Write(subtitle);
    }

    private static ServerMode AskMode()
    {
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[bold]¿En qué modo quieres arrancar el servidor?[/]")
                .PageSize(5)
                .HighlightStyle(new Style(foreground: Color.Black, background: Color.Aqua))
                .AddChoices(new[]
                {
                    "Test (Development) — claves de prueba, logs detallados",
                    "Prod (Production) — claves persistentes, logs reducidos"
                }));

        return choice.StartsWith("Test", StringComparison.Ordinal)
            ? ServerMode.Test
            : ServerMode.Prod;
    }

    private static void EnsureSecrets(string environment, string contentRoot)
    {
        var settingsFile = Path.Combine(contentRoot, $"appsettings.{environment}.json");
        var node = LoadOrCreate(settingsFile);

        var jwt = EnsureSection(node, "EnterpriseChat", "Jwt");
        var bootstrap = EnsureSection(node, "EnterpriseChat", "Bootstrap");

        var changed = false;

        if (string.IsNullOrWhiteSpace(jwt["SigningKey"]?.GetValue<string>()))
        {
            jwt["SigningKey"] = GenerateSigningKey();
            changed = true;
            AnsiConsole.MarkupLine($"  [yellow]·[/] SigningKey generada para [bold]{environment}[/].");
        }
        if (string.IsNullOrWhiteSpace(jwt["Issuer"]?.GetValue<string>()))
        {
            jwt["Issuer"] = $"EnterpriseChat.{environment}";
            changed = true;
        }
        if (string.IsNullOrWhiteSpace(jwt["Audience"]?.GetValue<string>()))
        {
            jwt["Audience"] = $"EnterpriseChat.Clients.{environment}";
            changed = true;
        }
        if (jwt["AccessTokenLifetimeMinutes"] is null)
        {
            jwt["AccessTokenLifetimeMinutes"] = environment == "Development" ? 240 : 60;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(bootstrap["AdminPassword"]?.GetValue<string>()))
        {
            string adminPwd;
            if (environment == "Development")
            {
                adminPwd = "1234";
                AnsiConsole.MarkupLine("  [yellow]·[/] Bootstrap admin password [bold]'1234'[/] (modo test).");
            }
            else
            {
                AnsiConsole.MarkupLine(string.Empty);
                AnsiConsole.MarkupLine("[bold red]Producción detectada y sin contraseña de admin inicial.[/]");
                adminPwd = AnsiConsole.Prompt(
                    new TextPrompt<string>("  Introduce contraseña para el usuario [bold]admin[/]:")
                        .Secret('*')
                        .Validate(p => p.Length >= 6
                            ? ValidationResult.Success()
                            : ValidationResult.Error("[red]Mínimo 6 caracteres.[/]")));
            }
            bootstrap["AdminPassword"] = adminPwd;
            changed = true;
        }

        if (changed)
        {
            SaveJson(settingsFile, node);
            AnsiConsole.MarkupLine($"  [green]✓[/] Configuración escrita en [grey]{Path.GetFileName(settingsFile)}[/].");
        }
        else
        {
            AnsiConsole.MarkupLine($"  [green]✓[/] Configuración existente en [grey]{Path.GetFileName(settingsFile)}[/] correcta.");
        }
    }

    private static JsonObject LoadOrCreate(string path)
    {
        if (File.Exists(path))
        {
            var raw = File.ReadAllText(path);
            try
            {
                if (JsonNode.Parse(raw) is JsonObject existing)
                {
                    return existing;
                }
            }
            catch (JsonException)
            {
                // Fall through and recreate.
            }
        }
        return new JsonObject();
    }

    private static JsonObject EnsureSection(JsonObject root, string parent, string child)
    {
        if (root[parent] is not JsonObject parentObj)
        {
            parentObj = new JsonObject();
            root[parent] = parentObj;
        }
        if (parentObj[child] is not JsonObject childObj)
        {
            childObj = new JsonObject();
            parentObj[child] = childObj;
        }
        return childObj;
    }

    private static void SaveJson(string path, JsonObject node)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var text = node.ToJsonString(options);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string GenerateSigningKey()
    {
        var buffer = new byte[48];
        RandomNumberGenerator.Fill(buffer);
        return Convert.ToBase64String(buffer);
    }

    private enum ServerMode
    {
        Test,
        Prod
    }
}
