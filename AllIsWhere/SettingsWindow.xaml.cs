using System;
using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using AllIsWhere.Models;
using AllIsWhere.Services;

namespace AllIsWhere;

public partial class SettingsWindow : Window, IComponentConnector
{
	public UserSettings Settings { get; private set; }

	public SettingsWindow(UserSettings current)
	{
		InitializeComponent();
		Settings = new UserSettings
		{
			SearchRoots = current.SearchRoots.ToList(),
			Language = current.Language,
			ShowHiddenFolders = current.ShowHiddenFolders,
			Theme = current.Theme,
			ResultFontSize = current.ResultFontSize,
			MaxResults = current.MaxResults,
			SearchDebounceMs = current.SearchDebounceMs,
			StartWithWindows = current.StartWithWindows,
			ScanPerformance = current.ScanPerformance,
			LastUpdateCheckUtc = current.LastUpdateCheckUtc,
			SavedSearches = current.SavedSearches.ToList(),
			ExcludedFolders = current.ExcludedFolders.ToList()
		};
		SelectTag(ThemeBox, Settings.Theme);
		SelectTag(FontSizeBox, Settings.ResultFontSize.ToString("0"));
		SelectTag(MaxResultsBox, Settings.MaxResults.ToString());
		SelectTag(DebounceBox, Settings.SearchDebounceMs.ToString());
		StartWithWindowsBox.IsChecked = Settings.StartWithWindows;
		SelectTag(ScanPerformanceBox, Settings.ScanPerformance);
		SelectTag(LanguageBox, Settings.Language);
		ExcludedFoldersBox.Text = string.Join(Environment.NewLine, Settings.ExcludedFolders);
		ApplyLanguage();
	}

	private void ApplyLanguage()
	{
		Title = LocalizationService.Get("Settings");
		SettingsTitle.Text = LocalizationService.Get("SettingsTitle");
		SettingsDescription.Text = LocalizationService.Get("SettingsDescription");
		AppearanceSectionLabel.Text = LocalizationService.Get("AppearanceSection");
		SearchSectionLabel.Text = LocalizationService.Get("SearchSection");
		StartupSectionLabel.Text = LocalizationService.Get("StartupSection");
		ThemeLabel.Text = LocalizationService.Get("Theme");
		ThemeDescription.Text = LocalizationService.Get("ThemeDescription");
		LightThemeItem.Content = LocalizationService.Get("Light");
		DarkThemeItem.Content = LocalizationService.Get("Dark");
		FontSizeLabel.Text = LocalizationService.Get("ResultFont");
		SmallFontItem.Content = LocalizationService.Get("Small") + " · 12";
		NormalFontItem.Content = LocalizationService.Get("Normal") + " · 14";
		LargeFontItem.Content = LocalizationService.Get("Large") + " · 16";
		ExtraLargeFontItem.Content = LocalizationService.Get("ExtraLarge") + " · 18";
		MaxResultsLabel.Text = LocalizationService.Get("MaxResults");
		MaxResultsDescription.Text = LocalizationService.Get("MaxResultsDescription");
		SearchDelayLabel.Text = LocalizationService.Get("SearchDelay");
		SearchDelayDescription.Text = LocalizationService.Get("SearchDelayDescription");
		InstantDelayItem.Content = LocalizationService.Get("Instant");
		ScanPerformanceLabel.Text = LocalizationService.Get("ScanPerformance");
		ScanPerformanceDescription.Text = LocalizationService.Get("ScanPerformanceDescription");
		FastScanItem.Content = LocalizationService.Get("Fast");
		BalancedScanItem.Content = LocalizationService.Get("Balanced");
		LowResourceItem.Content = LocalizationService.Get("LowResource");
		StartWithWindowsBox.Content = LocalizationService.Get("StartWithWindows");
		LanguageLabel.Text = LocalizationService.Get("Language");
		ExclusionsSectionLabel.Text = LocalizationService.Get("ExcludedFolders");
		ExcludedFoldersDescription.Text = LocalizationService.Get("ExcludedFoldersDescription");
		CancelButton.Content = LocalizationService.Get("Cancel");
		SaveButton.Content = LocalizationService.Get("Save");
	}

	private static void SelectTag(ComboBox box, string value)
	{
		foreach (ComboBoxItem item in (IEnumerable)box.Items)
		{
			if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
			{
				box.SelectedItem = item;
				return;
			}
		}
		box.SelectedIndex = 0;
	}

	private void Save_Click(object sender, RoutedEventArgs e)
	{
		Settings = new UserSettings
		{
			SearchRoots = Settings.SearchRoots.ToList(),
			Language = (((ComboBoxItem)LanguageBox.SelectedItem).Tag?.ToString() ?? "tr"),
			ShowHiddenFolders = Settings.ShowHiddenFolders,
			Theme = (((ComboBoxItem)ThemeBox.SelectedItem).Tag?.ToString() ?? "Light"),
			ResultFontSize = double.TryParse((FontSizeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out double fontSize) ? fontSize : Settings.ResultFontSize,
			MaxResults = int.TryParse((MaxResultsBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int maxResults) ? maxResults : Settings.MaxResults,
			SearchDebounceMs = int.TryParse((DebounceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out int debounce) ? debounce : Settings.SearchDebounceMs,
			StartWithWindows = (StartWithWindowsBox.IsChecked == true),
			ScanPerformance = (((ComboBoxItem)ScanPerformanceBox.SelectedItem).Tag?.ToString() ?? "Balanced"),
			LastUpdateCheckUtc = Settings.LastUpdateCheckUtc,
			SavedSearches = Settings.SavedSearches.ToList(),
			ExcludedFolders = ExcludedFoldersBox.Text.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
		};
		DialogResult = true;
	}
}
