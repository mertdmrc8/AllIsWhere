using System;
using System.IO;
using Microsoft.Win32;

namespace AllIsWhere.Services;

public static class StartupRegistration
{
	private const string RunKeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string ValueName = "AllIsWhere";

	public static void Apply(bool enabled)
	{
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true) ?? throw new InvalidOperationException("Windows başlangıç kayıt anahtarı açılamadı.");
		if (!enabled)
		{
			registryKey.DeleteValue("AllIsWhere", throwOnMissingValue: false);
			registryKey.DeleteValue("EverythingLite", throwOnMissingValue: false);
			return;
		}
		string? processPath = Environment.ProcessPath;
		if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
		{
			throw new InvalidOperationException("Uygulama EXE yolu belirlenemedi.");
		}
		registryKey.SetValue("AllIsWhere", "\"" + processPath + "\"", RegistryValueKind.String);
		registryKey.DeleteValue("EverythingLite", throwOnMissingValue: false);
	}
}
