using System.Text;
using System.Text.Json;

namespace CargoFit.LicenseServer.Core;

public static class SignedPayload
{
    public static byte[] Canonical(string tokenId, string machineId, DateTime expiresAt, DateTime serverNow)
    {
        var obj = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["expiresAt"] = expiresAt.ToUniversalTime().ToString("O"),
            ["machineId"] = machineId,
            ["serverNow"] = serverNow.ToUniversalTime().ToString("O"),
            ["tokenId"]   = tokenId,
        };
        var json = JsonSerializer.Serialize(obj);
        return Encoding.UTF8.GetBytes(json);
    }
}
