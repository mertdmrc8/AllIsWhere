using System;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AllIsWhere.Interop;
using AllIsWhere.Models;
using Microsoft.Win32.SafeHandles;

namespace AllIsWhere.Engine;

public sealed class UsnJournalWatcher
{
	private const int BufferSize = 1048576;

	private const int UsnV2HeaderSize = 60;

	private const uint RelevantReasons = 45824u;

	private readonly char _driveLetter;

	private readonly SearchIndex _index;

	public UsnJournalWatcher(char driveLetter, SearchIndex index)
	{
		_driveLetter = driveLetter;
		_index = index;
	}

	public async Task RunAsync(long startingUsn, Action<string> status, Action changed, CancellationToken token, ulong expectedJournalId = 0uL, Action<long>? checkpointChanged = null)
	{
		using SafeFileHandle volume = NativeMethods.OpenVolume(_driveLetter);
		NativeMethods.UsnJournalData journal = QueryJournal(volume);
		ulong journalId = journal.UsnJournalId;
		if (expectedJournalId != 0L && journalId != expectedJournalId)
		{
			throw new InvalidOperationException("USN Journal yeniden oluşturuldu; indeks yeniden taranmalı.");
		}
		if (startingUsn < journal.FirstUsn || startingUsn > journal.NextUsn)
		{
			throw new InvalidOperationException("USN Journal cursor'ı geçersiz; indeks yeniden taranmalı.");
		}
		long cursor = startingUsn;
		DateTime nextJournalValidation = DateTime.UtcNow.AddSeconds(30.0);
		byte[] buffer = new byte[1048576];
		status("USN Journal izleniyor");
		while (!token.IsCancellationRequested)
		{
			if (DateTime.UtcNow >= nextJournalValidation)
			{
				journal = QueryJournal(volume);
				if (journal.UsnJournalId != journalId)
				{
					throw new InvalidOperationException("USN Journal yeniden oluşturuldu; indeks yeniden taranmalı.");
				}
				if (cursor < journal.FirstUsn || cursor > journal.NextUsn)
				{
					throw new InvalidOperationException("USN Journal cursor'ı geçersiz; indeks yeniden taranmalı.");
				}
				nextJournalValidation = DateTime.UtcNow.AddSeconds(30.0);
			}
			if (!NativeMethods.Control<NativeMethods.ReadUsnJournalData>(volume, 590011u, new NativeMethods.ReadUsnJournalData
			{
				StartUsn = cursor,
				ReasonMask = 45824u,
				ReturnOnlyOnClose = 0u,
				Timeout = 0uL,
				BytesToWaitFor = 0uL,
				UsnJournalId = journal.UsnJournalId
			}, buffer, out var bytesReturned))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal okunamadı.");
			}
			if (bytesReturned < 8)
			{
				await Task.Delay(250, token);
				continue;
			}
			cursor = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(0, 8));
			int i = 8;
			bool flag = false;
			uint num;
			for (; i + 60 <= bytesReturned; i += checked((int)num))
			{
				num = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i, 4));
				if (num < 60 || num > bytesReturned - i)
				{
					break;
				}
				if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(i + 4, 2)) != 2)
				{
					continue;
				}
				ulong fileId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i + 8, 8));
				ulong parentFileId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i + 16, 8));
				long lastUsn = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(i + 24, 8));
				uint num2 = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i + 40, 4));
				uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(i + 52, 4));
				ushort num3 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(i + 56, 2));
				ushort num4 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(i + 58, 2));
				if ((num3 & 1) == 0 && num4 + num3 <= num)
				{
					string text = Encoding.Unicode.GetString(buffer, i + num4, num3);
					if ((num2 & 0x200) != 0)
					{
						_index.Remove(fileId);
						flag = true;
					}
					else if ((num2 & 0x1000) == 0 && text.Length != 0)
					{
						_index.Upsert(new IndexedEntry(fileId, parentFileId, text, attributes, lastUsn));
						flag = true;
					}
				}
			}
			if (flag)
			{
				changed();
			}
			checkpointChanged?.Invoke(cursor);
			if (cursor >= journal.NextUsn)
			{
				await Task.Delay(150, token);
			}
		}
	}

	private static NativeMethods.UsnJournalData QueryJournal(SafeFileHandle volume)
	{
		byte[] array = new byte[Marshal.SizeOf<NativeMethods.UsnJournalData>()];
		if (!NativeMethods.DeviceIoControl(volume, 590068u, IntPtr.Zero, 0, array, array.Length, out var bytesReturned, IntPtr.Zero) || bytesReturned < array.Length)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal sorgulanamadı.");
		}
		return NtfsVolumeReader.ReadStruct<NativeMethods.UsnJournalData>(array);
	}
}
