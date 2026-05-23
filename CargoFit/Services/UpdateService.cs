using Velopack;
using Velopack.Sources;

namespace CargoFit;

/// <summary>
/// ตรวจสอบและติดตั้งอัปเดตจาก GitHub Releases
/// ไม่ auto-update — แสดงแค่ banner และรอให้ผู้ใช้กดปุ่ม
/// </summary>
internal static class UpdateService
{
    private const string RepoUrl = "https://github.com/PingSunn/cargofit";

    private static UpdateManager? _mgr;
    private static UpdateInfo? _pending;

    /// <summary>
    /// ข้อมูลอัปเดตที่พบ (null = ไม่มีอัปเดต หรือเช็คไม่สำเร็จ)
    /// </summary>
    internal static UpdateInfo? PendingUpdate => _pending;

    /// <summary>
    /// เช็คว่ามีเวอร์ชันใหม่ใน GitHub Releases หรือไม่
    /// เรียกได้ปลอดภัยใน dev mode — จะ catch ทุก exception แบบเงียบ
    /// </summary>
    internal static async Task CheckAsync()
    {
        try
        {
            _mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
            _pending = await _mgr.CheckForUpdatesAsync();
        }
        catch
        {
            // ไม่มีเน็ต / รันจาก dev / ยังไม่มี release บน GitHub → ไม่ทำอะไร
        }
    }

    /// <summary>
    /// ดาวน์โหลด + ติดตั้งอัปเดต แล้ว restart แอปทันที
    /// เรียกจากปุ่ม UI เท่านั้น — method นี้ไม่ return เมื่อสำเร็จ
    /// </summary>
    internal static async Task ApplyUpdateAsync(Action<int>? onProgress = null)
    {
        if (_mgr == null || _pending == null) return;
        await _mgr.DownloadUpdatesAsync(_pending, onProgress);
        _mgr.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
