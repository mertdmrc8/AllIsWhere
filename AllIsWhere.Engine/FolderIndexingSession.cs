using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using AllIsWhere.Services;

namespace AllIsWhere.Engine;

public sealed class FolderIndexingSession : IDisposable
{
	private static readonly SemaphoreSlim ScanGate = new SemaphoreSlim(1, 1);

	private readonly string _root;

	private readonly SearchIndex _index;

	private readonly bool _isNetworkPath;

	private readonly object _watcherLock = new object();

    private readonly object _cacheLock = new();
    private Timer? _cacheTimer;
    private bool _cacheReady;
    private bool _cacheDisposed;
    private int _cacheDirty;

	private string _scanPerformance;

	private FileSystemWatcher? _watcher;

	private CancellationTokenSource? _sessionCancellation;

	private CancellationTokenSource? _networkMonitorCancellation;

	private CancellationToken _sessionToken;

	private bool _watching;

	private bool _scanCompleted;

	private bool _networkReachable = true;

	private int _activeScans;

	private int _networkProbeRunning;

	public bool IsWatching => _watching;

	public bool IsScanning => Volatile.Read(in _activeScans) > 0;

	public bool IsNetworkAvailable => _networkReachable;

	public string ScanPerformance
	{
		get
		{
			return Volatile.Read(in _scanPerformance);
		}
		set
		{
			Volatile.Write(ref _scanPerformance, value);
		}
	}

	public event Action<string>? StatusChanged;

	public event Action? IndexChanged;

	public event Action<bool, int>? ScanProgressChanged;

	public FolderIndexingSession(string root, SearchIndex index, string scanPerformance = "Balanced")
	{
		_root = Path.GetFullPath(root);
		_index = index;
		_isNetworkPath = IsNetworkPath(_root);
		_scanPerformance = scanPerformance;
	}

