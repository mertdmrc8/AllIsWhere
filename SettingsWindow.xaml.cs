using System.Windows;
using System.Windows.Controls;
using AllIsWhere.Models;
using AllIsWhere.Services;

namespace AllIsWhere;

public partial class SettingsWindow : Window
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
            ScanPerformance = current.ScanPerformance
        };
        SelectTag(ThemeBox, Settings.Theme);
        SelectTag(FontSizeBox, Settings.ResultFontSize.ToString("0"));
        SelectTag(MaxResultsBox, Settings.MaxResults.ToString());
        SelectTag(DebounceBox, Settings.SearchDebounceMs.ToString());
        StartWithWindowsBox.IsChecked = Settings.StartWithWindows;
        SelectTag(ScanPerformanceBox, Settings.ScanPerformance);
        SelectTag(LanguageBox, Settings.Language);
        ApplyLanguage();
    }

    private void ApplyLanguage()
    {
        Title = LocalizationService.Get("Settings");
        SettingsTitle.Text = LocalizationService.Get("SettingsTitle");
        SettingsDescription.Text = LocalizationService.Get("SettingsDescription");
        ThemeLabel.Text = LocalizationService.Get("Theme");
        ThemeDescription.Text = LocalizationService.Get("ThemeDescription");
        LightThemeItem.Content = LocalizationService.Get("Light");
        DarkThemeItem.Content = LocalizationService.Get("Dark");
        FontSizeLabel.Text = LocalizationService.Get("ResultFont");
        SmallFontItem.Content = $"{LocalizationService.Get("Small")} · 12";
        NormalFontItem.Content = $"{LocalizationService.Get("Normal")} · 14";
        LargeFontItem.Content = $"{LocalizationService.Get("Large")} · 16";
        ExtraLargeFontItem.Content = $"{LocalizationService.Get("ExtraLarge")} · 18";
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
        CancelButton.Content = LocalizationService.Get("Cancel");
        SaveButton.Content = LocalizationService.Get("Save");
    }

    private static void SelectTag(ComboBox box, string value)
    {
        foreach (ComboBoxItem item in box.Items)
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
            Language = ((ComboBoxItem)LanguageBox.SelectedItem).Tag?.ToString() ?? "tr",
            ShowHiddenFolders = Settings.ShowHiddenFolders,
            Theme = ((ComboBoxItem)ThemeBox.SelectedItem).Tag?.ToString() ?? "Light",
            ResultFontSize = double.Parse(((ComboBoxItem)FontSizeBox.SelectedItem).Tag!.ToString()!),
            MaxResults = int.Parse(((ComboBoxItem)MaxResultsBox.SelectedItem).Tag!.ToString()!),
            SearchDebounceMs = int.Parse(((ComboBoxItem)DebounceBox.SelectedItem).Tag!.ToString()!),
            StartWithWindows = StartWithWindowsBox.IsChecked == true,
            ScanPerformance = ((ComboBoxItem)ScanPerformanceBox.SelectedItem).Tag?.ToString() ?? "Balanced"
        };
        DialogResult = true;
    }
}
