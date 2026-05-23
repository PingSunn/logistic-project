using Microsoft.EntityFrameworkCore;

namespace CargoFit.LicenseServer.Core;

public class LicenseDbContext : DbContext
{
    public DbSet<License> Licenses => Set<License>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    private readonly string _dbPath;

    public LicenseDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public LicenseDbContext(DbContextOptions<LicenseDbContext> options) : base(options)
    {
        _dbPath = "";
    }

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured && !string.IsNullOrEmpty(_dbPath))
            options.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<License>()
            .HasIndex(l => l.Token)
            .IsUnique();

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.Timestamp);
    }

    /// <summary>
    /// Creates the AuditLogs table (and its index) if it doesn't exist yet.
    /// Must be called after EnsureCreated() — safe to call on both fresh and
    /// already-deployed databases (all statements use IF NOT EXISTS).
    /// </summary>
    public static void EnsureAuditLogTable(LicenseDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS AuditLogs (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp  TEXT    NOT NULL,
                Action     TEXT    NOT NULL,
                Token      TEXT    NOT NULL DEFAULT '',
                ClientName TEXT,
                MachineId  TEXT,
                IpAddress  TEXT,
                Success    INTEGER NOT NULL DEFAULT 0,
                Detail     TEXT
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            "CREATE INDEX IF NOT EXISTS IX_AuditLogs_Timestamp ON AuditLogs (Timestamp DESC);");
    }

    public static LicenseDbContext OpenFile(string dbPath)
    {
        var ctx = new LicenseDbContext(dbPath);
        ctx.Database.EnsureCreated();
        EnsureAuditLogTable(ctx);
        return ctx;
    }
}
