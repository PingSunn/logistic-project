using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CargoFit;

public partial class MainWindow : Window
{
    private readonly SettingsWindow _settingsWindow = new();

    public MainWindow()
    {
        InitializeComponent();
        MainContent.Content = new PlanningView();

        LicenseManager.Verified += _ => Dispatcher.UIThread.Post(UpdateTrialBanner);
        UpdateTrialBanner();

        // เช็คอัปเดตใน background หลังแอปโหลดเสร็จ
        _ = CheckForUpdateAsync();
    }

    private async System.Threading.Tasks.Task CheckForUpdateAsync()
    {
        await UpdateService.CheckAsync();
        if (UpdateService.PendingUpdate is { } update)
        {
            UpdateBannerText.Text = $"🆕 มีเวอร์ชันใหม่ {update.TargetFullRelease.Version} พร้อมแล้ว";
            UpdateBanner.IsVisible = true;
        }
    }

    private async void UpdateButton_Click(object? sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        UpdateBannerText.Text = "⏳ กำลังดาวน์โหลด กรุณารอสักครู่...";
        await UpdateService.ApplyUpdateAsync();
        // ApplyUpdatesAndRestart() ไม่ return — แอป restart ทันทีเมื่อสำเร็จ
    }

    private void UpdateTrialBanner()
    {
        var days = LicenseManager.DaysRemaining;
        if (days is null || days > 3)
        {
            TrialBanner.IsVisible = false;
            return;
        }

        TrialBannerText.Text = days == 0
            ? "⚠ เวอร์ชันทดลองหมดอายุวันนี้ — ติดต่อปิงเพื่อต่ออายุ"
            : $"⚠ เวอร์ชันทดลองเหลืออีก {days} วัน — ติดต่อปิงเพื่อต่ออายุ";
        TrialBanner.IsVisible = true;
    }

    private void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        var win = new Window
        {
            Title = "Settings",
            Width = 860,
            Height = 620,
            Content = _settingsWindow
        };
        // Detach on close so the same UserControl can be re-parented next time.
        win.Closed += (_, _) => win.Content = null;
        win.ShowDialog(this);
    }
}
