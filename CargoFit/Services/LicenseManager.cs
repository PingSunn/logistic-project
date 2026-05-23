using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace CargoFit;

internal enum LicenseStatus
{
    Ok,
    NeedsActivation,
    Expired,
    Revoked,
    WrongMachine,
    NoNetwork,
    BadSignature,
    ServerError,
    UnknownToken,
}

internal readonly record struct LicenseResult(LicenseStatus Status, DateTime? ExpiresAt = null, string? RawError = null)
{
    internal bool IsOk => Status == LicenseStatus.Ok;
}

internal static class LicenseManager
{
    private const int BackgroundHeartbeatMinutes = 30;
    private const int FreshCacheMinutes = 5;
    private const int MaxConsecutiveNetworkFailures = 4;

    private static readonly string CacheFile = Path.Combine(AppPaths.DataDir, "license-cache.json");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static DateTime? _lastVerifiedAt;
    private static DateTime? _lastKnownExpiresAt;
    private static int _consecutiveNetworkFailures;
    private static PeriodicTimer? _heartbeatTimer;

    internal static DateTime? LastKnownExpiresAt => _lastKnownExpiresAt;

    internal static int? DaysRemaining
    {
        get
        {
            if (_lastKnownExpiresAt is not DateTime e) return null;
            var days = (int)Math.Ceiling((e - DateTime.UtcNow).TotalDays);
            return Math.Max(0, days);
        }
    }

    /// <summary>Fired after every successful heartbeat. Argument is the absolute expiry timestamp.</summary>
    internal static event Action<DateTime>? Verified;

    /// <summary>Fired when the background timer concludes the license is no longer valid (expired/revoked/wrong machine/sustained network loss).</summary>
    internal static event Action<LicenseResult>? Lost;

    private sealed record CacheEntry(string Token, string MachineId);

    private sealed record ServerResponse(
        [property: JsonPropertyName("expiresAt")] string ExpiresAt,
        [property: JsonPropertyName("serverNow")] string ServerNow,
        [property: JsonPropertyName("signature")] string Signature);

    private sealed record ServerError([property: JsonPropertyName("error")] string Error);

    /// <summary>
    /// Called once at startup. If a cached token exists, hit /v1/heartbeat to validate.
    /// </summary>
    internal static async Task<LicenseResult> EnforceAsync()
    {
        var cache = LoadCache();
        if (cache is null) return new LicenseResult(LicenseStatus.NeedsActivation);

        var machineId = MachineFingerprint.Get();
        if (cache.MachineId != machineId)
            return new LicenseResult(LicenseStatus.WrongMachine);

        return await CallAsync("/v1/heartbeat", cache.Token, machineId);
    }

    /// <summary>
    /// Called by LicenseWindow when the user pastes a token. Persists cache on success.
    /// </summary>
    internal static async Task<LicenseResult> ActivateAsync(string token)
    {
        var trimmed = token.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return new LicenseResult(LicenseStatus.UnknownToken);

        var machineId = MachineFingerprint.Get();
        var result = await CallAsync("/v1/activate", trimmed, machineId);
        if (result.IsOk)
            SaveCache(new CacheEntry(trimmed, machineId));
        return result;
    }

    /// <summary>
    /// Called before any heavy action (Calculate, Export PDF). Uses cached result if the
    /// last successful heartbeat was within <see cref="FreshCacheMinutes"/>; otherwise
    /// hits the server.
    /// </summary>
    internal static async Task<LicenseResult> EnsureFreshAsync()
    {
        if (_lastVerifiedAt is DateTime t
            && (DateTime.UtcNow - t).TotalMinutes < FreshCacheMinutes
            && _lastKnownExpiresAt is DateTime e
            && DateTime.UtcNow < e)
        {
            return new LicenseResult(LicenseStatus.Ok, e);
        }

        var cache = LoadCache();
        if (cache is null) return new LicenseResult(LicenseStatus.NeedsActivation);

        var machineId = MachineFingerprint.Get();
        if (cache.MachineId != machineId)
            return new LicenseResult(LicenseStatus.WrongMachine);

        return await CallAsync("/v1/heartbeat", cache.Token, machineId);
    }

