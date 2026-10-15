using System.IO;
using AllIsWhere.Services;

namespace AllIsWhere.Engine;

/// <summary>
/// Indexes a folder on a local disk, mapped drive, or UNC share.
/// Scanning uses the Directory API; FileSystemWatcher reports live changes.
/// </summary>
public sealed class FolderIndexingSession : IDisposable
{
    // Serialize background scans to avoid loading multiple drives at once.
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    private readonly string _root;
    private readonly SearchIndex _index;
    private string _scanPerformance;
    private FileSystemWatcher? _watcher;
    private bool _watching;
    private int _activeScans;

    public FolderIndexingSession(string root, SearchIndex index, string scanPerformance = "Balanced")
    {
        _root = Path.GetFullPath(root);
        _index = index;
        _scanPerformance = scanPerformance;
    }

    public event Action<string>? StatusChanged;
    public event Action? IndexChanged;
    public event Action<bool, int>? ScanProgressChanged;
    public bool IsWatching => _watching;
    public bool IsScanning => Volatile.Read(ref _activeScans) > 0;
    public string ScanPerformance
    {
        get => Volatile.Read(ref _scanPerformance);
        set => Volatile.Write(ref _scanPerformance, value);
    }

    public async Task StartAsync(CancellationToken token)
    {
        // Start the watcher first so changes during the initial scan are not lost.
        try
        {
            _watcher = new FileSystemWatcher(_root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            _watcher.Created += OnChanged;
            _watcher.Changed += OnChanged;
            _watcher.Deleted += OnChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += OnError;
            _watcher.EnableRaisingEvents = true;
            _watching = true;
            StatusChanged?.Invoke(LocalizationService.Get("FolderWatching"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                   System.Security.SecurityException)
        {
            _watcher?.Dispose();
            _watcher = null;
            StatusChanged?.Invoke(LocalizationService.Get("FolderWatchUnavailable", ex.Message));
        }

        await Task.Run(() => Scan(token), token);
        StatusChanged?.Invoke(LocalizationService.Get(_watching ? "FolderReadyWatching" : "FolderScanComplete"));
        IndexChanged?.Invoke();
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (e.ChangeType == WatcherChangeTypes.Deleted)
            _index.RemovePathTree(e.FullPath);
        else
        {
            bool isDirectory = RefreshPath(e.FullPath);
            if (isDirectory && e.ChangeType == WatcherChangeTypes.Created)
                _ = Task.Run(() => Scan(e.FullPath, CancellationToken.None));
        }
        IndexChanged?.Invoke();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        _index.RemovePathTree(e.OldFullPath);
        if (RefreshPath(e.FullPath)) _ = Task.Run(() => Scan(e.FullPath, CancellationToken.None));
        IndexChanged?.Invoke();
    }

    private bool RefreshPath(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
                    _index.UpsertPath(path, isDirectory, (attributes & FileAttributes.Hidden) != 0);
            return isDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                   System.Security.SecurityException)
        {
            _index.RemovePath(path);
            return false;
        }
    }

    private void Scan(CancellationToken token) => Scan(_root, token);

    private void Scan(string scanRoot, CancellationToken token)
    {
        ScanGate.Wait(token);
        var scanThread = Thread.CurrentThread;
        var previousPriority = scanThread.Priority;
        SetThreadPriority(scanThread, ScanPerformance);
        if (Interlocked.Increment(ref _activeScans) == 1)
            ScanProgressChanged?.Invoke(true, _index.TotalCount);
        var pending = new Stack<string>();
        pending.Push(scanRoot);
        int sinceProgress = 0;
        try
        {
            try { scanThread.Priority = ThreadPriority.BelowNormal; }
            catch (ThreadStateException) { }
            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                try
                {
                    var rootAttributes = File.GetAttributes(directory);
                    _index.UpsertPath(directory, isDirectory: true,
                        isHidden: (rootAttributes & FileAttributes.Hidden) != 0);
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var attributes = File.GetAttributes(path);
                            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            _index.UpsertPath(path, isDirectory, (attributes & FileAttributes.Hidden) != 0);
                            if (isDirectory && (attributes & FileAttributes.ReparsePoint) == 0)
                                pending.Push(path);
                            string performance = ScanPerformance;
                            int batchSize = performance == "LowResource" ? 128 : 256;
                            if (++sinceProgress >= batchSize)
                            {
                                sinceProgress = 0;
                                ScanProgressChanged?.Invoke(true, _index.TotalCount);
                                if (performance == "LowResource") Thread.Sleep(8);
                                else if (performance == "Balanced") Thread.Sleep(2);
                                SetThreadPriority(scanThread, performance);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                                   ArgumentException or System.Security.SecurityException)
                        {
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           ArgumentException or System.Security.SecurityException)
                {
                    StatusChanged?.Invoke(LocalizationService.Get("FolderReadWarning", directory));
                }
            }
        }
        finally
        {
            try
            {
                try { scanThread.Priority = previousPriority; }
                catch (ThreadStateException) { }
                if (Interlocked.Decrement(ref _activeScans) == 0)
                    ScanProgressChanged?.Invoke(false, _index.TotalCount);
                IndexChanged?.Invoke();
            }
            finally { ScanGate.Release(); }
        }
    }

    private static void SetThreadPriority(Thread thread, string performance)
    {
        ThreadPriority priority = performance switch
        {
            "Fast" => ThreadPriority.Normal,
            "LowResource" => ThreadPriority.Lowest,
            _ => ThreadPriority.BelowNormal
        };
        try { thread.Priority = priority; }
        catch (ThreadStateException) { }
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        StatusChanged?.Invoke(LocalizationService.Get("WatcherOverflow"));
        _ = Task.Run(() =>
        {
            _index.RemovePathTree(_root);
            Scan(_root, CancellationToken.None);
        });
    }

    public void Dispose()
    {
        if (_watcher is null) return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }
}
