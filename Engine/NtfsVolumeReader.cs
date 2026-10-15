using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AllIsWhere.Interop;
using AllIsWhere.Models;
using Microsoft.Win32.SafeHandles;

namespace AllIsWhere.Engine;

/// <summary>Reads NTFS MFT records in pages by using FSCTL_ENUM_USN_DATA.</summary>
public sealed class NtfsVolumeReader
{
    private const int BufferSize = 1024 * 1024;
    private const int UsnV2HeaderSize = 60;
    private readonly char _driveLetter;

    public NtfsVolumeReader(char driveLetter) => _driveLetter = char.ToUpperInvariant(driveLetter);

    /// <summary>
    /// Capture the starting USN before scanning the MFT. After the scan, the watcher
    /// resumes from this position and applies changes made during the scan.
    /// </summary>
    public long GetCurrentUsn()
    {
        using SafeFileHandle volume = NativeMethods.OpenVolume(_driveLetter);
        byte[] buffer = new byte[MarshalSize<NativeMethods.UsnJournalData>()];
        if (!NativeMethods.DeviceIoControl(volume, NativeMethods.FsctlQueryUsnJournal,
                IntPtr.Zero, 0, buffer, buffer.Length, out int returned, IntPtr.Zero) ||
            returned < buffer.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal bilgisi okunamadı.");
        return ReadStruct<NativeMethods.UsnJournalData>(buffer).NextUsn;
    }

    public async IAsyncEnumerable<IndexedEntry> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        Func<string>? performanceProvider = null)
    {
        using SafeFileHandle volume = NativeMethods.OpenVolume(_driveLetter);
        byte[] queryBuffer = new byte[MarshalSize<NativeMethods.UsnJournalData>()];
        if (!NativeMethods.DeviceIoControl(volume, NativeMethods.FsctlQueryUsnJournal,
                IntPtr.Zero, 0, queryBuffer, queryBuffer.Length, out int queryBytes, IntPtr.Zero) ||
            queryBytes < queryBuffer.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal bilgisi okunamadı.");
        var query = ReadStruct<NativeMethods.UsnJournalData>(queryBuffer);

        var cursor = new NativeMethods.MftEnumData
        {
            StartFileReferenceNumber = 0,
            LowUsn = 0,
            HighUsn = query.NextUsn
        };
        var buffer = new byte[BufferSize];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool ok = NativeMethods.Control(volume, NativeMethods.FsctlEnumUsnData,
                cursor, buffer, out int bytesReturned);
            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 38) yield break; // ERROR_HANDLE_EOF
                throw new Win32Exception(error, "MFT kayıtları okunamadı.");
            }

            // The first DWORDLONG in the output is the next page's MFT cursor.
            if (bytesReturned < sizeof(ulong)) yield break;
            ulong nextCursor = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(0, 8));
            int offset = 8;

            while (offset + UsnV2HeaderSize <= bytesReturned)
            {
                uint recordLength = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
                if (recordLength < UsnV2HeaderSize || recordLength > bytesReturned - offset) break;

                ushort major = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 4, 2));
                if (major == 2)
                {
                    ulong fileId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 8, 8));
                    ulong parentId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 16, 8));
                    uint attrs = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 52, 4));
                    ushort nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 56, 2));
                    ushort nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 58, 2));
                    if ((nameBytes & 1) == 0 && nameOffset + nameBytes <= recordLength)
                    {
                        string name = System.Text.Encoding.Unicode.GetString(
                            buffer, offset + nameOffset, nameBytes);
                        yield return new IndexedEntry(fileId, parentId, name, attrs);
                    }
                }
                offset += checked((int)recordLength);
            }

            if (nextCursor == cursor.StartFileReferenceNumber) yield break;
            cursor.StartFileReferenceNumber = nextCursor;
            // Adjust the scan speed according to the performance setting.
            switch (performanceProvider?.Invoke() ?? "Balanced")
            {
                case "Fast": await Task.Yield(); break;
                case "LowResource": await Task.Delay(8, cancellationToken); break;
                default: await Task.Delay(2, cancellationToken); break;
            }
        }
    }

    internal static T ReadStruct<T>(byte[] bytes) where T : struct
    {
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(bytes,
            System.Runtime.InteropServices.GCHandleType.Pinned);
        try { return System.Runtime.InteropServices.Marshal.PtrToStructure<T>(pin.AddrOfPinnedObject()); }
        finally { pin.Free(); }
    }

    private static int MarshalSize<T>() where T : struct =>
        System.Runtime.InteropServices.Marshal.SizeOf<T>();
}
