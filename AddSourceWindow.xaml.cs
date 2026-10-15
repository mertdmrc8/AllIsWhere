using System.IO;
using System.Windows;
using AllIsWhere.Services;
using Microsoft.Win32;

namespace AllIsWhere;

/// <summary>Collects a local folder, mapped drive, or UNC path.</summary>
public partial class AddSourceWindow : Window
{
    public string SelectedPath => PathBox.Text.Trim();

    public AddSourceWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        PathBox.Focus();
    }

    private void ApplyLanguage()
    {
        Title = LocalizationService.Get("AddSourceTitle");
        DialogTitle.Text = LocalizationService.Get("AddSourceTitle");
        DialogDescription.Text = LocalizationService.Get("AddSourceDescription");
        BrowseButton.Content = LocalizationService.Get("Browse");
        PathBox.ToolTip = LocalizationService.Get("FolderPlaceholder");
        PathExample.Text = LocalizationService.Get("PathHint");
        CancelButton.Content = LocalizationService.Get("Cancel");
        AddButton.Content = LocalizationService.Get("AddSource");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = LocalizationService.Get("FolderPickerTitle"),
            Multiselect = false,
            InitialDirectory = FindBrowseStart(PathBox.Text)
        };

        if (picker.ShowDialog(this) == true)
            PathBox.Text = picker.FolderName;
    }

    /// <summary>Starts at the entered folder, or at its nearest accessible parent.</summary>
    private static string? FindBrowseStart(string input)
    {
        string? candidate = input.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        try { candidate = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate)); }
        catch { return null; }

        while (!string.IsNullOrWhiteSpace(candidate))
        {
            try
            {
                if (Directory.Exists(candidate)) return candidate;
                candidate = Directory.GetParent(candidate)?.FullName;
            }
            catch { return null; }
        }
        return null;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathBox.Text))
        {
            MessageBox.Show(this, LocalizationService.Get("PathEmpty"),
                LocalizationService.Get("PathEmptyTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            PathBox.Focus();
            return;
        }

        // The main window validates access and displays any path error.
        DialogResult = true;
    }
}
