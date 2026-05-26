using System;
using System.Diagnostics;
using System.IO;

namespace CargoFit;

/// <summary>
/// Lightweight debug logger for PackingEngine.
/// ใช้งาน:
///   - CLI mode (--input): เปิดอัตโนมัติเสมอ → เขียนไปที่ packing-debug.log ใน CWD
///   - GUI mode: เปิดเมื่อ env var CARGOFIT_DEBUG=1 → เขียนไปที่ packing-debug.log ใน CWD
/// </summary>
internal static class PackingLog
{
    private static StreamWriter? _writer;

    internal static bool IsEnabled { get; private set; }

    /// <summary>
    /// เรียกก่อน PackingEngine.Calculate() ทุกครั้ง
    /// </summary>
    /// <param name="logPath">path เต็มของ log file, null = packing-debug.log ใน CWD</param>
    /// <param name="force">true = เปิด log ไม่ว่า env var จะเป็นอะไร (ใช้ใน CLI mode)</param>
    internal static void Init(string? logPath = null, bool force = false)
    {
        IsEnabled = force || Environment.GetEnvironmentVariable("CARGOFIT_DEBUG") == "1";
        if (!IsEnabled) return;

        var path = logPath ?? Path.Combine(Directory.GetCurrentDirectory(), "packing-debug.log");
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    internal static void Phase(string name) => Write($"\n=== [{name}] ===");
    internal static void Info(string msg)   => Write($"  {msg}");
    internal static void Blank()            => Write(string.Empty);

    internal static void Finish()
    {
        _writer?.Dispose();
        _writer = null;
        IsEnabled = false;
    }

    private static void Write(string msg)
    {
        if (!IsEnabled) return;
        _writer?.WriteLine(msg);
        Debug.WriteLine($"[PackingLog] {msg}");
    }
}
