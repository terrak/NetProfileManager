using System.Text.Json;

using NetProfileManager.Core.Models;

namespace NetProfileManager.Core.Services;

public static class UiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetProfileManager",
        "ui-settings.json");

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath)) ?? new UiSettings();
                settings.HiddenAdapters = new HashSet<string>(settings.HiddenAdapters, StringComparer.OrdinalIgnoreCase);
                return settings;
            }
        }
        catch
        {
            // fichier illisible ou corrompu : on repart sur des réglages vierges
        }
        return new UiSettings();
    }

    public static void Save(UiSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
