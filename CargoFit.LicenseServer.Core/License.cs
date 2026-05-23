using System.ComponentModel.DataAnnotations;

namespace CargoFit.LicenseServer.Core;

public class License
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Token { get; set; } = "";

    [MaxLength(200)]
    public string ClientName { get; set; } = "";

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    [MaxLength(128)]
    public string? MachineId { get; set; }

    public bool Revoked { get; set; }

    public DateTime? LastSeenAt { get; set; }
}
