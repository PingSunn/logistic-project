using System.Diagnostics;

namespace CargoFit.Installer;

internal static class Program
{
    private static readonly CancellationTokenSource Cts = new();

    static async Task<int> Main()
    {
        // รองรับ UTF-8 สำหรับภาษาไทย
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "CargoFit Installer";

        // ให้ Ctrl+C ยกเลิก download ได้
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; Cts.Cancel(); };

        PrintBanner();

        string? tempSetup = null;
        try
        {
            // Step 1: ตรวจ GitHub API
            Print("กำลังตรวจสอบเวอร์ชันล่าสุด...", ConsoleColor.Cyan);

            var release = await GitHubClient.GetLatestReleaseAsync(Cts.Token);
            var asset   = release.Assets.FirstOrDefault(a =>
                              a.Name.Equals("CargoFit-win-Setup.exe",
                                  StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException(
                              "ไม่พบไฟล์ CargoFit-win-Setup.exe ใน GitHub Release");

            var version = release.TagName.TrimStart('v');
            Console.WriteLine();
            Print($"พบเวอร์ชัน {version}", ConsoleColor.Green);
            Console.WriteLine();

            // Step 2: Download พร้อม progress bar
            Print($"กำลังดาวน์โหลด v{version}...", ConsoleColor.Cyan);
            Console.WriteLine();

            var progress = new Progress<(long received, long total)>(p =>
            {
                if (p.total <= 0) return;
                DrawProgressBar(p.received, p.total);
            });

            tempSetup = await GitHubClient.DownloadToTempAsync(
                asset.BrowserDownloadUrl, asset.Size, progress, Cts.Token);

            Console.WriteLine();
            Console.WriteLine();

            // Step 3: รัน setup --silent
            Print("กำลังติดตั้ง กรุณารอสักครู่...", ConsoleColor.Cyan);

            // UseShellExecute=true เพื่อให้ Velopack ขอ UAC elevation ได้
            var psi  = new ProcessStartInfo(tempSetup, "--silent") { UseShellExecute = true };
            var proc = Process.Start(psi)
                       ?? throw new InvalidOperationException("ไม่สามารถเริ่มต้น installer ได้");

            await proc.WaitForExitAsync(Cts.Token);

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Installer ออกด้วย exit code {proc.ExitCode}");

            // Step 4: Done
            Console.WriteLine();
            Print("ติดตั้ง CargoFit เสร็จแล้ว!", ConsoleColor.Green);
            Console.WriteLine();
            Console.Write("กด Enter เพื่อปิด...");
            Console.ReadLine();
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Print("ยกเลิกแล้ว", ConsoleColor.Yellow);
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Print($"เกิดข้อผิดพลาด: {ex.Message}", ConsoleColor.Red);
            Console.WriteLine();
            Console.Write("กด Enter เพื่อปิด...");
            Console.ReadLine();
            return 2;
        }
        finally
        {
            if (tempSetup is not null)
                try { File.Delete(tempSetup); } catch { /* best-effort */ }
        }
    }

    // ── UI helpers ─────────────────────────────────────────────────────────────

    private static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        Console.WriteLine("  ╔═══════════════════════════════════╗");
        Console.WriteLine("  ║         CargoFit Installer        ║");
        Console.WriteLine("  ╚═══════════════════════════════════╝");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void Print(string text, ConsoleColor color)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"  {text}");
        Console.ResetColor();
    }

    private static void DrawProgressBar(long received, long total)
    {
        const int barWidth = 30;
        var pct      = (int)(received * 100L / total);
        var filled   = (int)(received * barWidth / total);
        var bar      = new string('█', filled) + new string('░', barWidth - filled);
        var mbRecv   = received / 1_048_576.0;
        var mbTotal  = total    / 1_048_576.0;

        // \r เพื่อ overwrite บรรทัดเดิม
        Console.Write($"\r  [{bar}] {pct,3}%  {mbRecv:F1}/{mbTotal:F1} MB  ");
    }
}
