using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AllIsWhere.Engine;
using AllIsWhere.Models;
using AllIsWhere.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace AllIsWhere;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<SourceRow> _sourceRows = new();
    private readonly ObservableCollection<SourceFilterOption> _sourceFilterOptions = new();
    private readonly Dictionary<string, SearchSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<ResultRow> _results = new();
    private CancellationTokenSource? _searchCts;
    private UserSettings _settings = new();
    private Forms.NotifyIcon? _trayIcon;
    private bool _allowClose;
    private bool _suppressDriveSelection;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        _settings.Normalize();
        LocalizationService.SetLanguage(_settings.Language);
        ApplyLocalization();
        ApplyTheme();
        InitializeTrayIcon();
        try { StartupRegistration.Apply(_settings.StartWithWindows); }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("StartupApplyFailed", ex.Message); }
        ResultsList.ItemsSource = _results;
        SourcesList.ItemsSource = _sourceRows;
        SourceFilterBox.ItemsSource = _sourceFilterOptions;
        SourceFilterBox.DisplayMemberPath = nameof(SourceFilterOption.Label);
        SourceFilterBox.SelectedValuePath = nameof(SourceFilterOption.Root);
        _sourceFilterOptions.Add(new SourceFilterOption(LocalizationService.Get("AllSources"), null));
        SourceFilterBox.SelectedIndex = 0;
        HiddenFoldersCheckBox.IsChecked = _settings.ShowHiddenFolders;
        HiddenFoldersCheckBox.Checked += HiddenFolders_Changed;
        HiddenFoldersCheckBox.Unchecked += HiddenFolders_Changed;

        LoadDriveOptions(autoStartDefaultDrive: true);
        Loaded += MainWindow_Loaded;
    }

    private void LoadDriveOptions(bool autoStartDefaultDrive)
    {
        var drives = DriveInfo.GetDrives()
            .Where(drive => drive.Name.Length >= 3 && drive.Name[1] == ':')
            .Select(CreateDriveOption)
            .ToList();

        foreach (var (letter, remotePath) in NetworkDriveDiscovery.GetMappings())
        {
            int existingIndex = drives.FindIndex(drive => drive.Letter == letter);
            var mappedDrive = new DriveOption(letter, LocalizationService.Get("NetworkDriveLabel", letter, remotePath),
                DriveType.Network, existingIndex >= 0 && drives[existingIndex].IsReady,
                IsNtfs: false, SourceRoot: remotePath);
            if (existingIndex >= 0) drives[existingIndex] = mappedDrive;
            else drives.Add(mappedDrive);
        }

        char? priorSelection = DriveSelector.SelectedValue is char selected ? selected : null;
        var defaultDrive = drives.FirstOrDefault(drive => drive.DriveType == DriveType.Fixed && drive.IsNtfs);
        _suppressDriveSelection = !autoStartDefaultDrive || _settings.SearchRoots.Count > 0;
        DriveSelector.ItemsSource = drives;
        DriveSelector.DisplayMemberPath = nameof(DriveOption.Label);
        DriveSelector.SelectedValuePath = nameof(DriveOption.Letter);
        DriveSelector.SelectedValue = drives.Any(drive => drive.Letter == priorSelection)
            ? priorSelection
            : defaultDrive?.Letter;
        _suppressDriveSelection = false;

        if (DriveSelector.SelectedValue is null)
            StatusText.Text = LocalizationService.Get("NoSource");
    }

    private void ApplyLocalization()
    {
        Title = LocalizationService.Get("AppName");
        HeaderTitle.Text = LocalizationService.Get("AppName");
        TaglineText.Text = LocalizationService.Get("Tagline");
        AddFolderButton.Content = LocalizationService.Get("AddFolder");
        SettingsButton.Content = LocalizationService.Get("Settings");
        SourcesLabel.Text = LocalizationService.Get("Sources");
        RemoveSourceButton.Content = LocalizationService.Get("RemoveSelected");
        SearchBox.ToolTip = LocalizationService.Get("SearchTooltip");
        FilterLabel.Text = LocalizationService.Get("Filter");
        AllTypesItem.Content = LocalizationService.Get("AllTypes");
        FilesItem.Content = LocalizationService.Get("Files");
        FoldersItem.Content = LocalizationService.Get("Folders");
        ExtensionPopupTitle.Text = LocalizationService.Get("ExtensionPopupTitle");
        ExtensionFilterBox.ToolTip = LocalizationService.Get("CustomExtensions");
        HiddenFoldersCheckBox.Content = LocalizationService.Get("HiddenFolders");
        if (_sourceFilterOptions.Count > 0)
            _sourceFilterOptions[0] = new SourceFilterOption(LocalizationService.Get("AllSources"), null);
        UpdateExtensionFilterLabel();

        if (_trayIcon is not null)
        {
            _trayIcon.Text = LocalizationService.Get("TrayRunning");
            if (_trayIcon.ContextMenuStrip?.Items.Count > 0 && _trayIcon.ContextMenuStrip.Items[0] is Forms.ToolStripMenuItem showItem)
                showItem.Text = LocalizationService.Get("TrayOpen");
            if (_trayIcon.ContextMenuStrip?.Items.Count > 2 && _trayIcon.ContextMenuStrip.Items[2] is Forms.ToolStripMenuItem exitItem)
                exitItem.Text = LocalizationService.Get("TrayExit");
        }
    }

    private void UpdateExtensionFilterLabel()
    {
        int count = ExtensionOptionsPanel.Children.OfType<CheckBox>().Count(option => option.IsChecked == true);
        count += (ParseExtensions(ExtensionFilterBox.Text)?.Count ?? 0);
        ExtensionFilterToggle.Content = count == 0
            ? LocalizationService.Get("ExtensionsAll")
            : LocalizationService.Get("ExtensionsSelected", count);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Restore saved sources each time the application starts.
        foreach (string root in _settings.SearchRoots.ToArray())
        {
            if (TryGetNtfsDrive(root, out char drive)) await AddNtfsVolumeAsync(drive);
            else await AddFolderSourceAsync(root);
        }
    }

    private void InitializeTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        var showItem = new Forms.ToolStripMenuItem(LocalizationService.Get("TrayOpen"));
        showItem.Click += (_, _) => ShowMainWindow();
        var exitItem = new Forms.ToolStripMenuItem(LocalizationService.Get("TrayExit"));
        exitItem.Click += (_, _) =>
        {
            _allowClose = true;
            _trayIcon!.Visible = false;
            System.Windows.Application.Current.Shutdown();
        };
        menu.Items.Add(showItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);
        string? executable = Process.GetCurrentProcess().MainModule?.FileName;
        Drawing.Icon icon = Drawing.SystemIcons.Application;
        try
        {
            if (!string.IsNullOrEmpty(executable)) icon = Drawing.Icon.ExtractAssociatedIcon(executable) ?? icon;
        }
        catch { }
        _trayIcon = new Forms.NotifyIcon
        {
            Text = LocalizationService.Get("TrayRunning"),
            Icon = icon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private static bool TryGetNtfsDrive(string root, out char drive)
    {
        drive = '\0';
        if (root.Length != 3 || root[1] != ':' || root[2] != '\\') return false;
        var info = DriveInfo.GetDrives().FirstOrDefault(d => d.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
        if (info is null) return false;
        var option = CreateDriveOption(info);
        if (!option.IsReady || option.DriveType != DriveType.Fixed || !option.IsNtfs) return false;
        drive = root[0];
        return true;
    }

    private static DriveOption CreateDriveOption(DriveInfo drive)
    {
        // DriveFormat can throw for disconnected network drives.
        try
        {
            bool ready = drive.IsReady;
            string format = ready ? drive.DriveFormat : LocalizationService.Get("DriveDisconnected");
            string root = $"{drive.Name[0]}:\\";
            return new DriveOption(drive.Name[0], LocalizationService.Get("DriveLabel", drive.Name[0], format, drive.DriveType),
                drive.DriveType, ready, format.Equals("NTFS", StringComparison.OrdinalIgnoreCase), root);
        }
        catch (IOException)
        {
            return new DriveOption(drive.Name[0], LocalizationService.Get("DriveStatusLabel", drive.Name[0], LocalizationService.Get("DriveInaccessible"), drive.DriveType),
                drive.DriveType, false, false, $"{drive.Name[0]}:\\");
        }
        catch (UnauthorizedAccessException)
        {
            return new DriveOption(drive.Name[0], LocalizationService.Get("DriveStatusLabel", drive.Name[0], LocalizationService.Get("DriveAccessDenied"), drive.DriveType),
                drive.DriveType, false, false, $"{drive.Name[0]}:\\");
        }
    }

    private async void DriveSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDriveSelection) return;
        if (DriveSelector.SelectedValue is not char driveLetter) return;
        var selectedDrive = DriveSelector.Items.OfType<DriveOption>().FirstOrDefault(d => d.Letter == driveLetter);
        if (selectedDrive is null) return;
        string root = selectedDrive.SourceRoot;
        if (_sources.ContainsKey(root)) return;

        if (!selectedDrive.IsReady && selectedDrive.DriveType != DriveType.Network)
        {
            StatusText.Text = LocalizationService.Get("DriveUnavailable", root);
            return;
        }

        if (selectedDrive.DriveType == DriveType.Fixed && selectedDrive.IsNtfs)
            await AddNtfsVolumeAsync(driveLetter);
        else
            await AddFolderSourceAsync(root);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddSourceWindow { Owner = this };
        if (dialog.ShowDialog() == true)
            await AddFolderSourceAsync(dialog.SelectedPath);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _settings = dialog.Settings;
        LocalizationService.SetLanguage(_settings.Language);
        ApplyLocalization();
        LoadDriveOptions(autoStartDefaultDrive: false);
        ApplyTheme();
        try { SettingsStore.Save(_settings); }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("SettingsSaveFailed", ex.Message); }
        try { StartupRegistration.Apply(_settings.StartWithWindows); }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("StartupSaveFailed", ex.Message); }
        foreach (var source in _sources.Values)
            if (source.FolderSession is not null) source.FolderSession.ScanPerformance = _settings.ScanPerformance;
        await RefreshResultsAsync();
    }

    private void ApplyTheme()
    {
        bool dark = string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase);
        var colors = dark
            ? new Dictionary<string, string>
            {
                ["WindowBackground"] = "#12161C", ["PanelBackground"] = "#1B2029",
                ["InputBackground"] = "#202631", ["ControlBackground"] = "#2B3441",
                ["TextPrimary"] = "#F2F5F9", ["TextMuted"] = "#B0BBC8",
                ["BorderBrush"] = "#3A4654", ["AccentBrush"] = "#75A9FF"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackground"] = "#F3F5F8", ["PanelBackground"] = "#FFFFFF",
                ["InputBackground"] = "#FFFFFF", ["ControlBackground"] = "#E8EDF4",
                ["TextPrimary"] = "#17202B", ["TextMuted"] = "#536171",
                ["BorderBrush"] = "#C7D0DC", ["AccentBrush"] = "#1769D2"
            };
        foreach (var (key, color) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
        Application.Current.Resources["ResultFontSize"] = _settings.ResultFontSize;
        Application.Current.Resources["DetailFontSize"] = Math.Max(10, _settings.ResultFontSize - 3);
    }

    private async Task AddNtfsVolumeAsync(char drive)
    {
        string root = $"{char.ToUpperInvariant(drive)}:\\";
        if (_sources.ContainsKey(root)) return;
        var source = new SearchSource(root, "NTFS volume", new SearchIndex());
        AddSource(source);
        StatusText.Text = LocalizationService.Get("MftPreparing", root);
        source.Cancellation = new CancellationTokenSource();
        try
        {
            var reader = new NtfsVolumeReader(drive);
            // Apply changes made during the MFT scan starting at the captured USN cursor.
            long initialUsn = await Task.Run(reader.GetCurrentUsn, source.Cancellation.Token);
            await Task.Run(async () =>
            {
                await foreach (var entry in reader.EnumerateAsync(source.Cancellation.Token,
                                   () => _settings.ScanPerformance))
                    source.Index.Upsert(entry);
            }, source.Cancellation.Token);

            source.Status = LocalizationService.Get("MftUsn");
            UpdateSourceRow(source);
            StatusText.Text = LocalizationService.Get("MftReady", root);
            source.JournalCancellation = new CancellationTokenSource();
            _ = RunJournalAsync(source, drive, initialUsn, source.JournalCancellation.Token);
            await RefreshResultsAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            source.Status = LocalizationService.Get("Error");
            UpdateSourceRow(source);
            StatusText.Text = LocalizationService.Get("MftFailed", root, ex.Message);
        }
    }

    private async Task AddFolderSourceAsync(string folderPath)
    {
        string root;
        string? networkAccessError = null;
        try
        {
            // Support quoted paths and Windows variables such as %USERPROFILE%.
            string input = Environment.ExpandEnvironmentVariables(folderPath.Trim().Trim('"'));
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("InvalidFolderPath", ex.Message); return; }
        if (!TryReadFolder(root, out Exception? accessError))
        {
            // If the UNC path fails, retry once through a mapped drive for the same share.
            if (root.StartsWith(@"\\", StringComparison.Ordinal) &&
                NetworkDriveDiscovery.TryResolveToMappedPath(root, out string mappedPath) &&
                TryReadFolder(mappedPath, out _))
            {
                root = mappedPath;
            }
            else if (!root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                StatusText.Text = LocalizationService.Get("FolderAccessFailed", root, accessError?.Message ?? string.Empty);
                return;
            }
            else networkAccessError = accessError?.Message ?? string.Empty;
        }
        if (_sources.ContainsKey(root))
        {
            SourcesList.SelectedItem = _sourceRows.FirstOrDefault(x => x.Root.Equals(root, StringComparison.OrdinalIgnoreCase));
            return;
        }

        var source = new SearchSource(root, LocalizationService.Get("FolderSource"), new SearchIndex());
        AddSource(source);
        source.Cancellation = new CancellationTokenSource();
        source.FolderSession = new FolderIndexingSession(root, source.Index, _settings.ScanPerformance);
        source.FolderSession.StatusChanged += message => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_sources.ContainsKey(root))
            {
                source.Status = message;
                UpdateSourceRow(source);
                StatusText.Text = $"{root}: {message}";
            }
        }));
        source.FolderSession.IndexChanged += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_sources.ContainsKey(root))
            {
                UpdateSourceRow(source);
                _ = RefreshResultsAsync();
            }
        }));
        source.FolderSession.ScanProgressChanged += (_, scanned) => Dispatcher.BeginInvoke(new Action(() =>
        {
            var active = _sources.Values.Where(x => x.FolderSession?.IsScanning == true).ToArray();
            FolderProgressPanel.Visibility = active.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (active.Length > 0)
            {
                var current = active.FirstOrDefault(x => x.Root.Equals(root, StringComparison.OrdinalIgnoreCase)) ?? active[0];
                FolderProgressText.Text = LocalizationService.Get("ScanProgress", current.Root,
                    Math.Max(scanned, current.Index.TotalCount));
            }
        }));

        StatusText.Text = LocalizationService.Get("FolderScanning", root);
        try
        {
            await source.FolderSession.StartAsync(source.Cancellation.Token);
            source.Status = source.FolderSession.IsWatching
                ? LocalizationService.Get("LiveWatch")
                : LocalizationService.Get("LiveWatchUnavailable");
            if (networkAccessError is not null && !TryReadFolder(root, out _))
            {
                source.Status = LocalizationService.Get("NetworkPathUnavailable");
                StatusText.Text = LocalizationService.Get("NetworkPathQueued", root, networkAccessError);
            }
            UpdateSourceRow(source);
            await RefreshResultsAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            source.Status = LocalizationService.Get("Error");
            UpdateSourceRow(source);
            StatusText.Text = LocalizationService.Get("FolderRootScanFailed", root, ex.Message);
        }
    }

    /// <summary>
    /// Directory.Exists hides the underlying error, so this attempts to open the folder once.
    /// This provides more useful details for denied or unavailable UNC paths.
    /// </summary>
    private static bool TryReadFolder(string path, out Exception? error)
    {
        try
        {
            using IEnumerator<string> entries = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            _ = entries.MoveNext(); // Boş klasör de geçerli kaynaktır; önemli olan açılabilmesidir.
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
            error = ex;
            return false;
        }
    }

    private async Task RunJournalAsync(SearchSource source, char drive, long startingUsn,
        CancellationToken token)
    {
        try
        {
            var watcher = new UsnJournalWatcher(drive, source.Index);
            await watcher.RunAsync(startingUsn,
                status => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_sources.ContainsKey(source.Root)) StatusText.Text = LocalizationService.Get("VolumeStatus", source.Root, status);
                })),
                () => Dispatcher.BeginInvoke(new Action(() => _ = RefreshResultsAsync())), token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_sources.ContainsKey(source.Root))
                {
                    source.Status = LocalizationService.Get("WatcherError");
                    UpdateSourceRow(source);
                    StatusText.Text = LocalizationService.Get("WatcherStopped", source.Root, ex.Message);
                }
            }));
        }
    }

    private void AddSource(SearchSource source)
    {
        _sources.Add(source.Root, source);
        if (!_settings.SearchRoots.Contains(source.Root, StringComparer.OrdinalIgnoreCase))
        {
            _settings.SearchRoots.Add(source.Root);
            PersistSettings();
        }
        _sourceRows.Add(new SourceRow(source.Root, $"{source.Root} · {LocalizationService.Get("Preparing")}"));
        _sourceFilterOptions.Add(new SourceFilterOption(source.Root, source.Root));
        SourcesList.SelectedItem = _sourceRows.Last();
        CountText.Text = $"{_sources.Count} kaynak";
    }

    private void UpdateSourceRow(SearchSource source)
    {
        var row = _sourceRows.FirstOrDefault(x => x.Root.Equals(source.Root, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            int i = _sourceRows.IndexOf(row);
            _sourceRows[i] = new SourceRow(source.Root, $"{source.Root} · {source.Status}");
        }
        CountText.Text = LocalizationService.Get("RecordCount", source.Index.TotalCount, _sources.Count);
    }

    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RemoveSourceButton.IsEnabled = SourcesList.SelectedItem is SourceRow;

    private async void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (SourcesList.SelectedItem is not SourceRow row || !_sources.TryGetValue(row.Root, out var source)) return;
        var answer = MessageBox.Show(this,
            LocalizationService.Get("ConfirmRemove", row.Root),
            LocalizationService.Get("ConfirmRemoveTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || !_sources.Remove(row.Root)) return;
        _settings.SearchRoots.RemoveAll(path => path.Equals(row.Root, StringComparison.OrdinalIgnoreCase));
        PersistSettings();
        source.Cancellation?.Cancel();
        source.JournalCancellation?.Cancel();
        source.FolderSession?.Dispose();
        source.Cancellation?.Dispose();
        source.JournalCancellation?.Dispose();
        _sourceRows.Remove(row);
        var sourceOption = _sourceFilterOptions.FirstOrDefault(x =>
            x.Root?.Equals(row.Root, StringComparison.OrdinalIgnoreCase) == true);
        if (sourceOption is not null) _sourceFilterOptions.Remove(sourceOption);
        if (SourceFilterBox.SelectedValue is string selectedRoot &&
            selectedRoot.Equals(row.Root, StringComparison.OrdinalIgnoreCase))
            SourceFilterBox.SelectedIndex = 0;
        _results.Clear();
        CountText.Text = $"{_sources.Count} kaynak";
        StatusText.Text = LocalizationService.Get("SourceRemoved", row.Root);
        await RefreshResultsAsync();
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        await RefreshResultsAsync();

    private async void Filter_Changed(object sender, RoutedEventArgs e) => await RefreshResultsAsync();

    private void ExtensionFilterToggle_Click(object sender, RoutedEventArgs e) =>
        ExtensionFilterPopup.IsOpen = ExtensionFilterToggle.IsChecked == true;

    private async void Extension_Changed(object sender, RoutedEventArgs e)
    {
        UpdateExtensionFilterLabel();
        await RefreshResultsAsync();
    }

    private async void ExtensionFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateExtensionFilterLabel();
        await RefreshResultsAsync();
    }

    private async void HiddenFolders_Changed(object sender, RoutedEventArgs e)
    {
        _settings.ShowHiddenFolders = HiddenFoldersCheckBox.IsChecked == true;
        PersistSettings();
        await RefreshResultsAsync();
    }

    private async Task RefreshResultsAsync()
    {
        if (_sources.Count == 0) return;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        string query = SearchBox.Text;
        try
        {
            await Task.Delay(Math.Clamp(_settings.SearchDebounceMs, 0, 1000), token);
            var sourceSnapshot = _sources.Values.ToArray();
            if (SourceFilterBox.SelectedValue is string selectedRoot)
                sourceSnapshot = sourceSnapshot.Where(x =>
                    x.Root.Equals(selectedRoot, StringComparison.OrdinalIgnoreCase)).ToArray();
            bool? directoriesOnly = TypeFilterBox.SelectedItem is ComboBoxItem typeItem
                ? typeItem.Tag?.ToString() switch { "Files" => false, "Folders" => true, _ => null }
                : null;
            var extensions = GetSelectedExtensions();
            bool showHiddenFolders = HiddenFoldersCheckBox.IsChecked == true;
            var tasks = sourceSnapshot.Select(source => source.Index.SearchAsync(query, _settings.MaxResults, token,
                source.Kind == "NTFS volume" ? source.Root : null, directoriesOnly, extensions, showHiddenFolders));
            var resultSets = await Task.WhenAll(tasks);
            if (token.IsCancellationRequested) return;
            _results.Clear();
            foreach (var item in resultSets.SelectMany(x => x)
                         .DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(_settings.MaxResults))
                _results.Add(new ResultRow(item.Name, item.Path, item.IsDirectory ? "📁" : "📄"));
            CountText.Text = LocalizationService.Get("ResultCount", _results.Count, _sources.Count);
        }
        catch (OperationCanceledException) { }
    }

    private static IReadOnlySet<string>? ParseExtensions(string text)
    {
        var extensions = text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.StartsWith('*') ? value[1..] : value)
            .Select(value => value.StartsWith('.') ? value : $".{value}")
            .Where(value => value.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return extensions.Count == 0 ? null : extensions;
    }

    private IReadOnlySet<string>? GetSelectedExtensions()
    {
        var extensions = ExtensionOptionsPanel.Children.OfType<CheckBox>()
            .Where(option => option.IsChecked == true)
            .Select(option => option.Tag?.ToString())
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        extensions.UnionWith(ParseExtensions(ExtensionFilterBox.Text) ?? new HashSet<string>());
        return extensions.Count == 0 ? null : extensions;
    }

    /// <summary>Gets the row associated with the context-menu command.</summary>
    private static ResultRow? GetContextMenuResult(object sender) =>
        (sender as MenuItem)?.DataContext as ResultRow;

    private void ResultContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.Header = item.Tag?.ToString() switch
            {
                "CopyPath" => LocalizationService.Get("CopyPath"),
                "OpenLocation" => LocalizationService.Get("OpenLocation"),
                "Open" => LocalizationService.Get("Open"),
                _ => item.Header
            };
        }
    }

    /// <summary>Copies the result path to the clipboard without adding quotes.</summary>
    private void CopyResultPath_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuResult(sender) is not ResultRow row) return;
        try
        {
            Clipboard.SetText(row.Path);
            StatusText.Text = LocalizationService.Get("CopySuccess");
        }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("CopyFailed", ex.Message); }
    }

    /// <summary>Selects a file in Explorer or opens the folder result itself.</summary>
    private void RevealResult_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuResult(sender) is not ResultRow row) return;
        try
        {
            if (Directory.Exists(row.Path))
            {
                Process.Start("explorer.exe", $"\"{row.Path}\"");
            }
            else if (File.Exists(row.Path))
            {
                // /select opens the containing folder and selects the file.
                Process.Start("explorer.exe", $"/select,\"{row.Path}\"");
            }
            else
            {
                StatusText.Text = LocalizationService.Get("MissingItem");
            }
        }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("LocationOpenFailed", ex.Message); }
    }

    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuResult(sender) is ResultRow row) OpenResult(row);
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is ResultRow row) OpenResult(row);
    }

    /// <summary>Opens files with their default application and folders in Explorer.</summary>
    private void OpenResult(ResultRow row)
    {
        try
        {
            if (Directory.Exists(row.Path))
                Process.Start("explorer.exe", $"\"{row.Path}\"");
            else if (File.Exists(row.Path))
                Process.Start(new ProcessStartInfo(row.Path) { UseShellExecute = true });
            else
                StatusText.Text = LocalizationService.Get("MissingItem");
        }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("ItemOpenFailed", ex.Message); }
    }

    protected override void OnClosed(EventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        foreach (var source in _sources.Values)
        {
            source.Cancellation?.Cancel();
            source.JournalCancellation?.Cancel();
            source.FolderSession?.Dispose();
        }
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        base.OnClosed(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            _trayIcon?.ShowBalloonTip(1800, LocalizationService.Get("AppName"), LocalizationService.Get("TrayBalloon"), Forms.ToolTipIcon.Info);
            return;
        }
        base.OnClosing(e);
    }

    private void PersistSettings()
    {
        try { SettingsStore.Save(_settings); }
        catch (Exception ex) { StatusText.Text = LocalizationService.Get("SourceSaveFailed", ex.Message); }
    }

    private sealed record DriveOption(char Letter, string Label, DriveType DriveType, bool IsReady,
        bool IsNtfs, string SourceRoot);
    private sealed record SourceRow(string Root, string Label);
    private sealed record SourceFilterOption(string Label, string? Root);
    private sealed record ResultRow(string Name, string Path, string Icon);

    private sealed class SearchSource(string root, string kind, SearchIndex index)
    {
        public string Root { get; } = root;
        public string Kind { get; } = kind;
        public SearchIndex Index { get; } = index;
        public string Status { get; set; } = LocalizationService.Get("Preparing");
        public CancellationTokenSource? Cancellation { get; set; }
        public CancellationTokenSource? JournalCancellation { get; set; }
        public FolderIndexingSession? FolderSession { get; set; }
    }
}
