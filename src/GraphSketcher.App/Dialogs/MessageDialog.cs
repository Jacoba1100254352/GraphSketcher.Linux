using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace GraphSketcher.App.Dialogs;

internal sealed class MessageDialog : Window
{
    private readonly bool _isConfirmation;

    private MessageDialog(string title, string message, bool isConfirmation)
    {
        _isConfirmation = isConfirmation;
        Title = title;
        Width = 460;
        MinHeight = 190;
        MaxHeight = 420;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(
            AssetLoader.Open(new Uri("avares://GraphSketcher/Assets/GraphSketcher.png")));

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var primaryButton = new Button
        {
            Content = isConfirmation ? "Continue" : "OK",
            MinWidth = 90,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Classes = { "accent" },
        };
        primaryButton.Click += (_, _) => Close(true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        if (isConfirmation)
        {
            var cancelButton = new Button
            {
                Content = "Cancel",
                MinWidth = 90,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            cancelButton.Click += (_, _) => Close(false);
            buttons.Children.Add(cancelButton);
        }

        buttons.Children.Add(primaryButton);

        Content = new Grid
        {
            RowDefinitions = RowDefinitions.Parse("*,Auto"),
            Margin = new Thickness(22),
            RowSpacing = 20,
            Children =
            {
                messageBlock,
                buttons,
            },
        };
        Grid.SetRow(buttons, 1);

        KeyDown += OnKeyDown;
    }

    public static Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        bool isConfirmation = false)
    {
        var dialog = new MessageDialog(title, message, isConfirmation);
        return dialog.ShowDialog<bool>(owner);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(!_isConfirmation);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Close(true);
            e.Handled = true;
        }
    }
}
