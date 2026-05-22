using System.Security.Cryptography;

namespace Logistic.LicenseServer.Core;

public static class TokenGenerator
{
    private const string Prefix = "tr_";

    public static string NewToken()
    {
        // 16 random bytes → 22 base64url chars (no padding), ≥128 bits entropy.
        Span<byte> buf = stackalloc byte[16];
        RandomNumberGenerator.Fill(buf);
        var b64 = Convert.ToBase64String(buf)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return Prefix + b64;
    }
}
