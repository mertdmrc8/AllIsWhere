using System;
using System.IO;
using System.IO.Compression;
using AllIsWhere.Engine;
using AllIsWhere.Models;

namespace AllIsWhere.Services;

public static class IndexCacheStore
{
	private const int Magic = 1096239448;

	private const int FormatVersion = 2;

	private const int MaximumEntryCount = 50000000;

	private const ulong EndMarker = ulong.MaxValue;

	private static readonly string CacheDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AllIsWhere", "IndexCache");

	public static bool TryLoad(char driveLetter, SearchIndex index, out CachedIndexSnapshot? snapshot)
	{
		snapshot = null;
		string cachePath = GetCachePath(driveLetter);
		if (!File.Exists(cachePath))
		{
			return false;
		}
		try
		{
			using FileStream stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			using GZipStream input = new GZipStream(stream, CompressionMode.Decompress);
			using BinaryReader binaryReader = new BinaryReader(input);
			if (binaryReader.ReadInt32() != 1096239448 || binaryReader.ReadInt32() != 2 || char.ToUpperInvariant(binaryReader.ReadChar()) != char.ToUpperInvariant(driveLetter))
			{
				return false;
			}
			ulong journalId = binaryReader.ReadUInt64();
			long nextUsn = binaryReader.ReadInt64();
			int num = 0;
			while (true)
			{
				ulong num2;
				try
				{
					num2 = binaryReader.ReadUInt64();
				}
				catch (EndOfStreamException)
				{
					return false;
				}
				if (num2 == ulong.MaxValue)
				{
					break;
				}
				if (++num > 50000000)
				{
					return false;
				}
				index.Upsert(new IndexedEntry(num2, binaryReader.ReadUInt64(), binaryReader.ReadString(), binaryReader.ReadUInt32(), binaryReader.ReadInt64()));
			}
			snapshot = new CachedIndexSnapshot(journalId, nextUsn);
			return true;
		}
		catch (Exception ex2) when ((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is InvalidDataException || ex2 is ArgumentException) ? true : false)
		{
			return false;
		}
	}

	public static void Save(char driveLetter, ulong journalId, long nextUsn, SearchIndex index)
	{
		Directory.CreateDirectory(CacheDirectory);
		string cachePath = GetCachePath(driveLetter);
		string text = cachePath + ".tmp";
		try
		{
			using (FileStream stream = new FileStream(text, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
			{
				using GZipStream output = new GZipStream(stream, CompressionLevel.Fastest);
				using BinaryWriter binaryWriter = new BinaryWriter(output);
				binaryWriter.Write(1096239448);
				binaryWriter.Write(2);
				binaryWriter.Write(char.ToUpperInvariant(driveLetter));
				binaryWriter.Write(journalId);
				binaryWriter.Write(nextUsn);
				foreach (IndexedEntry item in index.Snapshot())
				{
					binaryWriter.Write(item.FileId);
					binaryWriter.Write(item.ParentFileId);
					binaryWriter.Write(item.Name);
					binaryWriter.Write(item.Attributes);
					binaryWriter.Write(item.LastUsn);
				}
				binaryWriter.Write(ulong.MaxValue);
			}
			File.Move(text, cachePath, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public static void Delete(char driveLetter)
	{
		string cachePath = GetCachePath(driveLetter);
		if (File.Exists(cachePath))
		{
			File.Delete(cachePath);
		}
	}

	private static string GetCachePath(char driveLetter)
	{
		return Path.Combine(CacheDirectory, $"{char.ToUpperInvariant(driveLetter)}.awidx");
	}
}
