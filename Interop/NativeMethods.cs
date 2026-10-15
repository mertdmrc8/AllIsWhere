using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AllIsWhere.Interop;

internal static class NativeMethods
{
    internal const uint FsctlEnumUsnData = 0x000900B3;
    internal const uint FsctlReadUsnJournal = 0x000900BB;
    internal const uint FsctlQueryUsnJournal = 0x000900F4;
    internal const uint FsctlCreateUsnJournal = 0x000900E7;

    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeviceIoControl(
        SafeFileHandle device, uint controlCode,
        IntPtr inputBuffer, int inputBufferSize,
        byte[] outputBuffer, int outputBufferSize,
        out int bytesReturned, IntPtr overlapped);

    internal static SafeFileHandle OpenVolume(char driveLetter)
    {
        var handle = CreateFileW($@"\\.\{char.ToUpperInvariant(driveLetter)}:",
            GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero,
            OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Birim {driveLetter}: açılamadı. Yönetici yetkisi gerekebilir.");
        return handle;
    }

    internal static bool Control<T>(SafeFileHandle handle, uint code, in T input,
        byte[] output, out int bytesReturned) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        IntPtr memory = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(input, memory, false);
            return DeviceIoControl(handle, code, memory, size, output, output.Length,
                out bytesReturned, IntPtr.Zero);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MftEnumData
    {
        public ulong StartFileReferenceNumber;
        public long LowUsn;
        public long HighUsn;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    internal struct ReadUsnJournalData
    {
        public long StartUsn;
        public uint ReasonMask;
        public uint ReturnOnlyOnClose;
        public ulong Timeout;
        public ulong BytesToWaitFor;
        public ulong UsnJournalId;
    }
}
