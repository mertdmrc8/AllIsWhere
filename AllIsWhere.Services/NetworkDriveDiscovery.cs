using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AllIsWhere.Services;

public static class NetworkDriveDiscovery
{
	private const int ErrorMoreData = 234;

	public static IReadOnlyDictionary<char, string> GetMappings()
	{
		Dictionary<char, string> dictionary = new Dictionary<char, string>();
		DriveInfo[] drives = DriveInfo.GetDrives();
		foreach (DriveInfo driveInfo in drives)
		{
			if (driveInfo.Name.Length >= 3 && driveInfo.Name[1] == ':' && driveInfo.DriveType == DriveType.Network && TryGetRemotePath(driveInfo.Name.Substring(0, 2), out string remotePath))
			{
				dictionary[char.ToUpperInvariant(driveInfo.Name[0])] = remotePath;
			}
		}
		try
		{
			using RegistryKey? registryKey = Registry.CurrentUser.OpenSubKey("Network");
			if (registryKey == null)
			{
				return dictionary;
			}
			string[] subKeyNames = registryKey.GetSubKeyNames();
			foreach (string text in subKeyNames)
			{
				if (text.Length != 1 || !char.IsLetter(text[0]))
				{
					continue;
				}
				using RegistryKey? registryKey2 = registryKey.OpenSubKey(text);
				string? text2 = registryKey2?.GetValue("RemotePath") as string;
				if (!string.IsNullOrWhiteSpace(text2))
				{
					char key = char.ToUpperInvariant(text[0]);
					dictionary.TryAdd(key, NormalizeUncRoot(text2));
				}
			}
		}
		catch (UnauthorizedAccessException)
		{
		}
		return dictionary;
	}

	public static bool TryResolveToMappedPath(string path, out string mappedPath)
	{
		string text = path.TrimEnd('\\');
		foreach (KeyValuePair<char, string> item in from item in GetMappings()
			orderby item.Value.Length descending
			select item)
		{
			string text2 = NormalizeUncRoot(item.Value);
			if (text.Equals(text2, StringComparison.OrdinalIgnoreCase) || text.StartsWith(text2 + "\\", StringComparison.OrdinalIgnoreCase))
			{
				string text3 = text.Substring(text2.Length).TrimStart('\\');
				mappedPath = ((text3.Length == 0) ? $"{item.Key}:\\" : $"{item.Key}:\\{text3}");
				return true;
			}
		}
		mappedPath = string.Empty;
		return false;
	}

	private static bool TryGetRemotePath(string localName, out string remotePath)
	{
		remotePath = string.Empty;
		StringBuilder stringBuilder = new StringBuilder(32768);
		int length = stringBuilder.Capacity;
		int num = WNetGetConnection(localName, stringBuilder, ref length);
		if (num != 0 && num != 234)
		{
			return false;
		}
		if (num == 234)
		{
			stringBuilder = new StringBuilder(length);
			num = WNetGetConnection(localName, stringBuilder, ref length);
		}
		if (num != 0 || stringBuilder.Length == 0)
		{
			return false;
		}
		remotePath = NormalizeUncRoot(stringBuilder.ToString());
		return true;
	}

	private static string NormalizeUncRoot(string path)
	{
		return path.TrimEnd('\\');
	}

	[DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
}
