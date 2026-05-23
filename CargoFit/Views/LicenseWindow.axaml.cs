using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CargoFit;

public partial class LicenseWindow : Window
{
    private bool _activated;

    public LicenseWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shows the activation window with the message appropriate to <paramref name="reason"/>.
    /// Returns true if the user successfully activated; false if they closed/cancelled.
    /// </summary>
    internal static async Task<bool> ShowAndActivateAsync(LicenseResult reason)
    {
        var win = new LicenseWindow();
        win.ApplyReason(reason);
        await win.ShowDialogStandalone();
        return win._activated;
    }

    private async Task ShowDialogStandalone()
    {
        var tcs = new TaskCompletionSource();
        Closed += (_, _) => tcs.TrySetResult();
        Show();
        await tcs.Task;
    }

    private void ApplyReason(LicenseResult reason)
    {
        StatusLabel.Text = reason.Status switch
        {
            LicenseStatus.NeedsActivation => "",
            LicenseStatus.Expired         => "โทเค็นเดิมหมดอายุแล้ว กรอกโทเค็นใหม่ที่ได้จากปิงด้านล่าง",
            LicenseStatus.Revoked         => "โทเค็นเดิมถูกยกเลิก กรอกโทเค็นใหม่ที่ได้จากปิงด้านล่าง",
            LicenseStatus.WrongMachine    => "โทเค็นเดิมถูกผูกกับเครื่องอื่น กรอกโทเค็นใหม่ที่ได้จากปิงด้านล่าง",
            LicenseStatus.NoNetwork       => "ไม่สามารถเชื่อมต่อกับเซิร์ฟเวอร์ กรุณาตรวจสอบอินเทอร์เน็ตแล้วลองอีกครั้ง",
            LicenseStatus.UnknownToken    => "ไม่พบโทเค็นนี้ในระบบ",
            LicenseStatus.BadSignature    => "เซิร์ฟเวอร์ตอบกลับด้วยลายเซ็นที่ไม่ถูกต้อง",
            LicenseStatus.ServerError     => "เซิร์ฟเวอร์ขัดข้อง กรุณาลองอีกครั้งภายหลัง",
            _                              => "",
        };

        // NoNetwork ไม่สามารถทำอะไรได้จนกว่าจะมีเน็ต — disable input
        // Expired / Revoked / WrongMachine → โทเค็นเดิมตาย แต่ผู้ใช้สามารถกรอกโทเค็นใหม่ทดแทนได้
        if (reason.Status == LicenseStatus.NoNetwork)
        {
            TokenInput.IsEnabled = false;
            ActivateButton.IsEnabled = false;
            CancelButton.Content = "ปิด";
        }
    }

    private async void ActivateButton_Click(object? sender, RoutedEventArgs e)
    {
        ActivateButton.IsEnabled = false;
        StatusLabel.Foreground = ThemeColors.InkMuted;
        StatusLabel.Text = "กำลังเปิดใช้งาน…";

        var result = await LicenseManager.ActivateAsync(TokenInput.Text ?? "");

        if (result.IsOk)
        {
            _activated = true;
            Close();
            return;
        }

        StatusLabel.Foreground = ThemeColors.Danger;
        StatusLabel.Text = result.Status switch
        {
            LicenseStatus.UnknownToken   => "โทเค็นไม่ถูกต้อง",
            LicenseStatus.Expired        => "โทเค็นนี้หมดอายุแล้ว",
            LicenseStatus.Revoked        => "โทเค็นนี้ถูกยกเลิก",
            LicenseStatus.WrongMachine   => "โทเค็นนี้ถูกผูกกับเครื่องอื่นแล้ว",
            LicenseStatus.NoNetwork      => "ไม่สามารถเชื่อมต่อกับเซิร์ฟเวอร์",
            LicenseStatus.BadSignature   => "ลายเซ็นจากเซิร์ฟเวอร์ไม่ถูกต้อง",
            _                             => $"เกิดข้อผิดพลาด: {result.RawError ?? "unknown"}",
        };
        ActivateButton.IsEnabled = true;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close();
}
