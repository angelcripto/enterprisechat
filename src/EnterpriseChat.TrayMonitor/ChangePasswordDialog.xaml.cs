using System.Windows;
using Wpf.Ui.Controls;

namespace EnterpriseChat.TrayMonitor;

public partial class ChangePasswordDialog : FluentWindow
{
    private const int MinPasswordLength = 4;

    /// <summary>
    /// Contraseña aceptada por el operador tras pulsar "Guardar".
    /// Solo se rellena si DialogResult = true. NUNCA se serializa ni se
    /// loguea: la usa MainViewModel para una sola llamada al CLI y se
    /// descarta inmediatamente.
    /// </summary>
    public string AcceptedPassword { get; private set; } = string.Empty;

    public ChangePasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NewPasswordBox.Focus();
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var pwd1 = NewPasswordBox.Password ?? string.Empty;
        var pwd2 = ConfirmPasswordBox.Password ?? string.Empty;

        if (pwd1.Length == 0 && pwd2.Length == 0)
        {
            ErrorLabel.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = false;
            return;
        }

        if (pwd1.Length < MinPasswordLength)
        {
            ShowError($"La contraseña debe tener al menos {MinPasswordLength} caracteres.");
            return;
        }

        if (pwd1 != pwd2)
        {
            ShowError("Las dos contraseñas no coinciden.");
            return;
        }

        ErrorLabel.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = true;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        AcceptedPassword = NewPasswordBox.Password ?? string.Empty;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        AcceptedPassword = string.Empty;
        DialogResult = false;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Limpieza defensiva: los PasswordBox dejan el SecureString interno
        // hasta que se recolecta el control. Forzamos clear para reducir
        // ventana de retencion.
        NewPasswordBox.Clear();
        ConfirmPasswordBox.Clear();
        base.OnClosed(e);
    }
}
