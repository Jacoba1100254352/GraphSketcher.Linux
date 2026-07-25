using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace GraphSketcher.App;

public sealed partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;

            var smokeTest = desktop.Args?.Any(argument =>
                string.Equals(argument, "--smoke-test", StringComparison.OrdinalIgnoreCase)) == true;
            if (smokeTest)
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                mainWindow.ShowInTaskbar = false;
                mainWindow.Opened += (_, _) =>
                    Dispatcher.UIThread.Post(
                        () => desktop.Shutdown(0),
                        DispatcherPriority.ApplicationIdle);
            }

            var startupPath = desktop.Args?
                .FirstOrDefault(argument =>
                {
                    var extension = Path.GetExtension(argument);
                    return File.Exists(argument) &&
                           (string.Equals(extension, ".graphsketch", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(extension, ".ograph", StringComparison.OrdinalIgnoreCase));
                });
            if (startupPath is not null)
            {
                mainWindow.Opened += async (_, _) =>
                    await mainWindow.OpenDocumentPathAsync(startupPath);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
