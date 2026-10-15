using System.IO;
using Microsoft.Win32;

namespace AllIsWhere.Services;

/// <summary>Registers the app for the current Windows user without administrator access.</summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AllIsWhere";

    public static void Apply(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows başlangıç kayıt anahtarı açılamadı.");

        if (!enabled)
        {
            runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            runKey.DeleteValue("EverythingLite", throwOnMissingValue: false);
            return;
        }

        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new InvalidOperationException("Uygulama EXE yolu belirlenemedi.");

        runKey.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
        runKey.DeleteValue("EverythingLite", throwOnMissingValue: false);
    }
}
