using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AllIsWhere.Interop;
using AllIsWhere.Models;
using Microsoft.Win32.SafeHandles;

namespace AllIsWhere.Engine;

public sealed class NtfsVolumeReader
{
	private const int BufferSize = 1048576;

	private const int UsnV2HeaderSize = 60;

	private readonly char _driveLetter;

	public NtfsVolumeReader(char driveLetter)
	{
		_driveLetter = char.ToUpperInvariant(driveLetter);
	}

	public long GetCurrentUsn()
	{
		return GetJournalCheckpoint().NextUsn;
	}

	public JournalCheckpoint GetJournalCheckpoint()
	{
		using SafeFileHandle device = NativeMethods.OpenVolume(_driveLetter);
		byte[] array = new byte[MarshalSize<NativeMethods.UsnJournalData>()];
		if (!NativeMethods.DeviceIoControl(device, 590068u, IntPtr.Zero, 0, array, array.Length, out var bytesReturned, IntPtr.Zero) || bytesReturned < array.Length)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal bilgisi okunamadı.");
		}
		NativeMethods.UsnJournalData usnJournalData = ReadStruct<NativeMethods.UsnJournalData>(array);
		return new JournalCheckpoint(usnJournalData.UsnJournalId, usnJournalData.FirstUsn, usnJournalData.NextUsn);
	}

	public async IAsyncEnumerable<IndexedEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken, Func<string>? performanceProvider = null)
	{
		using SafeFileHandle volume = NativeMethods.OpenVolume(_driveLetter);
		byte[] array = new byte[MarshalSize<NativeMethods.UsnJournalData>()];
		if (!NativeMethods.DeviceIoControl(volume, 590068u, IntPtr.Zero, 0, array, array.Length, out var bytesReturned, IntPtr.Zero) || bytesReturned < array.Length)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal bilgisi okunamadı.");
		}
		NativeMethods.UsnJournalData usnJournalData = ReadStruct<NativeMethods.UsnJournalData>(array);
		NativeMethods.MftEnumData cursor = new NativeMethods.MftEnumData
		{
			StartFileReferenceNumber = 0uL,
			LowUsn = 0L,
			HighUsn = usnJournalData.NextUsn
		};
		byte[] buffer = new byte[1048576];
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!NativeMethods.Control(volume, 590003u, in cursor, buffer, out var bytesReturned2))
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				if (lastWin32Error == 38)
				{
					break;
				}
				throw new Win32Exception(lastWin32Error, "MFT kayıtları okunamadı.");
			}
			if (bytesReturned2 < 8)
			{
				break;
			}
			ulong nextCursor = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(0, 8));
			uint recordLength;
			for (int offset = 8; offset + 60 <= bytesReturned2; offset += checked((int)recordLength))
			{
				recordLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
				if (recordLength < 60 || recordLength > bytesReturned2 - offset)
				{
					break;
				}
				if (BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 4, 2)) == 2)
				{
					ulong fileId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 8, 8));
					ulong parentFileId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 16, 8));
					uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 52, 4));
					ushort num = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 56, 2));
					ushort num2 = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 58, 2));
					if ((num & 1) == 0 && num2 + num <= recordLength)
					{
						string name = Encoding.Unicode.GetString(buffer, offset + num2, num);
						yield return new IndexedEntry(fileId, parentFileId, name, attributes, 0L);
					}
				}
			}
			if (nextCursor == cursor.StartFileReferenceNumber)
			{
				break;
			}
			cursor.StartFileReferenceNumber = nextCursor;
			string text = performanceProvider?.Invoke() ?? "Balanced";
			if (!(text == "Fast"))
			{
				if (!(text == "LowResource"))
				{
					await Task.Delay(2, cancellationToken);
				}
				else
				{
					await Task.Delay(8, cancellationToken);
				}
			}
			else
			{
				await Task.Yield();
			}
		}
	}

	internal static T ReadStruct<T>(byte[] bytes) where T : struct
	{
		GCHandle gCHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
		try
		{
			return Marshal.PtrToStructure<T>(gCHandle.AddrOfPinnedObject());
		}
		finally
		{
			gCHandle.Free();
		}
	}

	private static int MarshalSize<T>() where T : struct
	{
		return Marshal.SizeOf<T>();
	}
}
