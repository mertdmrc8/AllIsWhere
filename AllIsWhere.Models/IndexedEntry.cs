namespace AllIsWhere.Models;

public sealed record IndexedEntry(ulong FileId, ulong ParentFileId, string Name, uint Attributes, long LastUsn = 0L)
{
	public bool IsDirectory => (Attributes & 0x10) != 0;

	public bool IsHidden => (Attributes & 2) != 0;
}
