using System.Runtime.InteropServices;
using System.Windows;

namespace EnterpriseChat.TrayMonitor;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// Mutex global con scope "Local\" (sesión interactiva del user) que
    /// garantiza una sola instancia del TrayMonitor por sesión Windows.
    /// Si ya hay una instancia activa, ponemos su ventana al frente vía
    /// <c>FindWindow</c> + <c>ShowWindow</c>/<c>SetForegroundWindow</c>
    /// y salimos.
    /// </summary>
    private static readonly string MutexName = "Local\\EnterpriseChat.TrayMonitor.SingleInstance";
    private const string MainWindowTitle = "EnterpriseChat Server";

    private Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            BringExistingToFront();
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _instanceMutex?.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // El mutex pertenecía a otro hilo del proceso; ignoramos.
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    // ------ Win32 helpers para enfocar la instancia previa ------

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    private static void BringExistingToFront()
    {
        var hwnd = FindWindow(null, MainWindowTitle);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }
}
