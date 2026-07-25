using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace GraphSketcher.App.Dialogs;

internal sealed class TextPromptDialog : Window
{
    private readonly TextBox _textBox;

    public TextPromptDialog(string title, string prompt, string initialValue = "")
    {
        Title = title;
        Width = 430;
        Height = 205;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _textBox = new TextBox
        {
            Text = initialValue,
            PlaceholderText = "Enter text",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 88,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        cancelButton.Click += (_, _) => Close(null);

        var addButton = new Button
        {
            Content = "Add",
            MinWidth = 88,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = true,
            Classes = { "accent" },
        };
        addButton.Click += (_, _) => Submit();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelButton, addButton },
        };

        var content = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto"),
            RowSpacing = 8,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = prompt },
                _textBox,
                buttons,
            },
        };
        Grid.SetRow(_textBox, 1);
        Grid.SetRow(buttons, 3);
        Content = content;

        Opened += (_, _) =>
        {
            _textBox.Focus();
            _textBox.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                Submit();
                e.Handled = true;
            }
        };
    }

    private void Submit()
    {
        var value = _textBox.Text?.Trim();
        Close(string.IsNullOrWhiteSpace(value) ? null : value);
    }
}
