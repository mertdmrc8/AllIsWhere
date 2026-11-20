using System.Windows;
using System.Windows.Controls;
using AllIsWhere.Services;

namespace AllIsWhere;

internal sealed class TextInputDialog : Window
{
	private readonly TextBox _valueBox;

	public string Value => _valueBox.Text;

	public TextInputDialog(string prompt, string initialValue)
	{
		Title = "AllIsWhere";
		Width = 420.0;
		SizeToContent = SizeToContent.Height;
		MinHeight = 170.0;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterOwner;
		ShowInTaskbar = false;
		Grid grid = new Grid
		{
			Margin = new Thickness(20.0)
		};
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		TextBlock element = new TextBlock
		{
			Text = prompt,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		};
		_valueBox = new TextBox
		{
			Text = initialValue,
			MinHeight = 34.0,
			Padding = new Thickness(8.0, 5.0, 8.0, 5.0)
		};
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 16.0, 0.0, 0.0)
		};
		Button element2 = new Button
		{
			Content = LocalizationService.Get("Cancel"),
			Width = 92.0,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			IsCancel = true
		};
		Button button = new Button
		{
			Content = LocalizationService.Get("Save"),
			Width = 92.0,
			IsDefault = true
		};
		button.Click += (object _, RoutedEventArgs _) =>
		{
			DialogResult = true;
		};
		stackPanel.Children.Add(element2);
		stackPanel.Children.Add(button);
		Grid.SetRow(_valueBox, 1);
		Grid.SetRow(stackPanel, 2);
		grid.Children.Add(element);
		grid.Children.Add(_valueBox);
		grid.Children.Add(stackPanel);
		Content = grid;
		Loaded += (object _, RoutedEventArgs _) =>
		{
			_valueBox.Focus();
			_valueBox.SelectAll();
		};
	}
}
