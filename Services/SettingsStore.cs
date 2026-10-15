using System.IO;
using System.Text.Json;
using AllIsWhere.Models;

namespace AllIsWhere.Services;

public static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AllIsWhere", "settings.json");
    private static readonly string LegacySettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EverythingLite", "settings.json");

    public static UserSettings Load()
    {
        try
        {
            string sourcePath = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (File.Exists(sourcePath))
            {
                var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(sourcePath)) ?? new UserSettings();
                settings.Normalize();
                if (sourcePath == LegacySettingsPath)
                {
                    try { Save(settings); } // Migrate user data without discarding saved sources.
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall back to safe defaults when the settings file is unreadable or invalid.
        }
        return new UserSettings();
    }

    public static void Save(UserSettings settings)
    {
        string? directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
