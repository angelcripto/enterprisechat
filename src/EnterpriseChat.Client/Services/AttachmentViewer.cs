using System.Diagnostics;
using System.IO;
using System.Windows;
using EnterpriseChat.Client.Views;

namespace EnterpriseChat.Client.Services;

/// <summary>
/// Helper that opens an attachment in an appropriate viewer:
/// - Images: inline WPF <see cref="ImageViewerWindow"/> with zoom + pan.
/// - Anything else: download to %TEMP%\EnterpriseChat\preview and shell-open
///   so PDF/Word/Excel/other formats are handled by the user's default app.
/// </summary>
public static class AttachmentViewer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    public static async Task ViewAsync(AttachmentApiClient client, long attachmentId, string? originalFileName)
    {
        try
        {
            var ext = string.IsNullOrWhiteSpace(originalFileName)
                ? ""
                : Path.GetExtension(originalFileName).ToLowerInvariant();
            var tempDir = Path.Combine(Path.GetTempPath(), "EnterpriseChat", "preview");
            Directory.CreateDirectory(tempDir);

            var safeName = string.IsNullOrWhiteSpace(originalFileName)
                ? $"attachment-{attachmentId}{ext}"
                : Path.GetFileName(originalFileName);
            var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{safeName}");

            await client.DownloadToAsync(attachmentId, tempFile);

            if (ImageExtensions.Contains(ext))
            {
                var viewer = new ImageViewerWindow
                {
                    Owner = Application.Current.MainWindow
                };
                viewer.LoadFromFile(tempFile, safeName);
                viewer.Show();
            }
            else
            {
                // Default Windows shell open — handles PDF (Edge / Acrobat),
                // Word (.doc/.docx), Excel (.xls/.xlsx), etc. via user's default app.
                Process.Start(new ProcessStartInfo
                {
                    FileName = tempFile,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"No se pudo abrir el adjunto: {ex.Message}",
                "EnterpriseChat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
