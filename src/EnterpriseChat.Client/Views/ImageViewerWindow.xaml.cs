using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace EnterpriseChat.Client.Views;

public partial class ImageViewerWindow : FluentWindow
{
    private Point _lastMousePosition;
    private bool _isDragging;

    public ImageViewerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Load image from a local file path. Caller is responsible for cleaning
    /// up the file (typically temp-folder cache).
    /// </summary>
    public void LoadFromFile(string path, string displayName)
    {
        Title = displayName;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        ImageElement.Source = bmp;
        StatusText.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight} px · {FormatFileSize(new FileInfo(path).Length)}";
    }

    private void OnImageMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(Scale.ScaleX * factor, 0.1, 20);
        Scale.ScaleX = newScale;
        Scale.ScaleY = newScale;
        StatusText.Text = $"{Math.Round(newScale * 100)}%";
    }

    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _lastMousePosition = e.GetPosition(this);
        ImageElement.CaptureMouse();
    }

    private void OnImageMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var current = e.GetPosition(this);
        Translate.X += current.X - _lastMousePosition.X;
        Translate.Y += current.Y - _lastMousePosition.Y;
        _lastMousePosition = current;
    }

    private void OnImageMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ImageElement.ReleaseMouseCapture();
    }

    private void OnFit_Click(object sender, RoutedEventArgs e)
    {
        Scale.ScaleX = Scale.ScaleY = 1;
        Translate.X = Translate.Y = 0;
        StatusText.Text = "100%";
    }

    private void OnReset_Click(object sender, RoutedEventArgs e) => OnFit_Click(sender, e);

    private static string FormatFileSize(long size)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double s = size;
        int i = 0;
        while (s >= 1024 && i < units.Length - 1) { s /= 1024; i++; }
        return $"{s:F1} {units[i]}";
    }
}
