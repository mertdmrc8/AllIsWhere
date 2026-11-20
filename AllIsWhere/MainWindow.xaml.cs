using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using AllIsWhere.Engine;
using AllIsWhere.Models;
using AllIsWhere.Services;

namespace AllIsWhere;

public partial class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private sealed record DriveOption(char Letter, string Label, DriveType DriveType, bool IsReady, bool IsNtfs, string SourceRoot)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	private sealed record SourceRow(string Root, string Label);

	private sealed record SourceFilterOption(string Label, string? Root)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	private sealed record ResultRow(string Name, string Path, bool IsDirectory, string TypeLabel);

	private sealed class SearchSource(string root, string kind, SearchIndex index)
	{
		public int CacheSaveRunning;

		public string Root { get; } = root;

		public string Kind { get; } = kind;

		public SearchIndex Index { get; } = index;

		public string Status { get; set; } = LocalizationService.Get("Preparing");

		public bool IsIndexing { get; set; } = true;

		public CancellationTokenSource? Cancellation { get; set; }

		public CancellationTokenSource? JournalCancellation { get; set; }

		public FolderIndexingSession? FolderSession { get; set; }

		public ulong JournalId { get; set; }

		public long LastUsn { get; set; }

		public DateTime LastCacheSaveUtc { get; set; }

		public Task? JournalTask { get; set; }
	}

	private readonly ObservableCollection<SourceRow> _sourceRows = new ObservableCollection<SourceRow>();

	private readonly ObservableCollection<SourceFilterOption> _sourceFilterOptions = new ObservableCollection<SourceFilterOption>();

	private readonly Dictionary<string, SearchSource> _sources = new Dictionary<string, SearchSource>(StringComparer.OrdinalIgnoreCase);

	private readonly ObservableCollection<ResultRow> _results = new ObservableCollection<ResultRow>();

	private readonly ObservableCollection<SavedSearch> _savedSearchRows = new ObservableCollection<SavedSearch>();

	private CancellationTokenSource? _searchCts;

	private UserSettings _settings = new UserSettings();

	private NotifyIcon? _trayIcon;

	private bool _allowClose;

	private bool _suppressDriveSelection;

	private bool _updateCheckInProgress;

	private bool _loadingSavedSearches;

	public MainWindow()
	{
		InitializeComponent();
		_settings = SettingsStore.Load();
		_settings.Normalize();
		LocalizationService.SetLanguage(_settings.Language);
		ApplyLocalization();
		ApplyTheme();
		InitializeTrayIcon();
		try
		{
			StartupRegistration.Apply(_settings.StartWithWindows);
		}
		catch (Exception ex)
		{
			StatusText.Text = LocalizationService.Get("StartupApplyFailed", ex.Message);
		}
		ResultsList.ItemsSource = _results;
		SourcesList.ItemsSource = _sourceRows;
		SourceFilterBox.ItemsSource = _sourceFilterOptions;
		SourceFilterBox.DisplayMemberPath = "Label";
		SourceFilterBox.SelectedValuePath = "Root";
		_sourceFilterOptions.Add(new SourceFilterOption(LocalizationService.Get("AllSources"), null));
		SourceFilterBox.SelectedIndex = 0;
		SavedSearchBox.ItemsSource = _savedSearchRows;
		SavedSearchBox.DisplayMemberPath = "Name";
		LoadSavedSearchRows();
		HiddenFoldersCheckBox.IsChecked = _settings.ShowHiddenFolders;
		HiddenFoldersCheckBox.Checked += HiddenFolders_Changed;
		HiddenFoldersCheckBox.Unchecked += HiddenFolders_Changed;
		LoadDriveOptions(autoStartDefaultDrive: true);
		UpdateEmptyState();
		Loaded += MainWindow_Loaded;
	}

	private void LoadDriveOptions(bool autoStartDefaultDrive)
	{
		List<DriveOption> list = (from drive in DriveInfo.GetDrives()
			where drive.Name.Length >= 3 && drive.Name[1] == ':'
			select drive).Select(CreateDriveOption).ToList();
		foreach (KeyValuePair<char, string> mapping in NetworkDriveDiscovery.GetMappings())
		{
			var (letter, text2) = mapping;
			int num = list.FindIndex((DriveOption drive) => drive.Letter == letter);
			DriveOption driveOption = new DriveOption(letter, LocalizationService.Get("NetworkDriveLabel", letter, text2), DriveType.Network, num >= 0 && list[num].IsReady, IsNtfs: false, text2);
			if (num >= 0)
			{
				list[num] = driveOption;
			}
			else
			{
				list.Add(driveOption);
			}
		}
		char? priorSelection = ((DriveSelector.SelectedValue is char value) ? new char?(value) : ((char?)null));
		DriveOption driveOption2 = list.FirstOrDefault((DriveOption drive) => drive.DriveType == DriveType.Fixed && drive.IsNtfs);
		_suppressDriveSelection = !autoStartDefaultDrive || _settings.SearchRoots.Count > 0;
		DriveSelector.ItemsSource = list;
		DriveSelector.DisplayMemberPath = "Label";
		DriveSelector.SelectedValuePath = "Letter";
		DriveSelector.SelectedValue = (list.Any((DriveOption drive) => drive.Letter == priorSelection) ? priorSelection : driveOption2?.Letter);
		_suppressDriveSelection = false;
		if (DriveSelector.SelectedValue == null)
		{
			StatusText.Text = LocalizationService.Get("NoSource");
		}
	}

	private void ApplyLocalization()
	{
		Title = LocalizationService.Get("AppName");
		HeaderTitle.Text = LocalizationService.Get("AppName");
		VersionText.Text = "v" + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
		TaglineText.Text = LocalizationService.Get("Tagline");
		AddFolderButton.Content = LocalizationService.Get("AddFolder");
		SettingsButton.Content = LocalizationService.Get("Settings");
		UpdateCheckButton.Content = LocalizationService.Get("CheckUpdates");
		SourcesLabel.Text = LocalizationService.Get("Sources");
		ResultsHeading.Text = LocalizationService.Get("ResultsHeading");
		NameColumnHeader.Text = LocalizationService.Get("ColumnName");
		PathColumnHeader.Text = LocalizationService.Get("ColumnPath");
		TypeColumnHeader.Text = LocalizationService.Get("ColumnType");
		SortNameItem.Content = LocalizationService.Get("SortName");
		SortPathItem.Content = LocalizationService.Get("SortPath");
		SortTypeItem.Content = LocalizationService.Get("SortType");
		SortBox.ToolTip = LocalizationService.Get("SearchSort");
		RemoveSourceButton.Content = LocalizationService.Get("RemoveSelected");
		RefreshSourceButton.Content = LocalizationService.Get("RefreshSelected");
		SearchBox.ToolTip = LocalizationService.Get("SearchTooltip");
		FilterLabel.Text = LocalizationService.Get("Filter");
		AllTypesItem.Content = LocalizationService.Get("AllTypes");
		FilesItem.Content = LocalizationService.Get("Files");
		FoldersItem.Content = LocalizationService.Get("Folders");
		ExtensionPopupTitle.Text = LocalizationService.Get("ExtensionPopupTitle");
		ExtensionFilterBox.ToolTip = LocalizationService.Get("CustomExtensions");
		HiddenFoldersCheckBox.Content = LocalizationService.Get("HiddenFolders");
		ModifiedFilterBox.ToolTip = LocalizationService.Get("ModifiedFilter");
		SizeFilterBox.ToolTip = LocalizationService.Get("SizeFilter");
		SavedSearchBox.ToolTip = LocalizationService.Get("SavedSearches");
		SaveSearchButton.Content = LocalizationService.Get("SaveSearch");
		RemoveSavedSearchButton.Content = LocalizationService.Get("RemoveSavedSearch");
		SetComboItemLabel(ModifiedFilterBox, "0", "ModifiedAny");
		SetComboItemLabel(ModifiedFilterBox, "1", "ModifiedDay");
		SetComboItemLabel(ModifiedFilterBox, "7", "ModifiedWeek");
		SetComboItemLabel(ModifiedFilterBox, "30", "ModifiedMonth");
		SetComboItemLabel(ModifiedFilterBox, "365", "ModifiedYear");
		SetComboItemLabel(SizeFilterBox, "All", "SizeAny");
		SetComboItemLabel(SizeFilterBox, "1", "SizeOver1Mb");
		SetComboItemLabel(SizeFilterBox, "10", "SizeOver10Mb");
		SetComboItemLabel(SizeFilterBox, "100", "SizeOver100Mb");
		SetComboItemLabel(SizeFilterBox, "1024", "SizeOver1Gb");
		LoadSavedSearchRows();
		if (_sourceFilterOptions.Count > 0)
		{
			_sourceFilterOptions[0] = new SourceFilterOption(LocalizationService.Get("AllSources"), null);
		}
		UpdateExtensionFilterLabel();
		if (_trayIcon != null)
		{
			_trayIcon.Text = LocalizationService.Get("TrayRunning");
			ContextMenuStrip? contextMenuStrip = _trayIcon.ContextMenuStrip;
			if (contextMenuStrip != null && contextMenuStrip.Items.Count > 0 && _trayIcon.ContextMenuStrip.Items[0] is ToolStripMenuItem toolStripMenuItem)
			{
				toolStripMenuItem.Text = LocalizationService.Get("TrayOpen");
			}
			ContextMenuStrip? contextMenuStrip2 = _trayIcon.ContextMenuStrip;
			if (contextMenuStrip2 != null && contextMenuStrip2.Items.Count > 2 && _trayIcon.ContextMenuStrip.Items[2] is ToolStripMenuItem toolStripMenuItem2)
			{
				toolStripMenuItem2.Text = LocalizationService.Get("TrayExit");
			}
		}
	}

	private static void SetComboItemLabel(System.Windows.Controls.ComboBox box, string tag, string key)
	{
		foreach (ComboBoxItem item in (IEnumerable)box.Items)
		{
			if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
			{
				item.Content = LocalizationService.Get(key);
			}
		}
	}

	private void LoadSavedSearchRows()
	{
		if (SavedSearchBox == null)
		{
			return;
		}
		_loadingSavedSearches = true;
		_savedSearchRows.Clear();
		foreach (SavedSearch savedSearch in _settings.SavedSearches)
		{
			_savedSearchRows.Add(savedSearch);
		}
		SavedSearchBox.SelectedItem = null;
		RemoveSavedSearchButton.IsEnabled = false;
		_loadingSavedSearches = false;
	}

	private void UpdateExtensionFilterLabel()
	{
		int num = ExtensionOptionsPanel.Children.OfType<System.Windows.Controls.CheckBox>().Count((System.Windows.Controls.CheckBox option) => option.IsChecked == true);
		num += ParseExtensions(ExtensionFilterBox.Text)?.Count ?? 0;
		ExtensionFilterToggle.Content = ((num == 0) ? LocalizationService.Get("ExtensionsAll") : LocalizationService.Get("ExtensionsSelected", num));
	}

	private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		CheckForUpdatesAtStartupAsync();
		string[] array = _settings.SearchRoots.ToArray();
        var folderTasks = new List<Task>();
		foreach (string text in array)
		{
			if (TryGetNtfsDrive(text, out var drive))
			{
				await AddNtfsVolumeAsync(drive);
			}
			else
			{
				folderTasks.Add(AddFolderSourceAsync(text, restoreSaved: true));
			}
		}
	    await Task.WhenAll(folderTasks);
    }

	private async Task CheckForUpdatesAtStartupAsync()
	{
		DateTime? lastUpdateCheckUtc = _settings.LastUpdateCheckUtc;
		if (lastUpdateCheckUtc.HasValue)
		{
			DateTime valueOrDefault = lastUpdateCheckUtc.GetValueOrDefault();
			if (DateTime.UtcNow - valueOrDefault < TimeSpan.FromHours(24))
			{
				return;
			}
		}
		await Task.Delay(TimeSpan.FromSeconds(15L));
		if (IsVisible)
		{
			await CheckForUpdatesAsync(automatic: true);
		}
	}

	private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
	{
		await CheckForUpdatesAsync(automatic: false);
	}

	private async Task CheckForUpdatesAsync(bool automatic)
	{
		if (_updateCheckInProgress)
		{
			return;
		}
		_updateCheckInProgress = true;
		UpdateCheckButton.IsEnabled = false;
		StatusText.Text = LocalizationService.Get("UpdateChecking");
		try
		{
			AppUpdate appUpdate = await UpdateService.FindUpdateAsync(typeof(MainWindow).Assembly.GetName().Version ?? new Version(1, 0, 0));
			_settings.LastUpdateCheckUtc = DateTime.UtcNow;
			PersistSettings();
			if ((object)appUpdate == null)
			{
				if (!automatic)
				{
					StatusText.Text = LocalizationService.Get("UpdateCurrent");
				}
				return;
			}
			string messageBoxText = LocalizationService.Get("UpdateAvailable", appUpdate.Version, appUpdate.ReleaseNotes);
			if (System.Windows.MessageBox.Show(this, messageBoxText, LocalizationService.Get("AppName"), MessageBoxButton.YesNo, MessageBoxImage.Asterisk, MessageBoxResult.Yes) != MessageBoxResult.Yes)
			{
				StatusText.Text = LocalizationService.Get("CheckUpdates");
				return;
			}
			StatusText.Text = LocalizationService.Get("UpdateDownloading", appUpdate.Version);
			Process.Start(new ProcessStartInfo(await UpdateService.DownloadAndVerifyAsync(appUpdate))
			{
				Arguments = $"/update {Environment.ProcessId}",
				UseShellExecute = true
			});
			StatusText.Text = LocalizationService.Get("UpdateStarting");
			_allowClose = true;
			if (_trayIcon != null)
			{
				_trayIcon.Visible = false;
			}
			System.Windows.Application.Current.Shutdown();
		}
		catch (Exception ex)
		{
			if (automatic)
			{
				_settings.LastUpdateCheckUtc = DateTime.UtcNow;
				PersistSettings();
			}
			bool flag = !automatic;
			if (!flag)
			{
				bool flag2 = ((ex is InvalidDataException || ex is FileNotFoundException) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				StatusText.Text = LocalizationService.Get("UpdateCheckFailed", ex.Message);
			}
		}
		finally
		{
			_updateCheckInProgress = false;
			if (!_allowClose)
			{
				UpdateCheckButton.IsEnabled = true;
			}
		}
	}

	private void InitializeTrayIcon()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(LocalizationService.Get("TrayOpen"));
		toolStripMenuItem.Click += (object? _, EventArgs _) =>
		{
			ShowMainWindow();
		};
		ToolStripMenuItem toolStripMenuItem2 = new ToolStripMenuItem(LocalizationService.Get("TrayExit"));
		toolStripMenuItem2.Click += (object? _, EventArgs _) =>
		{
			_allowClose = true;
			_trayIcon.Visible = false;
			System.Windows.Application.Current.Shutdown();
		};
		contextMenuStrip.Items.Add(toolStripMenuItem);
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add(toolStripMenuItem2);
		string text = Process.GetCurrentProcess().MainModule?.FileName;
		Icon icon = SystemIcons.Application;
		try
		{
			if (!string.IsNullOrEmpty(text))
			{
				icon = System.Drawing.Icon.ExtractAssociatedIcon(text) ?? icon;
			}
		}
		catch
		{
		}
		_trayIcon = new NotifyIcon
		{
			Text = LocalizationService.Get("TrayRunning"),
			Icon = icon,
			ContextMenuStrip = contextMenuStrip,
			Visible = true
		};
		_trayIcon.DoubleClick += (object? _, EventArgs _) =>
		{
			ShowMainWindow();
		};
	}

	private void ShowMainWindow()
	{
		Show();
		if (WindowState == WindowState.Minimized)
		{
			WindowState = WindowState.Normal;
		}
		Activate();
	}

	private static bool TryGetNtfsDrive(string root, out char drive)
	{
		drive = '\0';
		if (root.Length != 3 || root[1] != ':' || root[2] != '\\')
		{
			return false;
		}
		DriveInfo driveInfo = DriveInfo.GetDrives().FirstOrDefault((DriveInfo d) => d.Name.Equals(root, StringComparison.OrdinalIgnoreCase));
		if (driveInfo == null)
		{
			return false;
		}
		DriveOption driveOption = CreateDriveOption(driveInfo);
		if (!driveOption.IsReady || driveOption.DriveType != DriveType.Fixed || !driveOption.IsNtfs)
		{
			return false;
		}
		drive = root[0];
		return true;
	}

	private static DriveOption CreateDriveOption(DriveInfo drive)
	{
		try
		{
			bool isReady = drive.IsReady;
			string text = (isReady ? drive.DriveFormat : LocalizationService.Get("DriveDisconnected"));
			string sourceRoot = $"{drive.Name[0]}:\\";
			return new DriveOption(drive.Name[0], LocalizationService.Get("DriveLabel", drive.Name[0], text, drive.DriveType), drive.DriveType, isReady, text.Equals("NTFS", StringComparison.OrdinalIgnoreCase), sourceRoot);
		}
		catch (IOException)
		{
			return new DriveOption(drive.Name[0], LocalizationService.Get("DriveStatusLabel", drive.Name[0], LocalizationService.Get("DriveInaccessible"), drive.DriveType), drive.DriveType, IsReady: false, IsNtfs: false, $"{drive.Name[0]}:\\");
		}
		catch (UnauthorizedAccessException)
		{
			return new DriveOption(drive.Name[0], LocalizationService.Get("DriveStatusLabel", drive.Name[0], LocalizationService.Get("DriveAccessDenied"), drive.DriveType), drive.DriveType, IsReady: false, IsNtfs: false, $"{drive.Name[0]}:\\");
		}
	}

	private async void DriveSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_suppressDriveSelection)
		{
			return;
		}
		object selectedValue = DriveSelector.SelectedValue;
		if (!(selectedValue is char))
		{
			return;
		}
		char driveLetter = (char)selectedValue;
		DriveOption driveOption = DriveSelector.Items.OfType<DriveOption>().FirstOrDefault((DriveOption d) => d.Letter == driveLetter);
		if ((object)driveOption == null)
		{
			return;
		}
		string sourceRoot = driveOption.SourceRoot;
		if (!_sources.ContainsKey(sourceRoot))
		{
			if (!driveOption.IsReady && driveOption.DriveType != DriveType.Network)
			{
				StatusText.Text = LocalizationService.Get("DriveUnavailable", sourceRoot);
			}
			else if (driveOption.DriveType != DriveType.Fixed || !driveOption.IsNtfs)
			{
				await AddFolderSourceAsync(sourceRoot);
			}
			else
			{
				await AddNtfsVolumeAsync(driveLetter);
			}
		}
	}

	private async void AddFolder_Click(object sender, RoutedEventArgs e)
	{
		AddSourceWindow addSourceWindow = new AddSourceWindow
		{
			Owner = this
		};
		if (addSourceWindow.ShowDialog() == true)
		{
			await AddFolderSourceAsync(addSourceWindow.SelectedPath);
		}
	}

	private async void Settings_Click(object sender, RoutedEventArgs e)
	{
		SettingsWindow settingsWindow = new SettingsWindow(_settings)
		{
			Owner = this
		};
		if (settingsWindow.ShowDialog() != true)
		{
			return;
		}
		_settings = settingsWindow.Settings;
		LocalizationService.SetLanguage(_settings.Language);
		ApplyLocalization();
		LoadDriveOptions(autoStartDefaultDrive: false);
		ApplyTheme();
		try
		{
			SettingsStore.Save(_settings);
		}
		catch (Exception ex)
		{
			StatusText.Text = LocalizationService.Get("SettingsSaveFailed", ex.Message);
		}
		try
		{
			StartupRegistration.Apply(_settings.StartWithWindows);
		}
		catch (Exception ex2)
		{
			StatusText.Text = LocalizationService.Get("StartupSaveFailed", ex2.Message);
		}
		foreach (SearchSource value in _sources.Values)
		{
			if (value.FolderSession != null)
			{
				value.FolderSession.ScanPerformance = _settings.ScanPerformance;
			}
		}
		await RefreshResultsAsync();
	}

	private void ApplyTheme()
	{
		foreach (var (key, value) in string.Equals(_settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? new Dictionary<string, string>
		{
			["WindowBackground"] = "#101319",
			["PanelBackground"] = "#191E27",
			["InputBackground"] = "#222936",
			["ControlBackground"] = "#2B3442",
			["TextPrimary"] = "#FFFFFF",
			["TextMuted"] = "#D0D7E2",
			["BorderBrush"] = "#4B5668",
			["AccentBrush"] = "#BDD0FF",
			["PrimaryContainerBrush"] = "#2D4164",
			["OnAccentBrush"] = "#101526"
		} : new Dictionary<string, string>
		{
			["WindowBackground"] = "#F4F6FA",
			["PanelBackground"] = "#FFFFFF",
			["InputBackground"] = "#FFFFFF",
			["ControlBackground"] = "#EEF1F7",
			["TextPrimary"] = "#1A1B1F",
			["TextMuted"] = "#606875",
			["BorderBrush"] = "#DFE3EB",
			["AccentBrush"] = "#3F5FCE",
			["PrimaryContainerBrush"] = "#E8EEFF",
			["OnAccentBrush"] = "#FFFFFF"
		})
		{
			SolidColorBrush solidColorBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
			solidColorBrush.Freeze();
			System.Windows.Application.Current.Resources[key] = solidColorBrush;
		}
		System.Windows.Application.Current.Resources["ResultFontSize"] = _settings.ResultFontSize;
		System.Windows.Application.Current.Resources["DetailFontSize"] = Math.Max(10.0, _settings.ResultFontSize - 3.0);
	}

	private async Task AddNtfsVolumeAsync(char drive)
	{
		string text = $"{char.ToUpperInvariant(drive)}:\\";
		if (!_sources.ContainsKey(text))
		{
			SearchSource searchSource = new SearchSource(text, "NTFS volume", new SearchIndex());
			AddSource(searchSource);
			searchSource.Cancellation = new CancellationTokenSource();
			await RefreshNtfsIndexAsync(searchSource, useCache: true);
		}
	}

	private async Task RefreshNtfsIndexAsync(SearchSource source, bool useCache)
	{
		char drive = char.ToUpperInvariant(source.Root[0]);
		CancellationToken token = source.Cancellation?.Token ?? CancellationToken.None;
		try
		{
			NtfsVolumeReader reader = new NtfsVolumeReader(drive);
			Task<JournalCheckpoint> task = Task.Run((Func<JournalCheckpoint>)reader.GetJournalCheckpoint, token);
			Task<CachedIndexSnapshot> cacheTask = Task.Run(() => (!useCache || !IndexCacheStore.TryLoad(drive, source.Index, out CachedIndexSnapshot snapshot)) ? null : snapshot, token);
			JournalCheckpoint currentState = await task;
			CachedIndexSnapshot cachedIndexSnapshot = await cacheTask;
			bool cacheIsCurrent = (object)cachedIndexSnapshot != null && cachedIndexSnapshot.JournalId == currentState.JournalId && cachedIndexSnapshot.NextUsn >= currentState.FirstUsn && cachedIndexSnapshot.NextUsn <= currentState.NextUsn;
			JournalCheckpoint checkpoint;
			if (cacheIsCurrent)
			{
				checkpoint = new JournalCheckpoint(cachedIndexSnapshot.JournalId, currentState.FirstUsn, cachedIndexSnapshot.NextUsn);
				source.Status = LocalizationService.Get("CacheLoaded");
				source.IsIndexing = false;
				UpdateSourceRow(source);
				await RefreshResultsAsync();
			}
			else
			{
				source.IsIndexing = true;
				source.Status = LocalizationService.Get("MftPreparing", source.Root);
				StatusText.Text = LocalizationService.Get("MftPreparing", source.Root);
				UpdateSourceRow(source);
				source.Index.Clear();
				checkpoint = currentState;
				await Task.Run(async () =>
				{
					await foreach (IndexedEntry item in reader.EnumerateAsync(token, () => _settings.ScanPerformance))
					{
						source.Index.Upsert(item);
					}
				}, token);
				source.LastCacheSaveUtc = DateTime.UtcNow;
				source.JournalId = checkpoint.JournalId;
				source.LastUsn = checkpoint.NextUsn;
				try
				{
					await Task.Run(() =>
					{
						IndexCacheStore.Save(drive, checkpoint.JournalId, checkpoint.NextUsn, source.Index);
					}, token);
				}
				catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) ? true : false)
				{
				}
			}
			source.IsIndexing = false;
			source.Status = (cacheIsCurrent ? LocalizationService.Get("CacheLoaded") : LocalizationService.Get("MftUsn"));
			source.JournalId = checkpoint.JournalId;
			source.LastUsn = checkpoint.NextUsn;
			UpdateSourceRow(source);
			StatusText.Text = LocalizationService.Get("MftReady", source.Root);
			source.JournalCancellation?.Cancel();
			source.JournalCancellation?.Dispose();
			source.JournalCancellation = new CancellationTokenSource();
			source.JournalTask = RunJournalAsync(source, drive, checkpoint, source.JournalCancellation.Token);
			await RefreshResultsAsync();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			source.IsIndexing = false;
			source.Status = LocalizationService.Get("Error");
			UpdateSourceRow(source);
			StatusText.Text = LocalizationService.Get("MftFailed", source.Root, ex3.Message);
		}
	}

	private async Task AddFolderSourceAsync(string folderPath, bool restoreSaved = false)
	{
		string networkAccessError = null;
		string root;
		try
		{
			string path = Environment.ExpandEnvironmentVariables(folderPath.Trim().Trim('"'));
			root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		}
		catch (Exception ex)
		{
			StatusText.Text = LocalizationService.Get("InvalidFolderPath", ex.Message);
			return;
		}
		Exception error2;
		if (!(restoreSaved && FolderIndexCacheStore.Exists(root)) && !TryReadFolder(root, out Exception error))
		{
			if (root.StartsWith("\\\\", StringComparison.Ordinal) && NetworkDriveDiscovery.TryResolveToMappedPath(root, out string mappedPath) && TryReadFolder(mappedPath, out error2))
			{
				root = mappedPath;
			}
			else
			{
				if (!root.StartsWith("\\\\", StringComparison.Ordinal))
				{
					StatusText.Text = LocalizationService.Get("FolderAccessFailed", root, error?.Message ?? string.Empty);
					return;
				}
				networkAccessError = error?.Message ?? string.Empty;
			}
		}
		if (_sources.ContainsKey(root))
		{
			SourcesList.SelectedItem = _sourceRows.FirstOrDefault((SourceRow x) => x.Root.Equals(root, StringComparison.OrdinalIgnoreCase));
			return;
		}
		SearchSource source = new SearchSource(root, LocalizationService.Get("FolderSource"), new SearchIndex());
		AddSource(source);
		source.Cancellation = new CancellationTokenSource();
		source.FolderSession = new FolderIndexingSession(root, source.Index, _settings.ScanPerformance);
		source.FolderSession.StatusChanged += (string message) =>
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				if (_sources.ContainsKey(root))
				{
					source.Status = message;
					UpdateSourceRow(source);
					StatusText.Text = root + ": " + message;
				}
			}));
		};
		source.FolderSession.IndexChanged += () =>
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				if (_sources.ContainsKey(root))
				{
					UpdateSourceRow(source);
					RefreshResultsAsync();
				}
			}));
		};
		source.FolderSession.ScanProgressChanged += (bool _, int scanned) =>
		{
			Dispatcher.BeginInvoke((Action)(() =>
			{
				SearchSource[] array = _sources.Values.Where((SearchSource x) => x.FolderSession?.IsScanning ?? false).ToArray();
				FolderProgressPanel.Visibility = ((array.Length == 0) ? Visibility.Collapsed : Visibility.Visible);
				if (array.Length != 0)
				{
					SearchSource searchSource2 = array.FirstOrDefault((SearchSource x) => x.Root.Equals(root, StringComparison.OrdinalIgnoreCase)) ?? array[0];
					FolderProgressText.Text = LocalizationService.Get("ScanProgress", searchSource2.Root, Math.Max(scanned, searchSource2.Index.TotalCount));
				}
			}));
		};
		StatusText.Text = LocalizationService.Get("FolderScanning", root);
		try
		{
			await source.FolderSession.StartAsync(source.Cancellation.Token);
			source.IsIndexing = false;
			SearchSource searchSource = source;
			string status;
			if (source.FolderSession.IsNetworkAvailable)
			{
				status = (source.FolderSession.IsWatching ? LocalizationService.Get("LiveWatch") : LocalizationService.Get("LiveWatchUnavailable"));
			}
			else
			{
				status = LocalizationService.Get("NetworkDisconnected");
			}
			searchSource.Status = status;
			if (networkAccessError != null && !TryReadFolder(root, out error2))
			{
				source.Status = LocalizationService.Get("NetworkDisconnected");
				StatusText.Text = LocalizationService.Get("NetworkPathQueued", root, networkAccessError);
			}
			UpdateSourceRow(source);
			await RefreshResultsAsync();
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex3)
		{
			source.IsIndexing = false;
			source.Status = LocalizationService.Get("Error");
			UpdateSourceRow(source);
			StatusText.Text = LocalizationService.Get("FolderRootScanFailed", root, ex3.Message);
		}
	}

	private static bool TryReadFolder(string path, out Exception? error)
	{
		try
		{
			using IEnumerator<string> enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
			enumerator.MoveNext();
			error = null;
			return true;
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException || ex is ArgumentException) ? true : false)
		{
			error = ex;
			return false;
		}
	}

	private async Task RunJournalAsync(SearchSource source, char drive, JournalCheckpoint startingCheckpoint, CancellationToken token)
	{
		bool rebuiltAfterJournalReset = false;
		try
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					await new UsnJournalWatcher(drive, source.Index).RunAsync(startingCheckpoint.NextUsn, (string status) =>
					{
						Dispatcher.BeginInvoke((Action)(() =>
						{
							if (_sources.ContainsKey(source.Root))
							{
								StatusText.Text = LocalizationService.Get("VolumeStatus", source.Root, status);
							}
						}));
					}, () =>
					{
						Dispatcher.BeginInvoke((Action)(() =>
						{
							RefreshResultsAsync();
						}));
					}, token, startingCheckpoint.JournalId, (long cursor) =>
					{
						source.LastUsn = cursor;
						if (!(DateTime.UtcNow - source.LastCacheSaveUtc < TimeSpan.FromSeconds(30L)) && Interlocked.CompareExchange(ref source.CacheSaveRunning, 1, 0) == 0)
						{
							source.LastCacheSaveUtc = DateTime.UtcNow;
							Task.Run(() =>
							{
								try
								{
									IndexCacheStore.Save(drive, source.JournalId, cursor, source.Index);
								}
								catch (Exception ex7) when ((ex7 is IOException || ex7 is UnauthorizedAccessException) ? true : false)
								{
								}
								finally
								{
									Interlocked.Exchange(ref source.CacheSaveRunning, 0);
								}
							});
						}
					});
					break;
				}
				catch (InvalidOperationException) when (!rebuiltAfterJournalReset && !token.IsCancellationRequested)
				{
					rebuiltAfterJournalReset = true;
					await Dispatcher.InvokeAsync(() =>
					{
						source.IsIndexing = true;
						source.Status = LocalizationService.Get("MftPreparing", source.Root);
						UpdateSourceRow(source);
					});
					source.Index.Clear();
					NtfsVolumeReader reader = new NtfsVolumeReader(drive);
					JournalCheckpoint journalCheckpoint = await Task.Run((Func<JournalCheckpoint>)reader.GetJournalCheckpoint, token);
					startingCheckpoint = journalCheckpoint;
					source.JournalId = startingCheckpoint.JournalId;
					source.LastUsn = startingCheckpoint.NextUsn;
					await Task.Run(async () =>
					{
						await foreach (IndexedEntry item in reader.EnumerateAsync(token, () => _settings.ScanPerformance))
						{
							source.Index.Upsert(item);
						}
					}, token);
					try
					{
						await Task.Run(() =>
						{
							IndexCacheStore.Save(drive, startingCheckpoint.JournalId, startingCheckpoint.NextUsn, source.Index);
						}, token);
					}
					catch (Exception ex2) when ((ex2 is IOException || ex2 is UnauthorizedAccessException) ? true : false)
					{
					}
					source.LastCacheSaveUtc = DateTime.UtcNow;
					await Dispatcher.InvokeAsync(() =>
					{
						source.IsIndexing = false;
						source.Status = LocalizationService.Get("MftUsn");
						UpdateSourceRow(source);
						RefreshResultsAsync();
					});
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex4)
		{
			Exception ex5 = ex4;
			Exception ex6 = ex5;
			Dispatcher.BeginInvoke((Action)(() =>
			{
				if (_sources.ContainsKey(source.Root))
				{
					source.Status = LocalizationService.Get("WatcherError");
					UpdateSourceRow(source);
					StatusText.Text = LocalizationService.Get("WatcherStopped", source.Root, ex6.Message);
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
		_sourceRows.Add(new SourceRow(source.Root, source.Root + " · " + LocalizationService.Get("Preparing")));
		_sourceFilterOptions.Add(new SourceFilterOption(source.Root, source.Root));
		SourcesList.SelectedItem = _sourceRows.Last();
		CountText.Text = $"{_sources.Count} kaynak";
	}

	private void UpdateSourceRow(SearchSource source)
	{
		SourceRow sourceRow = _sourceRows.FirstOrDefault((SourceRow x) => x.Root.Equals(source.Root, StringComparison.OrdinalIgnoreCase));
		if ((object)sourceRow != null)
		{
			int index = _sourceRows.IndexOf(sourceRow);
			bool flag = SourcesList.SelectedItems.Contains(sourceRow);
			SourceRow value = new SourceRow(source.Root, source.Root + " · " + source.Status);
			_sourceRows[index] = value;
			if (flag)
			{
				SourcesList.SelectedItems.Add(value);
			}
		}
		CountText.Text = LocalizationService.Get("RecordCount", source.Index.TotalCount, _sources.Count);
		UpdateSourceActionButtons();
	}

	private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		UpdateSourceActionButtons();
	}

	private void UpdateSourceActionButtons()
	{
		SourceRow[] array = SourcesList.SelectedItems.OfType<SourceRow>().ToArray();
		RemoveSourceButton.IsEnabled = array.Length != 0;
		RefreshSourceButton.IsEnabled = array.Any((SourceRow row) => _sources.TryGetValue(row.Root, out SearchSource value) && !value.IsIndexing);
	}

	private async void RefreshSources_Click(object sender, RoutedEventArgs e)
	{
		await RefreshSelectedSourcesAsync();
	}

	private async Task RefreshSelectedSourcesAsync()
	{
		SearchSource[] array = (from row in SourcesList.SelectedItems.OfType<SourceRow>()
			select _sources.GetValueOrDefault(row.Root) into searchSource
			where searchSource != null && !searchSource.IsIndexing
			select searchSource).Cast<SearchSource>().ToArray();
		SearchSource[] array2 = array;
		foreach (SearchSource source in array2)
		{
			source.IsIndexing = true;
			source.Status = LocalizationService.Get("Preparing");
			UpdateSourceRow(source);
			UpdateSourceActionButtons();
			try
			{
				if (source.FolderSession != null)
				{
					await source.FolderSession.RefreshAsync(source.Cancellation?.Token ?? CancellationToken.None);
				}
				else
				{
					source.JournalCancellation?.Cancel();
					if (source.JournalTask != null)
					{
						try
						{
							await source.JournalTask;
						}
						catch (OperationCanceledException)
						{
						}
					}
					source.JournalCancellation?.Dispose();
					source.JournalCancellation = null;
					source.Cancellation?.Cancel();
					source.Cancellation?.Dispose();
					source.Cancellation = new CancellationTokenSource();
					await RefreshNtfsIndexAsync(source, useCache: false);
				}
				if (source.FolderSession != null)
				{
					source.IsIndexing = false;
					source.Status = (source.FolderSession.IsNetworkAvailable ? LocalizationService.Get(source.FolderSession.IsWatching ? "LiveWatch" : "LiveWatchUnavailable") : LocalizationService.Get("NetworkDisconnected"));
					UpdateSourceRow(source);
				}
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception ex3)
			{
				source.IsIndexing = false;
				source.Status = LocalizationService.Get("Error");
				UpdateSourceRow(source);
				StatusText.Text = LocalizationService.Get("FolderRootScanFailed", source.Root, ex3.Message);
			}
		}
		UpdateSourceActionButtons();
		await RefreshResultsAsync();
	}

	private async void RemoveSource_Click(object sender, RoutedEventArgs e)
	{
		SourceRow[] rows = SourcesList.SelectedItems.OfType<SourceRow>().ToArray();
		if (rows.Length == 0)
		{
			return;
		}
		string messageBoxText = ((rows.Length == 1) ? LocalizationService.Get("ConfirmRemove", rows[0].Root) : LocalizationService.Get("ConfirmRemoveMultiple", string.Join(Environment.NewLine, rows.Select((SourceRow sourceRow) => "• " + sourceRow.Root))));
		if (System.Windows.MessageBox.Show(this, messageBoxText, LocalizationService.Get("ConfirmRemoveTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		SourceRow[] array = rows;
		foreach (SourceRow row in array)
		{
			if (!_sources.Remove(row.Root, out SearchSource source))
			{
				continue;
			}
			_settings.SearchRoots.RemoveAll((string path) => path.Equals(row.Root, StringComparison.OrdinalIgnoreCase));
			source.Cancellation?.Cancel();
			source.JournalCancellation?.Cancel();
			if (source.JournalTask != null)
			{
				try
				{
					await source.JournalTask;
				}
				catch (OperationCanceledException)
				{
				}
			}
			while (Volatile.Read(in source.CacheSaveRunning) != 0)
			{
				await Task.Delay(25);
			}
			if (source.FolderSession is { } folderSession)
            {
                try
                {
                    folderSession.Dispose(deleteCache: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    StatusText.Text = LocalizationService.Get("SourceSaveFailed", ex.Message);
                }
            }
			source.Cancellation?.Dispose();
			source.JournalCancellation?.Dispose();
			if (source.Kind == "NTFS volume")
			{
				try
				{
					IndexCacheStore.Delete(source.Root[0]);
				}
				catch (Exception ex2) when ((ex2 is IOException || ex2 is UnauthorizedAccessException) ? true : false)
				{
					StatusText.Text = LocalizationService.Get("SourceSaveFailed", ex2.Message);
				}
			}
			_sourceRows.Remove(row);
			SourceFilterOption sourceFilterOption = _sourceFilterOptions.FirstOrDefault((SourceFilterOption x) => x.Root?.Equals(row.Root, StringComparison.OrdinalIgnoreCase) ?? false);
			if ((object)sourceFilterOption != null)
			{
				_sourceFilterOptions.Remove(sourceFilterOption);
			}
			if (SourceFilterBox.SelectedValue is string text && text.Equals(row.Root, StringComparison.OrdinalIgnoreCase))
			{
				SourceFilterBox.SelectedIndex = 0;
			}
			source = null;
		}
		PersistSettings();
		_results.Clear();
		CountText.Text = $"{_sources.Count} kaynak";
		StatusText.Text = ((rows.Length == 1) ? LocalizationService.Get("SourceRemoved", rows[0].Root) : LocalizationService.Get("SourcesRemoved", rows.Length));
		await RefreshResultsAsync();
	}

	private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		await RefreshResultsAsync();
	}

	private async void Filter_Changed(object sender, RoutedEventArgs e)
	{
		await RefreshResultsAsync();
	}

	private async void SavedSearchBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		RemoveSavedSearchButton.IsEnabled = SavedSearchBox.SelectedItem is SavedSearch;
		if (!_loadingSavedSearches && SavedSearchBox.SelectedItem is SavedSearch savedSearch)
		{
			SearchBox.Text = savedSearch.Query;
			SearchBox.Focus();
			SearchBox.CaretIndex = SearchBox.Text.Length;
			await RefreshResultsAsync();
		}
	}

	private void RemoveSavedSearch_Click(object sender, RoutedEventArgs e)
	{
		object selectedItem = SavedSearchBox.SelectedItem;
		SavedSearch saved = selectedItem as SavedSearch;
		if ((object)saved != null && System.Windows.MessageBox.Show(this, LocalizationService.Get("ConfirmRemoveSavedSearch", saved.Name), LocalizationService.Get("RemoveSavedSearch"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			_settings.SavedSearches.RemoveAll((SavedSearch item) => item.Name.Equals(saved.Name, StringComparison.OrdinalIgnoreCase));
			PersistSettings();
			LoadSavedSearchRows();
		}
	}

	private void SaveSearch_Click(object sender, RoutedEventArgs e)
	{
		string text = SearchBox.Text.Trim();
		if (text.Length == 0)
		{
			return;
		}
		TextInputDialog textInputDialog = new TextInputDialog(LocalizationService.Get("SaveSearchPrompt"), text)
		{
			Owner = this
		};
		if (textInputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textInputDialog.Value))
		{
			string name = textInputDialog.Value.Trim();
			_settings.SavedSearches.RemoveAll((SavedSearch item) => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
			_settings.SavedSearches.Insert(0, new SavedSearch(name, text));
			_settings.SavedSearches = _settings.SavedSearches.Take(100).ToList();
			PersistSettings();
			LoadSavedSearchRows();
			SavedSearchBox.SelectedItem = _savedSearchRows.FirstOrDefault((SavedSearch item) => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
		}
	}

	private void ExtensionFilterToggle_Click(object sender, RoutedEventArgs e)
	{
		ExtensionFilterPopup.IsOpen = ExtensionFilterToggle.IsChecked == true;
	}

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
		if (!IsInitialized)
		{
			return;
		}
		CancellationTokenSource searchCancellation = new CancellationTokenSource();
		CancellationTokenSource? searchCts = _searchCts;
		_searchCts = searchCancellation;
		searchCts?.Cancel();
		CancellationToken token = searchCancellation.Token;
		string query = SearchBox.Text.Trim();
		if (_sources.Count == 0 || query.Length == 0)
		{
			_results.Clear();
			SearchProgressBar.Visibility = Visibility.Collapsed;
			UpdateEmptyState(query);
			CountText.Text = LocalizationService.Get("SourceCount", _sources.Count);
			if (_searchCts == searchCancellation)
			{
				_searchCts = null;
			}
			searchCancellation.Dispose();
			return;
		}
		UpdateEmptyState(query, searching: true);
		try
		{
			await Task.Delay(Math.Clamp(_settings.SearchDebounceMs, 0, 1000), token);
			SearchSource[] source = _sources.Values.ToArray();
			object selectedValue = SourceFilterBox.SelectedValue;
			string selectedRoot = selectedValue as string;
			if (selectedRoot != null)
			{
				source = source.Where((SearchSource x) => x.Root.Equals(selectedRoot, StringComparison.OrdinalIgnoreCase)).ToArray();
			}
			bool? flag2;
			if (TypeFilterBox.SelectedItem is ComboBoxItem { Tag: var tag })
			{
				string text = tag?.ToString();
				bool? flag;
				if (text == "Files")
				{
					flag = false;
				}
				else
				{
					flag = ((!(text == "Folders")) ? ((bool?)null) : new bool?(true));
				}
				flag2 = flag;
			}
			else
			{
				flag2 = null;
			}
			bool? directoriesOnly = flag2;
			IReadOnlySet<string> extensions = GetSelectedExtensions();
			bool showHiddenFolders = HiddenFoldersCheckBox.IsChecked == true;
			SearchQueryTerms parsedQuery = SearchQueryTerms.Parse(query);
			int num = (int.TryParse((ModifiedFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var result) ? result : 0);
			DateTime? modifiedAfterUtc = ((num > 0) ? new DateTime?(DateTime.UtcNow.AddDays(-num)) : ((DateTime?)null));
			int num2 = (int.TryParse((SizeFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var result2) ? result2 : 0);
			long minimumSizeBytes = (long)num2 * 1024L * 1024;
			string[] excludedFolders = _settings.ExcludedFolders.ToArray();
			IReadOnlyList<SearchResult>[] source2 = await Task.WhenAll(source.Select((SearchSource searchSource) => searchSource.Index.SearchAsync(query, _settings.MaxResults, token, (searchSource.Kind == "NTFS volume") ? searchSource.Root : null, directoriesOnly, extensions, showHiddenFolders, parsedQuery, modifiedAfterUtc, minimumSizeBytes, long.MaxValue, excludedFolders)));
			if (token.IsCancellationRequested)
			{
				return;
			}
			IEnumerable<SearchResult> source3 = source2.SelectMany((IReadOnlyList<SearchResult> x) => x).DistinctBy((SearchResult x) => x.Path, StringComparer.OrdinalIgnoreCase);
			string text2 = (SortBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Name";
			IOrderedEnumerable<SearchResult> orderedEnumerable;
			if (text2 == "Path")
			{
				orderedEnumerable = source3.OrderBy((SearchResult x) => x.Path, StringComparer.OrdinalIgnoreCase).ThenBy((SearchResult x) => x.Name, StringComparer.OrdinalIgnoreCase);
			}
			else
			{
				orderedEnumerable = ((!(text2 == "Type")) ? source3.OrderBy((SearchResult x) => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy((SearchResult x) => x.Path, StringComparer.OrdinalIgnoreCase) : source3.OrderBy((SearchResult x) => (!x.IsDirectory) ? 1 : 0).ThenBy((SearchResult x) => x.Name, StringComparer.OrdinalIgnoreCase));
			}
			source3 = orderedEnumerable;
			_results.Clear();
			foreach (SearchResult item in source3.Take(_settings.MaxResults))
			{
				_results.Add(new ResultRow(item.Name, item.Path, item.IsDirectory, LocalizationService.Get(item.IsDirectory ? "FolderType" : "FileType")));
			}
			CountText.Text = LocalizationService.Get("ResultCount", _results.Count, _sources.Count);
			UpdateEmptyState(query);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (_searchCts == searchCancellation)
			{
				_searchCts = null;
				SearchProgressBar.Visibility = Visibility.Collapsed;
				UpdateEmptyState(query);
			}
			searchCancellation.Dispose();
		}
	}

	private void UpdateEmptyState(string? query = null, bool searching = false)
	{
		if (query == null)
		{
			query = SearchBox.Text.Trim();
		}
		bool flag = searching || _results.Count == 0;
		EmptyStatePanel.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		SearchProgressBar.Visibility = ((!searching) ? Visibility.Collapsed : Visibility.Visible);
		if (searching)
		{
			EmptyStateTitle.Text = LocalizationService.Get("SearchingTitle");
			EmptyStateDescription.Text = LocalizationService.Get("SearchingDescription");
		}
		else if (_sources.Count == 0)
		{
			EmptyStateTitle.Text = LocalizationService.Get("EmptyNoSourcesTitle");
			EmptyStateDescription.Text = LocalizationService.Get("EmptyNoSourcesDescription");
		}
		else if (query.Length == 0)
		{
			EmptyStateTitle.Text = LocalizationService.Get("EmptySearchTitle");
			EmptyStateDescription.Text = LocalizationService.Get("EmptySearchDescription");
		}
		else if (_sources.Values.Any((SearchSource source) => source.IsIndexing))
		{
			EmptyStateTitle.Text = LocalizationService.Get("IndexingTitle");
			EmptyStateDescription.Text = LocalizationService.Get("IndexingDescription");
			SearchProgressBar.Visibility = Visibility.Visible;
		}
		else
		{
			EmptyStateTitle.Text = LocalizationService.Get("EmptyNoResultsTitle");
			EmptyStateDescription.Text = LocalizationService.Get("EmptyNoResultsDescription");
		}
	}

	private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.L)
		{
			SearchBox.Focus();
			SearchBox.SelectAll();
			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			System.Windows.Controls.ComboBox comboBox = FindOpenComboBox(this);
			if (comboBox != null)
			{
				comboBox.IsDropDownOpen = false;
				e.Handled = true;
			}
			else if (ExtensionFilterPopup.IsOpen)
			{
				ExtensionFilterPopup.IsOpen = false;
				ExtensionFilterToggle.IsChecked = false;
				e.Handled = true;
			}
			else if (SearchBox.Text.Length > 0)
			{
				SearchBox.Clear();
				SearchBox.Focus();
				e.Handled = true;
			}
		}
		else if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && e.Key == Key.A && ResultsList.IsKeyboardFocusWithin)
		{
			ResultsList.SelectAll();
			e.Handled = true;
		}
		else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C &&
            ResultsList.IsKeyboardFocusWithin && ResultsList.SelectedItems.Count > 0)
        {
            CopyResultItems(ResultsList.SelectedItems.OfType<ResultRow>());
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C &&
            ResultsList.IsKeyboardFocusWithin && ResultsList.SelectedItems.Count > 0)
        {
            CopyPaths(ResultsList.SelectedItems.OfType<ResultRow>());
            e.Handled = true;
        }
		else if (e.Key == Key.F5 && SourcesList.SelectedItems.Count > 0)
		{
			RefreshSelectedSourcesAsync();
			e.Handled = true;
		}
		else if (e.Key == Key.Return && ResultsList.IsKeyboardFocusWithin && ResultsList.SelectedItem is ResultRow row)
		{
			OpenResult(row);
			e.Handled = true;
		}
	}

	private static System.Windows.Controls.ComboBox? FindOpenComboBox(DependencyObject parent)
	{
		int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is System.Windows.Controls.ComboBox { IsDropDownOpen: not false } comboBox)
			{
				return comboBox;
			}
			System.Windows.Controls.ComboBox comboBox2 = FindOpenComboBox(child);
			if (comboBox2 != null)
			{
				return comboBox2;
			}
		}
		return null;
	}

	private static IReadOnlySet<string>? ParseExtensions(string text)
	{
		HashSet<string> hashSet = (from value in text.Split(new char[6] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			select (!value.StartsWith('*')) ? value : value.Substring(1) into value
			select (!value.StartsWith('.')) ? ("." + value) : value into value
			where value.Length > 1
			select value).ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (hashSet.Count != 0)
		{
			return hashSet;
		}
		return null;
	}

	private IReadOnlySet<string>? GetSelectedExtensions()
	{
		HashSet<string> hashSet = (from option in ExtensionOptionsPanel.Children.OfType<System.Windows.Controls.CheckBox>()
			where option.IsChecked == true
			select option.Tag?.ToString() into extension
			where !string.IsNullOrWhiteSpace(extension)
			select extension).Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
		hashSet.UnionWith(ParseExtensions(ExtensionFilterBox.Text) ?? new HashSet<string>());
		if (hashSet.Count != 0)
		{
			return hashSet;
		}
		return null;
	}

	private static ResultRow? GetContextMenuResult(object sender)
	{
		return (sender as System.Windows.Controls.MenuItem)?.DataContext as ResultRow;
	}

	private void ResultContextMenu_Opened(object sender, RoutedEventArgs e)
	{
		if (!(sender is System.Windows.Controls.ContextMenu contextMenu))
		{
			return;
		}
		foreach (System.Windows.Controls.MenuItem item in contextMenu.Items.OfType<System.Windows.Controls.MenuItem>())
		{
			System.Windows.Controls.MenuItem menuItem = item;
			menuItem.Header = item.Tag?.ToString() switch
			{
				"CopyItem" => LocalizationService.Get("CopyItem"),
                "CopyPath" => LocalizationService.Get("CopyPath"),
				"OpenLocation" => LocalizationService.Get("OpenLocation"),
				"Open" => LocalizationService.Get("Open"),
				_ => item.Header,
			};
		}
	}

	private void CopyResultItem_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuResult(sender) is ResultRow row)
            CopyResultItems(new[] { row });
    }

    private void CopyResultItems(IEnumerable<ResultRow> rows)
    {
        try
        {
            var paths = new System.Collections.Specialized.StringCollection();
            foreach (var row in rows)
                paths.Add(row.Path);

            var data = new System.Windows.DataObject();
            data.SetFileDropList(paths);
            // Windows Explorer uses this DWORD to select copy rather than move.
            using var copyEffect = new MemoryStream(BitConverter.GetBytes(1));
            data.SetData("Preferred DropEffect", copyEffect);
            System.Windows.Clipboard.SetDataObject(data, copy: true);
            StatusText.Text = LocalizationService.Get("CopyItemSuccess");
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Get("CopyItemFailed", ex.Message);
        }
    }

    private void CopyResultPath_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuResult(sender) is ResultRow row)
            CopyPaths(new[] { row });
    }

    private void CopyPaths(IEnumerable<ResultRow> rows)
    {
        try
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, rows.Select(row => row.Path)));
            StatusText.Text = LocalizationService.Get("CopySuccess");
        }
        catch (Exception ex)
        {
            StatusText.Text = LocalizationService.Get("CopyFailed", ex.Message);
        }
    }

    private void RevealResult_Click(object sender, RoutedEventArgs e)
	{
		ResultRow contextMenuResult = GetContextMenuResult(sender);
		if ((object)contextMenuResult == null)
		{
			return;
		}
		try
		{
			if (Directory.Exists(contextMenuResult.Path))
			{
				Process.Start("explorer.exe", "\"" + contextMenuResult.Path + "\"");
			}
			else if (File.Exists(contextMenuResult.Path))
			{
				Process.Start("explorer.exe", "/select,\"" + contextMenuResult.Path + "\"");
			}
			else
			{
				StatusText.Text = LocalizationService.Get("MissingItem");
			}
		}
		catch (Exception ex)
		{
			StatusText.Text = LocalizationService.Get("LocationOpenFailed", ex.Message);
		}
	}

	private void OpenResult_Click(object sender, RoutedEventArgs e)
	{
		ResultRow contextMenuResult = GetContextMenuResult(sender);
		if ((object)contextMenuResult != null)
		{
			OpenResult(contextMenuResult);
		}
	}

	private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (ResultsList.SelectedItem is ResultRow row)
		{
			OpenResult(row);
		}
	}

	private void OpenResult(ResultRow row)
	{
		try
		{
			if (Directory.Exists(row.Path))
			{
				Process.Start("explorer.exe", "\"" + row.Path + "\"");
			}
			else if (File.Exists(row.Path))
			{
				Process.Start(new ProcessStartInfo(row.Path)
				{
					UseShellExecute = true
				});
			}
			else
			{
				StatusText.Text = LocalizationService.Get("MissingItem");
			}
		}
		catch (Exception ex)
		{
			StatusText.Text = LocalizationService.Get("ItemOpenFailed", ex.Message);
		}
	}

	protected override void OnClosed(EventArgs e)
	{
		_searchCts?.Cancel();
		_searchCts = null;
		foreach (SearchSource value in _sources.Values)
		{
			value.Cancellation?.Cancel();
			value.JournalCancellation?.Cancel();
			value.FolderSession?.Dispose();
		}
		if (_trayIcon != null)
		{
			_trayIcon.Visible = false;
			_trayIcon.Dispose();
			_trayIcon = null;
		}
		base.OnClosed(e);
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!_allowClose && IsInstallerRunning())
		{
			_allowClose = true;
		}
		if (!_allowClose)
		{
			e.Cancel = true;
			Hide();
			_trayIcon?.ShowBalloonTip(1800, LocalizationService.Get("AppName"), LocalizationService.Get("TrayBalloon"), ToolTipIcon.Info);
		}
		else
		{
			base.OnClosing(e);
		}
	}

	private static bool IsInstallerRunning()
	{
		try
		{
			Process[] processes = Process.GetProcesses();
			foreach (Process process in processes)
			{
				using (process)
				{
					if (process.ProcessName.StartsWith("AllIsWhere-Setup", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private void PersistSettings()
	{
		try
		{
			SettingsStore.Save(_settings);
		}
		catch (Exception ex)
		{
			StatusText.Text = LocalizationService.Get("SourceSaveFailed", ex.Message);
		}
	}
}
