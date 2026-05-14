using System.IO;

namespace EnterpriseChat.TrayMonitor.Services;

/// <summary>
/// Lectura concurrente del archivo Serilog que escribe el servidor. El
/// server tiene <c>FileShare.ReadWrite</c> activo (Serilog "shared" =
/// true), por lo que abrir el archivo con <see cref="FileShare.ReadWrite"/>
/// no bloquea la escritura.
///
/// Devuelve las últimas N líneas. Diseñado para refrescar cada ~2 s; no
/// hace polling continuo dentro de la clase para mantener UI MVVM en
/// control del timing.
/// </summary>
public sealed class LogTail
{
    private readonly string _logsDirectory;

    public LogTail(string installDir)
    {
        _logsDirectory = Path.Combine(installDir, "logs");
    }

    public IReadOnlyList<string> ReadLastLines(int maxLines)
    {
        if (!Directory.Exists(_logsDirectory))
        {
            return Array.Empty<string>();
        }

        string? latest;
        try
        {
            latest = Directory
                .EnumerateFiles(_logsDirectory, "server-*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }

        if (latest is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            using var stream = new FileStream(latest, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            // Buffer circular: leemos línea por línea descartando antiguas;
            // así no cargamos el archivo entero en memoria si hay un log
            // grande con días de actividad.
            var ring = new string[maxLines];
            var count = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                ring[count % maxLines] = line;
                count++;
            }

            var actual = Math.Min(count, maxLines);
            var result = new string[actual];
            var start = count >= maxLines ? count % maxLines : 0;
            for (var i = 0; i < actual; i++)
            {
                result[i] = ring[(start + i) % maxLines];
            }
            return result;
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }
}
