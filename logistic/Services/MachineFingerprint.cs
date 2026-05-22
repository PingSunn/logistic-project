using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace logistic;

internal static class MachineFingerprint
{
    private static string? _cached;

    internal static string Get()
    {
        if (_cached is not null) return _cached;

        var raw = ReadRaw() ?? Fallback();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        _cached = Convert.ToHexString(hash);
        return _cached;
    }

    private static string? ReadRaw()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return ReadWindowsMachineGuid();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return ReadMacIOPlatformUuid();
        }
        catch
        {
            // fall through to Fallback()
        }
        return null;
    }

    private static string? ReadWindowsMachineGuid()
    {
#pragma warning disable CA1416 // Validate platform compatibility
        using var key = Microsoft.Win32.Registry.LocalMachine
            .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid") as string;
#pragma warning restore CA1416
    }

    private static string? ReadMacIOPlatformUuid()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/usr/sbin/ioreg",
            Arguments = "-rd1 -c IOPlatformExpertDevice",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        foreach (var line in output.Split('\n'))
        {
            var idx = line.IndexOf("IOPlatformUUID", StringComparison.Ordinal);
            if (idx < 0) continue;
            var q = line.IndexOf('"', idx);
            if (q < 0) continue;
            var q2 = line.IndexOf('"', q + 1);
            if (q2 < 0) continue;
            var q3 = line.IndexOf('"', q2 + 1);
            if (q3 < 0) continue;
            var q4 = line.IndexOf('"', q3 + 1);
            if (q4 < 0) continue;
            return line.Substring(q3 + 1, q4 - q3 - 1);
        }
        return null;
    }

    private static string Fallback()
    {
        // Last-resort: hostname + first non-loopback MAC.
        var host = Environment.MachineName;
        string? mac = null;
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                var bytes = nic.GetPhysicalAddress().GetAddressBytes();
                if (bytes.Length == 0) continue;
                mac = Convert.ToHexString(bytes);
                break;
            }
        }
        catch { }
        return $"fallback|{host}|{mac ?? "no-mac"}";
    }
}
