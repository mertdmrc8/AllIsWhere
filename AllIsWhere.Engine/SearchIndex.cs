using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AllIsWhere.Models;

namespace AllIsWhere.Engine;

public sealed class SearchIndex
{
	internal sealed class PathEntry(string name, string fullPath, bool isDirectory, bool isHidden)
	{
		public string Name { get; } = name;
		public string FullPath { get; } = fullPath;
		public bool IsDirectory { get; } = isDirectory;
		public bool IsHidden { get; } = isHidden;
	}

	private readonly record struct SearchKey(string Name, string Path)
	{
		public static SearchKey From(SearchResult result)
		{
			return new SearchKey(result.Name, result.Path);
		}
	}

	private sealed class SearchKeyComparer : IComparer<SearchKey>
	{
		public static SearchKeyComparer Instance { get; } = new SearchKeyComparer();

		public int Compare(SearchKey left, SearchKey right)
		{
			int num = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
			if (num != 0)
			{
				return num;
			}
			num = StringComparer.OrdinalIgnoreCase.Compare(left.Path, right.Path);
			if (num != 0)
			{
				return num;
			}
			num = StringComparer.Ordinal.Compare(left.Name, right.Name);
			if (num == 0)
			{
				return StringComparer.Ordinal.Compare(left.Path, right.Path);
			}
			return num;
		}
	}

	private sealed class WorstFirstComparer : IComparer<SearchKey>
	{
		public static WorstFirstComparer Instance { get; } = new WorstFirstComparer();

		public int Compare(SearchKey left, SearchKey right)
		{
			return SearchKeyComparer.Instance.Compare(right, left);
		}
	}

	private readonly ConcurrentDictionary<ulong, IndexedEntry> _entries = new ConcurrentDictionary<ulong, IndexedEntry>();

	private readonly ConcurrentDictionary<string, PathEntry> _paths = new ConcurrentDictionary<string, PathEntry>(StringComparer.OrdinalIgnoreCase);

	internal IEnumerable<KeyValuePair<string, PathEntry>> PathEntries => _paths;

	internal void RemoveUnchangedPath(KeyValuePair<string, PathEntry> entry) =>
		_paths.TryRemove(entry);

	private const int MaxPathDepth = 256;

	public int Count => _entries.Count;

	public int TotalCount => _entries.Count + _paths.Count;

	public IEnumerable<IndexedEntry> Snapshot()
	{
		return _entries.Values;
	}

	public void Clear()
	{
		_entries.Clear();
		_paths.Clear();
	}

	public void Upsert(IndexedEntry entry)
	{
		_entries[entry.FileId] = entry;
	}

	public void Remove(ulong fileId)
	{
		_entries.TryRemove(fileId, out _);
	}

