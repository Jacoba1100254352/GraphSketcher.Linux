using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace GraphSketcher.App.Dialogs;

internal sealed class DataImportDialog : Window
{
    private readonly TextBox _dataBox;

    public DataImportDialog(string? initialText = null)
    {
        Title = "Import spreadsheet data";
        Width = 720;
        Height = 560;
        MinWidth = 560;
        MinHeight = 420;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _dataBox = new TextBox
        {
            Text = initialText ?? string.Empty,
            AcceptsReturn = true,
            AcceptsTab = true,
            FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
            PlaceholderText = "Paste CSV, TSV, or rows copied from Excel here…",
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_dataBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_dataBox, ScrollBarVisibility.Auto);

        var sampleButton = new Button
        {
            Content = "Use sample data",
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        sampleButton.Click += (_, _) =>
        {
            _dataBox.Text =
                "Time (s)\tExperiment\tReference\n" +
                "0\t1.2\t1.0\n" +
                "1\t2.4\t2.1\n" +
                "2\t4.1\t3.8\n" +
                "3\t5.7\t5.3\n" +
                "4\t8.2\t7.1";
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cancelButton.Click += (_, _) => Close(null);

        var importButton = new Button
        {
            Content = "Import",
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = true,
            Classes = { "accent" },
        };
        importButton.Click += (_, _) => Submit();

        var buttons = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto,Auto"),
            ColumnSpacing = 8,
            Children = { sampleButton, cancelButton, importButton },
        };
        Grid.SetColumn(cancelButton, 2);
        Grid.SetColumn(importButton, 3);

        Content = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto"),
            RowSpacing = 10,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = "Paste data from Excel or enter CSV/TSV text",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold,
                },
                new TextBlock
                {
                    Text = "The first numeric column becomes X. Each later numeric column becomes a series. Column headers become axis and series names.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Gray,
                },
                _dataBox,
                buttons,
            },
        };
        Grid.SetRow(((Grid)Content).Children[1], 1);
        Grid.SetRow(_dataBox, 2);
        Grid.SetRow(buttons, 3);

        Opened += (_, _) => _dataBox.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Submit();
                e.Handled = true;
            }
        };
    }

    private void Submit()
    {
        var text = _dataBox.Text;
        Close(string.IsNullOrWhiteSpace(text) ? null : text);
    }
}
