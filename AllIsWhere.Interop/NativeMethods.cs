using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AllIsWhere.Interop;

internal static class NativeMethods
{
	internal struct MftEnumData
	{
		public ulong StartFileReferenceNumber;

		public long LowUsn;

		public long HighUsn;
	}

	// DeviceIoControl fills these fields in unmanaged code.
#pragma warning disable CS0649
	[StructLayout(LayoutKind.Sequential)]
	internal struct UsnJournalData
	{
		public ulong UsnJournalId;

		public long FirstUsn;

		public long NextUsn;

		public long LowestValidUsn;

		public long MaxUsn;

		public ulong MaximumSize;

		public ulong AllocationDelta;
	}

#pragma warning restore CS0649

	internal struct ReadUsnJournalData
	{
		public long StartUsn;

		public uint ReasonMask;

		public uint ReturnOnlyOnClose;

		public ulong Timeout;

		public ulong BytesToWaitFor;

		public ulong UsnJournalId;
	}

	internal const uint FsctlEnumUsnData = 590003u;

	internal const uint FsctlReadUsnJournal = 590011u;

	internal const uint FsctlQueryUsnJournal = 590068u;

	internal const uint FsctlCreateUsnJournal = 590055u;

	private const uint GenericRead = 2147483648u;

	private const uint FileShareRead = 1u;

	private const uint FileShareWrite = 2u;

	private const uint OpenExisting = 3u;

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	internal static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, nint inputBuffer, int inputBufferSize, byte[] outputBuffer, int outputBufferSize, out int bytesReturned, nint overlapped);

	internal static SafeFileHandle OpenVolume(char driveLetter)
	{
		SafeFileHandle safeFileHandle = CreateFileW($"\\\\.\\{char.ToUpperInvariant(driveLetter)}:", 2147483648u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
		if (safeFileHandle.IsInvalid)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), $"Birim {driveLetter}: açılamadı. Yönetici yetkisi gerekebilir.");
		}
		return safeFileHandle;
	}

	internal static bool Control<T>(SafeFileHandle handle, uint code, in T input, byte[] output, out int bytesReturned) where T : struct
	{
		int num = Marshal.SizeOf<T>();
		nint num2 = Marshal.AllocHGlobal(num);
		try
		{
			Marshal.StructureToPtr(input, num2, fDeleteOld: false);
			return DeviceIoControl(handle, code, num2, num, output, output.Length, out bytesReturned, IntPtr.Zero);
		}
		finally
		{
			Marshal.FreeHGlobal(num2);
		}
	}
}
