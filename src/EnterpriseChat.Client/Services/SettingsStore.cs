using System.IO;
using System.Text.Json;

namespace EnterpriseChat.Client.Services;

/// <summary>
/// Persists <see cref="ChatSettings"/> as JSON under
/// <c>%APPDATA%\EnterpriseChat\settings.json</c>. Safe to call from any thread;
/// load happens once at startup.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private readonly object _gate = new();

    public SettingsStore()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(roaming, "EnterpriseChat");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");
    }

    public ChatSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_settingsPath))
            {
                return new ChatSettings();
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<ChatSettings>(json, JsonOpts) ?? new ChatSettings();
            }
            catch (JsonException)
            {
                // Settings file corrupted — fall back to defaults rather than crash the app.
                return new ChatSettings();
            }
        }
    }

    public void Save(ChatSettings settings)
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(_settingsPath, json);
        }
    }
}
