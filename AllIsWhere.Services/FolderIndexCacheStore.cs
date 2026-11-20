using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using AllIsWhere.Engine;

namespace AllIsWhere.Services;

internal static class FolderIndexCacheStore
{
    private const string Format = "AllIsWhere Folder Index 1";
    private const int MaximumEntries = 50_000_000;
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllIsWhere", "FolderIndexCache");

    public static bool Exists(string root) => File.Exists(GetCachePath(root));

    public static bool TryLoad(string root, SearchIndex index, CancellationToken token)
    {
        try
        {
            using var file = File.OpenRead(GetCachePath(root));
            using var compressed = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new BinaryReader(compressed);
            if (reader.ReadString() != Format ||
                !reader.ReadString().Equals(root, StringComparison.OrdinalIgnoreCase))
                return false;

            var entries = new List<(string Path, byte Flags)>();
            string prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            while (reader.ReadBoolean())
            {
                token.ThrowIfCancellationRequested();
                string path = Path.GetFullPath(reader.ReadString());
                byte flags = reader.ReadByte();
                if (entries.Count == MaximumEntries || flags > 3 ||
                    (!path.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                     !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    return false;
                entries.Add((path, flags));
            }
            // Read to EOF so a truncated or corrupt gzip trailer cannot be ignored.
            if (compressed.ReadByte() != -1) return false;
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                index.UpsertPath(entry.Path, (entry.Flags & 1) != 0, (entry.Flags & 2) != 0);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    public static void Save(string root, SearchIndex index)
    {
        Directory.CreateDirectory(CacheDirectory);
        string path = GetCachePath(root);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = File.Create(temporary))
            using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
            using (var writer = new BinaryWriter(compressed))
            {
                writer.Write(Format);
                writer.Write(root);
                foreach (var pair in index.PathEntries)
                {
                    var entry = pair.Value;
                    writer.Write(true);
                    writer.Write(entry.FullPath);
                    writer.Write((byte)((entry.IsDirectory ? 1 : 0) | (entry.IsHidden ? 2 : 0)));
                }
                writer.Write(false);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void Delete(string root) => File.Delete(GetCachePath(root));

    private static string GetCachePath(string root)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant()));
        return Path.Combine(CacheDirectory, Convert.ToHexString(hash) + ".awfolder");
    }
}
