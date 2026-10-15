namespace AllIsWhere.Models;

/// <summary>Lightweight index record for a file or folder on a volume.</summary>
public sealed record IndexedEntry(
    ulong FileId,
    ulong ParentFileId,
    string Name,
    uint Attributes,
    long LastUsn = 0)
{
    public bool IsDirectory => (Attributes & 0x10) != 0;
    public bool IsHidden => (Attributes & 0x2) != 0;
}
