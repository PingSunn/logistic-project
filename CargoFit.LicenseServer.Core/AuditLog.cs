using System.ComponentModel.DataAnnotations;

namespace CargoFit.LicenseServer.Core;

public class AuditLog
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }       // UTC

    [MaxLength(20)]
    public string Action { get; set; } = "";      // "activate" | "heartbeat" | "mint" | "revoke"

    [MaxLength(64)]
    public string Token { get; set; } = "";

    [MaxLength(200)]
    public string? ClientName { get; set; }

    [MaxLength(128)]
    public string? MachineId { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public bool Success { get; set; }

    [MaxLength(200)]
    public string? Detail { get; set; }           // e.g. "wrong_machine", "30 วัน"
}
