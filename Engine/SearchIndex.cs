using System.Collections.Concurrent;
using System.IO;
using AllIsWhere.Models;

namespace AllIsWhere.Engine;

/// <summary>
/// Lightweight File ID index. Name searches run in parallel; paths are assembled
/// by walking parent records only for matching results.
/// </summary>
public sealed class SearchIndex
{
    private readonly ConcurrentDictionary<ulong, IndexedEntry> _entries = new();
    private readonly ConcurrentDictionary<string, PathEntry> _paths =
        new(StringComparer.OrdinalIgnoreCase);
    private const int MaxPathDepth = 256;

    public int Count => _entries.Count;
    public int TotalCount => _entries.Count + _paths.Count;
    public void Upsert(IndexedEntry entry) => _entries[entry.FileId] = entry;
    public void Remove(ulong fileId) => _entries.TryRemove(fileId, out _);
    public void UpsertPath(string path, bool isDirectory, bool isHidden = false)
    {
        string fullPath = Path.GetFullPath(path);
        _paths[fullPath] = new PathEntry(Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)),
            fullPath, isDirectory, isHidden);
    }
    public void RemovePath(string path) => _paths.TryRemove(Path.GetFullPath(path), out _);
    public void RemovePathTree(string path)
    {
        string full = Path.GetFullPath(path);
        string prefix = full.EndsWith(Path.DirectorySeparatorChar) || full.EndsWith(Path.AltDirectorySeparatorChar)
            ? full : full + Path.DirectorySeparatorChar;
        foreach (string candidate in _paths.Keys)
            if (candidate.Equals(full, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                _paths.TryRemove(candidate, out _);
    }
    public IEnumerable<IndexedEntry> Snapshot() => _entries.Values;

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit,
        CancellationToken cancellationToken, string? volumeRoot = null, bool? directoriesOnly = null,
        IReadOnlySet<string>? extensions = null, bool showHiddenFolders = false) => Task.Run<IReadOnlyList<SearchResult>>(() =>
    {
        string needle = query.Trim();
        if (needle.Length == 0) return Array.Empty<SearchResult>();

        var matches = new ConcurrentBag<SearchResult>();
        int accepted = 0;
        int maxResults = Math.Max(1, limit);
        // ConcurrentDictionary enumerator is safe during updates and avoids
        // allocating a full Values snapshot for every keystroke.
        Parallel.ForEach(_entries,
            new ParallelOptions { CancellationToken = cancellationToken }, pair =>
            {
                var entry = pair.Value;
                if (Volatile.Read(ref accepted) < maxResults &&
                    MatchesFilters(entry.IsDirectory, entry.IsHidden, entry.Name, directoriesOnly, extensions, showHiddenFolders) &&
                    entry.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) &&
                    Interlocked.Increment(ref accepted) <= maxResults)
                    matches.Add(new SearchResult(entry.Name,
                        Path.Combine(volumeRoot ?? string.Empty, GetPath(entry)), entry.IsDirectory));
            });

        Parallel.ForEach(_paths,
            new ParallelOptions { CancellationToken = cancellationToken }, pair =>
            {
                var entry = pair.Value;
                if (Volatile.Read(ref accepted) < maxResults &&
                    MatchesFilters(entry.IsDirectory, entry.IsHidden, entry.Name, directoriesOnly, extensions, showHiddenFolders) &&
                    (entry.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                     entry.FullPath.Contains(needle, StringComparison.OrdinalIgnoreCase)) &&
                    Interlocked.Increment(ref accepted) <= maxResults)
                    matches.Add(new SearchResult(entry.Name, entry.FullPath, entry.IsDirectory));
            });

        return matches.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Take(limit).ToArray();
    }, cancellationToken);

    private static bool MatchesFilters(bool isDirectory, bool isHidden, string name, bool? directoriesOnly,
        IReadOnlySet<string>? extensions, bool showHiddenFolders)
    {
        if (isDirectory && isHidden && !showHiddenFolders) return false;
        if (directoriesOnly.HasValue && directoriesOnly.Value != isDirectory) return false;
        if (extensions is null || extensions.Count == 0) return true;
        return !isDirectory && extensions.Contains(Path.GetExtension(name));
    }

    private string GetPath(IndexedEntry entry)
    {
        var parts = new List<string>(12) { entry.Name };
        ulong parentId = entry.ParentFileId;
        for (int depth = 0; depth < MaxPathDepth && parentId != 0; depth++)
        {
            if (!_entries.TryGetValue(parentId, out var parent)) break;
            parts.Add(parent.Name);
            if (parent.ParentFileId == parent.FileId) break; // A volume root is its own parent.
            parentId = parent.ParentFileId;
        }
        parts.Reverse();
        return string.Join("\\", parts);
    }

    private sealed record PathEntry(string Name, string FullPath, bool IsDirectory, bool IsHidden);
}

public sealed record SearchResult(string Name, string Path, bool IsDirectory);
