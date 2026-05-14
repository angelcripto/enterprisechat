using System.Diagnostics;
using System.IO;

namespace EnterpriseChat.TrayMonitor.Services;

/// <summary>
/// Invoca el CLI nativo del servidor para rotar la contraseña del usuario
/// "admin". Comando: <c>EnterpriseChat.Server.exe --reset-admin-password &lt;new&gt;</c>
/// (definido en src/EnterpriseChat.Server/Bootstrap/AdminPasswordResetCli.cs).
///
/// El CLI actualiza el hash BCrypt directamente en la base SQLite. Funciona
/// con el servicio Windows corriendo porque SQLite WAL soporta lectores +
/// escritores concurrentes — el mismo enfoque usado por el wrapper de la
/// extensión Plesk en Linux.
/// </summary>
public sealed class AdminPasswordResetClient
{
    private readonly string _serverExe;
    private readonly string _workingDir;

    public AdminPasswordResetClient(string installDir)
    {
        _workingDir = installDir;
        _serverExe = Path.Combine(installDir, "EnterpriseChat.Server.exe");
    }

    public bool ServerBinaryExists() => File.Exists(_serverExe);

    public async Task<PasswordResetResult> RunAsync(string newPassword, CancellationToken ct = default)
    {
        if (!ServerBinaryExists())
        {
            return new PasswordResetResult(false, $"No se encuentra el binario del servidor: {_serverExe}");
        }

        // ProcessStartInfo: arguments pasa como argv separados; el binario
        // los recibe sin pasar por cmd.exe, así que no hay riesgo de
        // expansion de caracteres especiales.
        var psi = new ProcessStartInfo
        {
            FileName = _serverExe,
            WorkingDirectory = _workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--reset-admin-password");
        psi.ArgumentList.Add(newPassword);
        // Forzar el environment que el server lee de appsettings.Production.json.
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

        try
        {
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start devolvió null.");
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode == 0)
            {
                return new PasswordResetResult(true, stdout.Trim());
            }

            var msg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return new PasswordResetResult(false, $"exit code {proc.ExitCode}: {msg.Trim()}");
        }
        catch (Exception ex)
        {
            return new PasswordResetResult(false, ex.Message);
        }
    }
}

public sealed record PasswordResetResult(bool Ok, string Message);
