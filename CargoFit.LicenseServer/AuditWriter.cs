using CargoFit.LicenseServer.Core;

namespace CargoFit.LicenseServer;

internal static class AuditWriter
{
    internal static async Task WriteAsync(
        LicenseDbContext db,
        string action,
        string token,
        string? clientName,
        string? machineId,
        string? ip,
        bool success,
        string? detail = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Timestamp  = DateTime.UtcNow,
            Action     = action,
            Token      = token,
            ClientName = clientName,
            MachineId  = machineId,
            IpAddress  = ip,
            Success    = success,
            Detail     = detail,
        });
        await db.SaveChangesAsync();
    }
}
