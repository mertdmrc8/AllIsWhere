namespace AllIsWhere.Engine;

public sealed record JournalCheckpoint(ulong JournalId, long FirstUsn, long NextUsn);
