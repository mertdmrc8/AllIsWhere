using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using AllIsWhere.Services;
using Microsoft.Win32;

namespace AllIsWhere;

public partial class AddSourceWindow : Window, IComponentConnector
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
		OpenFolderDialog openFolderDialog = new OpenFolderDialog
		{
			Title = LocalizationService.Get("FolderPickerTitle"),
			Multiselect = false,
			InitialDirectory = FindBrowseStart(PathBox.Text)
		};
		if (openFolderDialog.ShowDialog(this) == true)
		{
			PathBox.Text = openFolderDialog.FolderName;
		}
	}

	private static string? FindBrowseStart(string input)
	{
		string text = input.Trim().Trim('"');
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		try
		{
			text = Path.GetFullPath(Environment.ExpandEnvironmentVariables(text));
		}
		catch
		{
			return null;
		}
		while (!string.IsNullOrWhiteSpace(text))
		{
			try
			{
				if (Directory.Exists(text))
				{
					return text;
				}
				text = Directory.GetParent(text)?.FullName;
			}
			catch
			{
				return null;
			}
		}
		return null;
	}

	private void Add_Click(object sender, RoutedEventArgs e)
	{
		if (string.IsNullOrWhiteSpace(PathBox.Text))
		{
			MessageBox.Show(this, LocalizationService.Get("PathEmpty"), LocalizationService.Get("PathEmptyTitle"), MessageBoxButton.OK, MessageBoxImage.Asterisk);
			PathBox.Focus();
		}
		else
		{
			DialogResult = true;
		}
	}
}
