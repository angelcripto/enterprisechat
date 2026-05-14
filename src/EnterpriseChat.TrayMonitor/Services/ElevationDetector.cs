using System.Security.Principal;

namespace EnterpriseChat.TrayMonitor.Services;

/// <summary>
/// Detecta si el proceso corre con privilegios de administrador. Los
/// botones Start/Stop/Restart del servicio quedan deshabilitados cuando
/// la detección devuelve false; la UI ofrece relanzar la aplicación
/// elevada via <c>runas</c>.
/// </summary>
public static class ElevationDetector
{
    public static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
