using System.IO;

namespace EnterpriseChat.TrayMonitor.Services;

/// <summary>
/// Localiza el directorio de instalación del servidor EnterpriseChat
/// probando, en orden:
///
///   1. La variable de entorno <c>ENTERPRISECHAT_INSTALL_DIR</c>
///      (escape para dev / instalaciones a medida).
///   2. El directorio padre del TrayMonitor cuando éste vive en
///      <c>{install}\TrayMonitor\</c> (caso producción tras el installer
///      Inno Setup).
///   3. Rutas conocidas estándar Windows:
///        C:\Program Files\EnterpriseChat
///        C:\Program Files (x86)\EnterpriseChat
///
/// Devuelve la primera ruta donde existe <c>EnterpriseChat.Server.exe</c>;
/// si ninguna existe, devuelve la opción (2) como fallback útil (los
/// logs se intentarán leer de ahí y los servicios verán que el binario
/// no existe, mostrando un mensaje claro en lugar de un crash).
/// </summary>
public static class InstallDirLocator
{
    private const string ServerBinary = "EnterpriseChat.Server.exe";

    public static string FindServerInstallDir(string trayBaseDir)
    {
        var candidates = new List<string>();

        var envOverride = Environment.GetEnvironmentVariable("ENTERPRISECHAT_INSTALL_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            candidates.Add(envOverride);
        }

        // Producción: TrayMonitor vive en {install}\TrayMonitor\.
        candidates.Add(Path.GetFullPath(Path.Combine(trayBaseDir, "..")));

        // Rutas conocidas en Windows.
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf)) candidates.Add(Path.Combine(pf, "EnterpriseChat"));
        if (!string.IsNullOrEmpty(pfx)) candidates.Add(Path.Combine(pfx, "EnterpriseChat"));

        foreach (var dir in candidates)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            if (File.Exists(Path.Combine(dir, ServerBinary)))
            {
                return dir;
            }
        }

        // Fallback: padre del TrayMonitor. Permite al resto del código
        // funcionar (logs vacíos, server-binary-missing detectable) sin
        // tirar NullReferenceException.
        return Path.GetFullPath(Path.Combine(trayBaseDir, ".."));
    }
}
