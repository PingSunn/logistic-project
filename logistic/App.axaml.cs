using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace logistic;

public class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Stay alive while LicenseWindow is the only window; otherwise Close() exits the app.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = Dispatcher.UIThread.InvokeAsync(() => StartAsync(desktop));
        }
    }

    private static async System.Threading.Tasks.Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var result = await LicenseManager.EnforceAsync();
        if (!result.IsOk)
        {
            var activated = await LicenseWindow.ShowAndActivateAsync(result);
            if (!activated)
            {
                desktop.Shutdown(1);
                return;
            }
        }

        ContainerSpec.Load();
        ProductSpec.Load();
        var main = new MainWindow();
        desktop.MainWindow = main;
        // Now that the main window exists, closing it should exit the app.
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
        main.Show();

        LicenseManager.Lost += lost => Dispatcher.UIThread.Post(() => OnLicenseLost(desktop, lost));
        LicenseManager.StartBackgroundHeartbeat();
    }

    private static async void OnLicenseLost(IClassicDesktopStyleApplicationLifetime desktop, LicenseResult lost)
    {
        var recovered = await LicenseWindow.ShowAndActivateAsync(lost);
        if (recovered)
        {
            LicenseManager.StartBackgroundHeartbeat();
            return;
        }
        desktop.Shutdown(1);
    }
}
