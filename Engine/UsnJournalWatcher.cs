using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AllIsWhere.Interop;
using AllIsWhere.Models;
using Microsoft.Win32.SafeHandles;

namespace AllIsWhere.Engine;

/// <summary>Applies new, changed, and deleted USN Journal records in real time.</summary>
public sealed class UsnJournalWatcher
{
    private const int BufferSize = 1024 * 1024;
    private const int UsnV2HeaderSize = 60;
    private readonly char _driveLetter;
    private readonly SearchIndex _index;

    public UsnJournalWatcher(char driveLetter, SearchIndex index)
    {
        _driveLetter = driveLetter;
        _index = index;
    }

    public async Task RunAsync(long startingUsn, Action<string> status, Action changed, CancellationToken token)
    {
        using SafeFileHandle volume = NativeMethods.OpenVolume(_driveLetter);
        var journal = QueryJournal(volume);
        ulong journalId = journal.UsnJournalId;
        long cursor = Math.Max(startingUsn, journal.FirstUsn);
        byte[] buffer = new byte[BufferSize];
        status("USN Journal izleniyor");

        while (!token.IsCancellationRequested)
        {
            journal = QueryJournal(volume);
            if (journal.UsnJournalId != journalId)
                throw new InvalidOperationException("USN Journal yeniden oluşturuldu; indeks yeniden taranmalı.");
            if (cursor < journal.FirstUsn || cursor > journal.NextUsn)
                throw new InvalidOperationException("USN Journal cursor'ı geçersiz; indeks yeniden taranmalı.");

            var read = new NativeMethods.ReadUsnJournalData
            {
                StartUsn = cursor,
                ReasonMask = uint.MaxValue,
                ReturnOnlyOnClose = 0,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalId = journal.UsnJournalId
            };

            bool ok = NativeMethods.Control(volume, NativeMethods.FsctlReadUsnJournal,
                read, buffer, out int bytesReturned);
            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal okunamadı.");
            if (bytesReturned < sizeof(long))
            {
                await Task.Delay(250, token);
                continue;
            }

            cursor = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(0, 8));
            int offset = 8;
            bool batchChanged = false;
            while (offset + UsnV2HeaderSize <= bytesReturned)
            {
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));
                if (length < UsnV2HeaderSize || length > bytesReturned - offset) break;
                ushort major = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 4, 2));
                if (major == 2)
                {
                    ulong fileId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 8, 8));
                    ulong parentId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset + 16, 8));
                    long usn = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(offset + 24, 8));
                    uint reason = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 40, 4));
                    uint attrs = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset + 52, 4));
                    ushort nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 56, 2));
                    ushort nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(offset + 58, 2));
                    if ((nameBytes & 1) == 0 && nameOffset + nameBytes <= length)
                    {
                        string name = System.Text.Encoding.Unicode.GetString(buffer,
                            offset + nameOffset, nameBytes);
                        const uint FileDelete = 0x00000200;
                        const uint RenameOldName = 0x00001000;
                        // Keep the old-name record pending; RenameNewName or a later MFT scan
                        // creates the new name. Remove deleted records from the index.
                        if ((reason & FileDelete) != 0)
                        {
                            _index.Remove(fileId);
                            batchChanged = true;
                        }
                        else if ((reason & RenameOldName) == 0 && name.Length != 0)
                        {
                            _index.Upsert(new IndexedEntry(fileId, parentId, name, attrs, usn));
                            batchChanged = true;
                        }
                    }
                }
                offset += checked((int)length);
            }

            if (batchChanged) changed();
            if (cursor >= journal.NextUsn)
                await Task.Delay(150, token);
        }
    }

    private static NativeMethods.UsnJournalData QueryJournal(SafeFileHandle volume)
    {
        byte[] buffer = new byte[System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.UsnJournalData>()];
        bool ok = NativeMethods.DeviceIoControl(volume, NativeMethods.FsctlQueryUsnJournal,
            IntPtr.Zero, 0, buffer, buffer.Length, out int returned, IntPtr.Zero);
        if (!ok || returned < buffer.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "USN Journal sorgulanamadı.");
        return NtfsVolumeReader.ReadStruct<NativeMethods.UsnJournalData>(buffer);
    }
}

