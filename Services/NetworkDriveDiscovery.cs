using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AllIsWhere.Services;

/// <summary>Finds Windows network drive mappings that may be hidden in elevated sessions.</summary>
public static class NetworkDriveDiscovery
{
    private const int ErrorMoreData = 234;

    /// <summary>
    /// Reads active Windows drive mappings first, then persistent mappings in the user profile.
    /// Returns stored drive letters and UNC targets even when no active mapping is available.
    /// </summary>
    public static IReadOnlyDictionary<char, string> GetMappings()
    {
        var mappings = new Dictionary<char, string>();

        // WNetGetConnection resolves mapped letters to UNC paths in the current session.
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.Name.Length < 3 || drive.Name[1] != ':' || drive.DriveType != DriveType.Network)
                continue;

            if (TryGetRemotePath(drive.Name.Substring(0, 2), out string remotePath))
                mappings[char.ToUpperInvariant(drive.Name[0])] = remotePath;
        }

        // UAC-elevated processes may not see mapped drives from the normal user session.
        // HKCU\Network stores the letters and UNC targets for persistent mappings.
        try
        {
            using RegistryKey? networkKey = Registry.CurrentUser.OpenSubKey(@"Network");
            if (networkKey is null) return mappings;

            foreach (string driveName in networkKey.GetSubKeyNames())
            {
                if (driveName.Length != 1 || !char.IsLetter(driveName[0])) continue;
                using RegistryKey? driveKey = networkKey.OpenSubKey(driveName);
                string? remotePath = driveKey?.GetValue("RemotePath") as string;
                if (string.IsNullOrWhiteSpace(remotePath)) continue;

                char letter = char.ToUpperInvariant(driveName[0]);
                // Prefer active WNet mappings; use the registry only for missing letters.
                mappings.TryAdd(letter, NormalizeUncRoot(remotePath));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The mapping list may still contain active WNet results.
        }

        return mappings;
    }

    /// <summary>Maps a UNC path back to its installed drive-letter alias when one exists.</summary>
    public static bool TryResolveToMappedPath(string path, out string mappedPath)
    {
        string normalizedPath = path.TrimEnd('\\');
        foreach (var mapping in GetMappings().OrderByDescending(item => item.Value.Length))
        {
            string remoteRoot = NormalizeUncRoot(mapping.Value);
            if (!normalizedPath.Equals(remoteRoot, StringComparison.OrdinalIgnoreCase) &&
                !normalizedPath.StartsWith(remoteRoot + "\\", StringComparison.OrdinalIgnoreCase))
                continue;

            string suffix = normalizedPath.Substring(remoteRoot.Length).TrimStart('\\');
            mappedPath = suffix.Length == 0
                ? $"{mapping.Key}:\\"
                : $"{mapping.Key}:\\{suffix}";
            return true;
        }

        mappedPath = string.Empty;
        return false;
    }

    private static bool TryGetRemotePath(string localName, out string remotePath)
    {
        remotePath = string.Empty;
        var buffer = new StringBuilder(32768);
        int length = buffer.Capacity;
        int result = WNetGetConnection(localName, buffer, ref length);
        if (result != 0 && result != ErrorMoreData) return false;

        if (result == ErrorMoreData)
        {
            buffer = new StringBuilder(length);
            result = WNetGetConnection(localName, buffer, ref length);
        }
        if (result != 0 || buffer.Length == 0) return false;

        remotePath = NormalizeUncRoot(buffer.ToString());
        return true;
    }

    private static string NormalizeUncRoot(string path) => path.TrimEnd('\\');

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
}