	public async Task StartAsync(CancellationToken token)
	{
		_sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
		token = _sessionCancellation.Token;
		_sessionToken = token;
        bool loaded = await Task.Run(() => FolderIndexCacheStore.TryLoad(_root, _index, token), token);
        token.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (_cacheDisposed) return;
            _cacheReady = loaded;
            _cacheTimer = new Timer(_ => FlushCache(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        }
        if (loaded)
        {
            StatusChanged?.Invoke(LocalizationService.Get("CacheLoaded"));
            IndexChanged?.Invoke();
        }
		if (_isNetworkPath)
		{
			_networkMonitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
			_ = MonitorNetworkAvailabilityAsync(_networkMonitorCancellation.Token);
		}
		await Task.Run(() =>
		{
            StartWatcher();
			Scan(token);
            FlushCache();
		}, token);
		_scanCompleted = true;
		StatusChanged?.Invoke((!_networkReachable) ? LocalizationService.Get("NetworkDisconnected") : LocalizationService.Get(_watching ? "FolderReadyWatching" : "FolderScanComplete"));
		IndexChanged?.Invoke();
	}

	public async Task RefreshAsync(CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		StatusChanged?.Invoke(LocalizationService.Get("FolderWatching"));
		await Task.Run(() =>
		{
			Scan(_root, token);
		}, token);
		_scanCompleted = true;
		StatusChanged?.Invoke((!_networkReachable) ? LocalizationService.Get("NetworkDisconnected") : LocalizationService.Get(_watching ? "FolderReadyWatching" : "FolderScanComplete"));
	}

	private static bool IsNetworkPath(string path)
	{
		if (path.StartsWith("\\\\", StringComparison.Ordinal))
		{
			return true;
		}
		string root = Path.GetPathRoot(path) ?? string.Empty;
		if (root.Length < 2 || root[1] != ':')
		{
			return false;
		}
		try
		{
			DriveInfo? driveInfo = DriveInfo.GetDrives().FirstOrDefault((DriveInfo drive) => drive.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
			return driveInfo != null && driveInfo.DriveType == DriveType.Network;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private async Task MonitorNetworkAvailabilityAsync(CancellationToken token)
	{
		while (!token.IsCancellationRequested)
		{
			bool? flag = await ProbeNetworkRootAsync(token);
			if (flag.HasValue)
			{
				SetNetworkAvailability(flag.Value);
			}
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(5L), token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}
	}

	private async Task<bool?> ProbeNetworkRootAsync(CancellationToken token)
	{
		if (Interlocked.CompareExchange(ref _networkProbeRunning, 1, 0) != 0)
		{
			return null;
		}
		Task<bool> task = Task.Run(() =>
		{
			try
			{
				using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(_root).GetEnumerator();
				enumerator.MoveNext();
				return true;
			}
			catch (Exception ex3) when ((ex3 is IOException || ex3 is UnauthorizedAccessException || ex3 is SecurityException || ex3 is ArgumentException) ? true : false)
			{
				return false;
			}
			finally
			{
				Interlocked.Exchange(ref _networkProbeRunning, 0);
			}
		});
		try
		{
			return await task.WaitAsync(TimeSpan.FromSeconds(4L), token);
		}
		catch (TimeoutException)
		{
			return false;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	private void OnChanged(object sender, FileSystemEventArgs e)
	{
		if (e.ChangeType == WatcherChangeTypes.Deleted)
		{
			_index.RemovePathTree(e.FullPath);
		}
		else if (RefreshPath(e.FullPath) && e.ChangeType == WatcherChangeTypes.Created)
		{
			QueueScan(e.FullPath);
		}
        MarkCacheDirty();
		IndexChanged?.Invoke();
	}

	private void OnRenamed(object sender, RenamedEventArgs e)
	{
		_index.RemovePathTree(e.OldFullPath);
		if (RefreshPath(e.FullPath))
		{
			QueueScan(e.FullPath);
		}
        MarkCacheDirty();
		IndexChanged?.Invoke();
	}

	private bool RefreshPath(string path)
	{
		try
		{
			FileAttributes attributes = File.GetAttributes(path);
			bool flag = (attributes & FileAttributes.Directory) != 0;
			_index.UpsertPath(path, flag, (attributes & FileAttributes.Hidden) != 0);
			return flag;
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is SecurityException) ? true : false)
		{
			_index.RemovePath(path);
			return false;
		}
	}

	private void Scan(CancellationToken token)
	{
		Scan(_root, token);
	}

	private void Scan(string scanRoot, CancellationToken token)
	{
        ScanGate.Wait(token);
        bool fullScan = string.Equals(scanRoot, _root, StringComparison.OrdinalIgnoreCase);
        var unreadable = new List<string>();
		Thread currentThread = Thread.CurrentThread;
		ThreadPriority priority = currentThread.Priority;
		SetThreadPriority(currentThread, ScanPerformance);
		if (Interlocked.Increment(ref _activeScans) == 1)
		{
			ScanProgressChanged?.Invoke(arg1: true, _index.TotalCount);
		}
		Stack<string> stack = new Stack<string>();
		stack.Push(scanRoot);
		int num = 0;
		try
		{
            var previous = fullScan ? _index.PathEntries.ToArray() : null;
			try
			{
				currentThread.Priority = ThreadPriority.BelowNormal;
			}
			catch (ThreadStateException)
			{
			}
			while (stack.Count > 0)
			{
				token.ThrowIfCancellationRequested();
				string text = stack.Pop();
				try
				{
					FileAttributes attributes = File.GetAttributes(text);
					_index.UpsertPath(text, isDirectory: true, (attributes & FileAttributes.Hidden) != 0);
					foreach (string item in Directory.EnumerateFileSystemEntries(text))
					{
						token.ThrowIfCancellationRequested();
						try
						{
							FileAttributes attributes2 = File.GetAttributes(item);
							bool flag = (attributes2 & FileAttributes.Directory) != 0;
							_index.UpsertPath(item, flag, (attributes2 & FileAttributes.Hidden) != 0);
							if (flag && (attributes2 & FileAttributes.ReparsePoint) == 0)
							{
								stack.Push(item);
							}
							string scanPerformance = ScanPerformance;
							int num2 = ((scanPerformance == "LowResource") ? 128 : 256);
							if (++num >= num2)
							{
								num = 0;
								ScanProgressChanged?.Invoke(arg1: true, _index.TotalCount);
								if (scanPerformance == "LowResource")
								{
									Thread.Sleep(8);
								}
								else if (scanPerformance == "Balanced")
								{
									Thread.Sleep(2);
								}
								SetThreadPriority(currentThread, scanPerformance);
							}
						}
						catch (Exception ex2) when ((ex2 is IOException || ex2 is UnauthorizedAccessException || ex2 is ArgumentException || ex2 is SecurityException) ? true : false)
						{
                            unreadable.Add(item);
						}
					}
				}
				catch (Exception ex3) when ((ex3 is IOException || ex3 is UnauthorizedAccessException || ex3 is ArgumentException || ex3 is SecurityException) ? true : false)
				{
                    unreadable.Add(text);
					StatusChanged?.Invoke(LocalizationService.Get("FolderReadWarning", text));
				}
			}
            token.ThrowIfCancellationRequested();
            if (fullScan && !unreadable.Contains(_root, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var oldEntry in previous!)
                {
                    token.ThrowIfCancellationRequested();
                    if (!unreadable.Any(path => IsAtOrBelow(oldEntry.Key, path)))
                        _index.RemoveUnchangedPath(oldEntry);
                }
                lock (_cacheLock)
                    _cacheReady = true;
                MarkCacheDirty();
            }
            else if (!fullScan && unreadable.Count == 0)
                MarkCacheDirty();

		}
		finally
		{
			try
			{
				try
				{
					currentThread.Priority = priority;
				}
				catch (ThreadStateException)
				{
				}
				if (Interlocked.Decrement(ref _activeScans) == 0)
				{
					ScanProgressChanged?.Invoke(arg1: false, _index.TotalCount);
				}
				IndexChanged?.Invoke();
			}
			finally
			{
				ScanGate.Release();
			}
		}
	}

    private static bool IsAtOrBelow(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);

	private static void SetThreadPriority(Thread thread, string performance)
	{
		ThreadPriority threadPriority = ((performance == "Fast") ? ThreadPriority.Normal : ((!(performance == "LowResource")) ? ThreadPriority.BelowNormal : ThreadPriority.Lowest));
		ThreadPriority priority = threadPriority;
		try
		{
			thread.Priority = priority;
		}
		catch (ThreadStateException)
		{
		}
	}

	private void OnError(object sender, ErrorEventArgs e)
	{
		Exception exception = e.GetException();
		if (_isNetworkPath && !(exception is InternalBufferOverflowException))
		{
			SetNetworkAvailability(reachable: false);
			return;
		}
		StatusChanged?.Invoke(LocalizationService.Get("WatcherOverflow"));
		QueueScan(_root);
	}

	private void QueueScan(string scanRoot)
	{
		CancellationToken token = _sessionToken;
		if (!token.IsCancellationRequested)
		{
			Task.Run(() =>
			{
				Scan(scanRoot, token);
			}, token);
		}
	}

	private void StartWatcher()
	{
		lock (_watcherLock)
		{
			if (_watcher != null || _sessionToken.IsCancellationRequested)
			{
				return;
			}
			FileSystemWatcher? fileSystemWatcher = null;
			try
			{
				fileSystemWatcher = new FileSystemWatcher(_root)
				{
					IncludeSubdirectories = true,
					NotifyFilter = (NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite),
					InternalBufferSize = 16384
				};
				fileSystemWatcher.Created += OnChanged;
				fileSystemWatcher.Changed += OnChanged;
				fileSystemWatcher.Deleted += OnChanged;
				fileSystemWatcher.Renamed += OnRenamed;
				fileSystemWatcher.Error += OnError;
				fileSystemWatcher.EnableRaisingEvents = true;
				_watcher = fileSystemWatcher;
				_watching = true;
				StatusChanged?.Invoke(LocalizationService.Get("FolderWatching"));
			}
			catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is SecurityException) ? true : false)
			{
				fileSystemWatcher?.Dispose();
				_watcher = null;
				_watching = false;
				StatusChanged?.Invoke(LocalizationService.Get("FolderWatchUnavailable", ex.Message));
			}
		}
	}

	private void StopWatcher()
	{
		lock (_watcherLock)
		{
			if (_watcher != null)
			{
				_watcher.EnableRaisingEvents = false;
				_watcher.Dispose();
				_watcher = null;
				_watching = false;
			}
		}
	}

	private void SetNetworkAvailability(bool reachable)
	{
		if (_networkReachable != reachable)
		{
			_networkReachable = reachable;
			if (reachable)
			{
				StartWatcher();
				QueueScan(_root);
			}
			else
			{
				StopWatcher();
			}
			string obj = ((!reachable) ? LocalizationService.Get("NetworkDisconnected") : LocalizationService.Get((!_scanCompleted) ? (_watching ? "FolderWatching" : "Preparing") : (_watching ? "FolderReadyWatching" : "FolderScanComplete")));
			StatusChanged?.Invoke(obj);
		}
	}

    private void MarkCacheDirty() => Interlocked.Exchange(ref _cacheDirty, 1);

    private void FlushCache()
    {
        lock (_cacheLock)
        {
            if (_cacheDisposed || !_cacheReady || IsScanning ||
                Interlocked.Exchange(ref _cacheDirty, 0) == 0)
                return;
            try
            {
                FolderIndexCacheStore.Save(_root, _index);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MarkCacheDirty();
            }
        }
    }

    public void Dispose() => Dispose(deleteCache: false);

    internal void Dispose(bool deleteCache)
    {
        _sessionCancellation?.Cancel();
        _networkMonitorCancellation?.Cancel();
        _networkMonitorCancellation?.Dispose();
        _networkMonitorCancellation = null;
        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        StopWatcher();
        lock (_cacheLock)
        {
            if (_cacheDisposed) return;
            _cacheTimer?.Dispose();
            if (!deleteCache)
                FlushCache();
            _cacheDisposed = true;
            if (deleteCache)
                FolderIndexCacheStore.Delete(_root);
        }
    }
}
