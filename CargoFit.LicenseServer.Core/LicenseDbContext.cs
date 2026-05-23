using Microsoft.EntityFrameworkCore;

namespace CargoFit.LicenseServer.Core;

public class LicenseDbContext : DbContext
{
    public DbSet<License> Licenses => Set<License>();

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
    }

    public static LicenseDbContext OpenFile(string dbPath)
    {
        var ctx = new LicenseDbContext(dbPath);
        ctx.Database.EnsureCreated();
        return ctx;
    }
}
