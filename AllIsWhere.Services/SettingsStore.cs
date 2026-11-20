using System;
using System.IO;
using System.Text.Json;
using AllIsWhere.Models;

namespace AllIsWhere.Services;

public static class SettingsStore
{
	private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllIsWhere", "settings.json");

	private static readonly string LegacySettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EverythingLite", "settings.json");

	public static UserSettings Load()
	{
		try
		{
			string text = (File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath);
			if (File.Exists(text))
			{
				UserSettings userSettings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(text)) ?? new UserSettings();
				userSettings.Normalize();
				if (text == LegacySettingsPath)
				{
					try
					{
						Save(userSettings);
					}
					catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) ? true : false)
					{
					}
				}
				return userSettings;
			}
		}
		catch (Exception ex2) when ((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is JsonException) ? true : false)
		{
		}
		return new UserSettings();
	}

	public static void Save(UserSettings settings)
	{
		string directoryName = Path.GetDirectoryName(SettingsPath);
		if (!string.IsNullOrEmpty(directoryName))
		{
			Directory.CreateDirectory(directoryName);
		}
		string text = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, JsonSerializer.Serialize(settings, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			File.Move(text, SettingsPath, overwrite: true);
		}
		finally
		{
			try
			{
				File.Delete(text);
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}
}