	public void UpsertPath(string path, bool isDirectory, bool isHidden = false)
	{
		string fullPath = Path.GetFullPath(path);
		_paths[fullPath] = new PathEntry(Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)), fullPath, isDirectory, isHidden);
	}

	public void RemovePath(string path)
	{
		_paths.TryRemove(Path.GetFullPath(path), out _);
	}

	public void RemovePathTree(string path)
	{
		string fullPath = Path.GetFullPath(path);
		string value = ((fullPath.EndsWith(Path.DirectorySeparatorChar) || fullPath.EndsWith(Path.AltDirectorySeparatorChar)) ? fullPath : (fullPath + Path.DirectorySeparatorChar));
		foreach (string key in _paths.Keys)
		{
			if (key.Equals(fullPath, StringComparison.OrdinalIgnoreCase) || key.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				_paths.TryRemove(key, out _);
			}
		}
	}

	public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit, CancellationToken cancellationToken, string? volumeRoot = null, bool? directoriesOnly = null, IReadOnlySet<string>? extensions = null, bool showHiddenFolders = false, SearchQueryTerms? parsedQuery = null, DateTime? modifiedAfterUtc = null, long minimumSizeBytes = 0L, long maximumSizeBytes = long.MaxValue, IReadOnlyList<string>? excludedFolders = null)
	{
		return Task.Run((Func<IReadOnlyList<SearchResult>>)(() =>
		{
			SearchQueryTerms terms = parsedQuery ?? SearchQueryTerms.Parse(query);
			IReadOnlySet<string>? readOnlySet2;
			if (terms.Extensions.Count != 0)
			{
				if (extensions != null && extensions.Count != 0)
				{
					IReadOnlySet<string> readOnlySet = terms.Extensions.Intersect(extensions, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
					readOnlySet2 = readOnlySet;
				}
				else
				{
					readOnlySet2 = terms.Extensions;
				}
			}
			else
			{
				readOnlySet2 = extensions;
			}
			IReadOnlySet<string>? effectiveExtensions = readOnlySet2;
			if (terms.NameTerms.Count == 0 && terms.PathTerms.Count == 0 && (effectiveExtensions == null || effectiveExtensions.Count == 0))
			{
				return Array.Empty<SearchResult>();
			}
			int maxResults = Math.Max(1, limit);
			ParallelOptions parallelOptions = new ParallelOptions
			{
				CancellationToken = cancellationToken,
				MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4)
			};
			PriorityQueue<SearchResult, SearchKey> matches = new PriorityQueue<SearchResult, SearchKey>(maxResults, WorstFirstComparer.Instance);
			object mergeLock = new object();
			Parallel.ForEach(_entries, parallelOptions, () => new PriorityQueue<SearchResult, SearchKey>(WorstFirstComparer.Instance), (KeyValuePair<ulong, IndexedEntry> pair, ParallelLoopState _, PriorityQueue<SearchResult, SearchKey> local) =>
			{
				IndexedEntry value = pair.Value;
				if (MatchesFilters(value.IsDirectory, value.IsHidden, value.Name, directoriesOnly, effectiveExtensions, showHiddenFolders) && MatchesName(value.Name, terms.NameTerms) && CouldEnterByName(local, value.Name, maxResults))
				{
					string path = Path.Combine(volumeRoot ?? string.Empty, GetPath(value));
					if (!MatchesPath(path, terms.PathTerms))
					{
						return local;
					}
					if (IsExcluded(path, excludedFolders) || !MatchesMetadata(path, value.IsDirectory, modifiedAfterUtc, minimumSizeBytes, maximumSizeBytes))
					{
						return local;
					}
					AddBounded(local, new SearchResult(value.Name, path, value.IsDirectory), maxResults);
				}
				return local;
			}, (PriorityQueue<SearchResult, SearchKey> local) =>
			{
				MergeBounded(matches, local, maxResults, mergeLock);
			});
			Parallel.ForEach(_paths, parallelOptions, () => new PriorityQueue<SearchResult, SearchKey>(WorstFirstComparer.Instance), (KeyValuePair<string, PathEntry> pair, ParallelLoopState _, PriorityQueue<SearchResult, SearchKey> local) =>
			{
				PathEntry value = pair.Value;
				if (MatchesFilters(value.IsDirectory, value.IsHidden, value.Name, directoriesOnly, effectiveExtensions, showHiddenFolders) && MatchesName(value.Name, terms.NameTerms) && CouldEnterByName(local, value.Name, maxResults) && MatchesPath(value.FullPath, terms.PathTerms) && !IsExcluded(value.FullPath, excludedFolders) && MatchesMetadata(value.FullPath, value.IsDirectory, modifiedAfterUtc, minimumSizeBytes, maximumSizeBytes))
				{
					AddBounded(local, new SearchResult(value.Name, value.FullPath, value.IsDirectory), maxResults);
				}
				return local;
			}, (PriorityQueue<SearchResult, SearchKey> local) =>
			{
				MergeBounded(matches, local, maxResults, mergeLock);
			});
			return matches.UnorderedItems.Select(((SearchResult Element, SearchKey Priority) item) => item.Element).OrderBy((SearchResult result) => result.Name, StringComparer.OrdinalIgnoreCase).ThenBy((SearchResult result) => result.Path, StringComparer.OrdinalIgnoreCase)
				.ThenBy((SearchResult result) => result.Name, StringComparer.Ordinal)
				.ThenBy((SearchResult result) => result.Path, StringComparer.Ordinal)
				.DistinctBy((SearchResult result) => result.Path, StringComparer.OrdinalIgnoreCase)
				.Take(maxResults)
				.ToArray();
		}), cancellationToken);
	}

	private static bool IsExcluded(string path, IReadOnlyList<string>? excludedFolders)
	{
		if (excludedFolders == null || excludedFolders.Count == 0)
		{
			return false;
		}
		foreach (string excludedFolder in excludedFolders)
		{
			if (path.Equals(excludedFolder, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			string value = (Path.EndsInDirectorySeparator(excludedFolder) ? excludedFolder : (excludedFolder + Path.DirectorySeparatorChar));
			if (path.StartsWith(value, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private static bool MatchesMetadata(string path, bool isDirectory, DateTime? modifiedAfterUtc, long minimumSizeBytes, long maximumSizeBytes)
	{
		if (!modifiedAfterUtc.HasValue && minimumSizeBytes <= 0 && maximumSizeBytes == long.MaxValue)
		{
			return true;
		}
		try
		{
			DateTime dateTime = (isDirectory ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path));
			if (modifiedAfterUtc.HasValue)
			{
				DateTime valueOrDefault = modifiedAfterUtc.GetValueOrDefault();
				if (dateTime < valueOrDefault)
				{
					return false;
				}
			}
			if (minimumSizeBytes <= 0 && maximumSizeBytes == long.MaxValue)
			{
				return true;
			}
			if (isDirectory)
			{
				return false;
			}
			long length = new FileInfo(path).Length;
			return length >= minimumSizeBytes && length <= maximumSizeBytes;
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException) ? true : false)
		{
			return false;
		}
	}

	private static bool MatchesName(string name, IReadOnlyList<string> terms)
	{
		return terms.All((string term) => name.Contains(term, StringComparison.OrdinalIgnoreCase));
	}

	private static bool MatchesPath(string path, IReadOnlyList<string> terms)
	{
		return terms.All((string term) => path.Contains(term, StringComparison.OrdinalIgnoreCase));
	}

	private static void AddBounded(PriorityQueue<SearchResult, SearchKey> queue, SearchResult result, int limit)
	{
		SearchKey searchKey = SearchKey.From(result);
		if (queue.Count < limit)
		{
			queue.Enqueue(result, searchKey);
			return;
		}
		queue.TryPeek(out _, out SearchKey priority);
		if (SearchKeyComparer.Instance.Compare(searchKey, priority) < 0)
		{
			queue.Dequeue();
			queue.Enqueue(result, searchKey);
		}
	}

	private static bool CouldEnterByName(PriorityQueue<SearchResult, SearchKey> queue, string name, int limit)
	{
		if (queue.Count < limit)
		{
			return true;
		}
		queue.TryPeek(out _, out SearchKey priority);
		return StringComparer.OrdinalIgnoreCase.Compare(name, priority.Name) <= 0;
	}

	private static void MergeBounded(PriorityQueue<SearchResult, SearchKey> destination, PriorityQueue<SearchResult, SearchKey> source, int limit, object mergeLock)
	{
		lock (mergeLock)
		{
			foreach (var unorderedItem in source.UnorderedItems)
			{
				AddBounded(destination, unorderedItem.Element, limit);
			}
		}
	}

	private static bool MatchesFilters(bool isDirectory, bool isHidden, string name, bool? directoriesOnly, IReadOnlySet<string>? extensions, bool showHiddenFolders)
	{
		if ((isDirectory & isHidden) && !showHiddenFolders)
		{
			return false;
		}
		if (directoriesOnly.HasValue && directoriesOnly.Value != isDirectory)
		{
			return false;
		}
		if (extensions == null || extensions.Count == 0)
		{
			return true;
		}
		if (!isDirectory)
		{
			return extensions.Contains(Path.GetExtension(name));
		}
		return false;
	}

	private string GetPath(IndexedEntry entry)
	{
		List<string> list = new List<string>(12) { entry.Name };
		ulong parentFileId = entry.ParentFileId;
		for (int i = 0; i < 256; i++)
		{
			if (parentFileId == 0L)
			{
				break;
			}
			if (!_entries.TryGetValue(parentFileId, out var value))
			{
				break;
			}
			list.Add(value.Name);
			if (value.ParentFileId == value.FileId)
			{
				break;
			}
			parentFileId = value.ParentFileId;
		}
		list.Reverse();
		return string.Join("\\", list);
	}
}