    /// <summary>
    /// Starts the background heartbeat. Call once after the initial EnforceAsync succeeds.
    /// </summary>
    internal static void StartBackgroundHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new PeriodicTimer(TimeSpan.FromMinutes(BackgroundHeartbeatMinutes));
        _ = HeartbeatLoopAsync(_heartbeatTimer);
    }

    private static async Task HeartbeatLoopAsync(PeriodicTimer timer)
    {
        while (await timer.WaitForNextTickAsync())
        {
            var cache = LoadCache();
            if (cache is null) continue;

            var result = await CallAsync("/v1/heartbeat", cache.Token, MachineFingerprint.Get());
            if (IsHardFailure(result.Status))
            {
                Lost?.Invoke(result);
                return;
            }
            // network/server hiccups are tolerated; CallAsync tracks the streak.
            if (_consecutiveNetworkFailures >= MaxConsecutiveNetworkFailures)
            {
                Lost?.Invoke(result);
                return;
            }
        }
    }

    private static bool IsHardFailure(LicenseStatus s) =>
        s is LicenseStatus.Expired
          or LicenseStatus.Revoked
          or LicenseStatus.WrongMachine
          or LicenseStatus.UnknownToken
          or LicenseStatus.BadSignature;

    private static async Task<LicenseResult> CallAsync(string path, string token, string machineId)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await Http.PostAsJsonAsync(
                LicenseConfig.ServerUrl + path,
                new { token, machineId });
        }
        catch
        {
            _consecutiveNetworkFailures++;
            return new LicenseResult(LicenseStatus.NoNetwork);
        }

        if (resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadFromJsonAsync<ServerResponse>();
            if (body is null) return new LicenseResult(LicenseStatus.ServerError);

            if (!VerifySignature(token, machineId, body))
                return new LicenseResult(LicenseStatus.BadSignature);

            if (!DateTime.TryParse(body.ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                return new LicenseResult(LicenseStatus.ServerError);

            _lastVerifiedAt = DateTime.UtcNow;
            _lastKnownExpiresAt = expiresAt;
            _consecutiveNetworkFailures = 0;
            Verified?.Invoke(expiresAt);
            return new LicenseResult(LicenseStatus.Ok, expiresAt);
        }

        ServerError? err = null;
        try { err = await resp.Content.ReadFromJsonAsync<ServerError>(); } catch { }

        return err?.Error switch
        {
            "expired"        => new LicenseResult(LicenseStatus.Expired),
            "revoked"        => new LicenseResult(LicenseStatus.Revoked),
            "wrong_machine"  => new LicenseResult(LicenseStatus.WrongMachine),
            "unknown_token"  => new LicenseResult(LicenseStatus.UnknownToken),
            _                => new LicenseResult(LicenseStatus.ServerError, RawError: err?.Error),
        };
    }

    private static bool VerifySignature(string token, string machineId, ServerResponse resp)
    {
        try
        {
            if (LicenseConfig.ServerPublicKeyBase64.StartsWith("REPLACE_"))
                return false;

            if (!DateTime.TryParse(resp.ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var expiresAt))
                return false;
            if (!DateTime.TryParse(resp.ServerNow, null, System.Globalization.DateTimeStyles.RoundtripKind, out var serverNow))
                return false;

            var payload = CanonicalPayload(token, machineId, expiresAt, serverNow);
            var keyBytes = Convert.FromBase64String(LicenseConfig.ServerPublicKeyBase64);
            var pub = new Ed25519PublicKeyParameters(keyBytes, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, pub);
            verifier.BlockUpdate(payload, 0, payload.Length);
            var sig = Convert.FromBase64String(resp.Signature);
            return verifier.VerifySignature(sig);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] CanonicalPayload(string token, string machineId, DateTime expiresAt, DateTime serverNow)
    {
        var obj = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["expiresAt"] = expiresAt.ToUniversalTime().ToString("O"),
            ["machineId"] = machineId,
            ["serverNow"] = serverNow.ToUniversalTime().ToString("O"),
            ["tokenId"]   = token,
        };
        var json = JsonSerializer.Serialize(obj);
        return Encoding.UTF8.GetBytes(json);
    }

    private static CacheEntry? LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return null;
            return JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(CacheFile));
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(CacheEntry entry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
        File.WriteAllText(CacheFile, JsonSerializer.Serialize(entry, JsonOptions.WriteIndented));
    }

    internal static void ClearCache()
    {
        try { File.Delete(CacheFile); } catch { }
    }
}
