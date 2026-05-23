using CargoFit.LicenseServer.Core;
using Microsoft.EntityFrameworkCore;

var dbPath = Environment.GetEnvironmentVariable("LICENSE_SERVER_DB_PATH")
             ?? Path.Combine(Directory.GetCurrentDirectory(), "licenses.db");

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

return args[0] switch
{
    "init-keys" => InitKeys(),
    "mint"      => Mint(args.Skip(1).ToArray()),
    "list"      => List(),
    "revoke"    => Revoke(args.Skip(1).ToArray()),
    "show"      => Show(args.Skip(1).ToArray()),
    "-h" or "--help" or "help" => UsageOk(),
    _ => UnknownCommand(args[0]),
};

static void PrintUsage()
{
    Console.WriteLine("license-admin <command> [args]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  init-keys                          Generate Ed25519 keypair; print both keys.");
    Console.WriteLine("  mint --client \"Name\" --days 30     Create a new token.");
    Console.WriteLine("  list                               Show all licenses.");
    Console.WriteLine("  revoke <token>                     Revoke a token.");
    Console.WriteLine("  show <token>                       Show details for one token.");
    Console.WriteLine();
    Console.WriteLine("Env vars:");
    Console.WriteLine("  LICENSE_SERVER_DB_PATH   Path to licenses.db (default: ./licenses.db)");
}

int UsageOk() { PrintUsage(); return 0; }

static int UnknownCommand(string cmd)
{
    Console.Error.WriteLine($"Unknown command: {cmd}");
    PrintUsage();
    return 1;
}

static int InitKeys()
{
    var keys = LicenseSigner.GenerateKeyPair();
    Console.WriteLine("# Public key (embed in client LicenseManager.cs):");
    Console.WriteLine(keys.PublicKeyBase64);
    Console.WriteLine();
    Console.WriteLine("# Private key (set on the server as env var):");
    Console.WriteLine($"export LICENSE_SERVER_SIGNING_KEY={keys.PrivateKeyBase64}");
    return 0;
}

int Mint(string[] a)
{
    string? clientName = null;
    int days = 30;
    for (var i = 0; i < a.Length; i++)
    {
        switch (a[i])
        {
            case "--client" when i + 1 < a.Length: clientName = a[++i]; break;
            case "--days" when i + 1 < a.Length:   days = int.Parse(a[++i]); break;
            default:
                Console.Error.WriteLine($"mint: unknown arg {a[i]}");
                return 1;
        }
    }
    if (string.IsNullOrWhiteSpace(clientName))
    {
        Console.Error.WriteLine("mint: --client required");
        return 1;
    }

    using var db = LicenseDbContext.OpenFile(dbPath);
    var now = DateTime.UtcNow;
    var license = new License
    {
        Token = TokenGenerator.NewToken(),
        ClientName = clientName,
        CreatedAt = now,
        ExpiresAt = now.AddDays(days),
    };
    db.Licenses.Add(license);
    db.SaveChanges();

    Console.WriteLine($"token:      {license.Token}");
    Console.WriteLine($"client:     {license.ClientName}");
    Console.WriteLine($"created:    {license.CreatedAt:O}");
    Console.WriteLine($"expires:    {license.ExpiresAt:O}");
    return 0;
}

int List()
{
    using var db = LicenseDbContext.OpenFile(dbPath);
    var rows = db.Licenses.OrderBy(l => l.CreatedAt).ToList();
    if (rows.Count == 0)
    {
        Console.WriteLine("(no licenses)");
        return 0;
    }
    Console.WriteLine($"{"TOKEN",-28} {"CLIENT",-20} {"EXPIRES",-22} {"BOUND",-8} {"REVOKED",-8} LAST SEEN");
    foreach (var l in rows)
    {
        Console.WriteLine(
            $"{l.Token,-28} " +
            $"{Truncate(l.ClientName, 20),-20} " +
            $"{l.ExpiresAt:yyyy-MM-dd HH:mm:ssZ} " +
            $"{(l.MachineId is null ? "-" : "yes"),-8} " +
            $"{(l.Revoked ? "yes" : "-"),-8} " +
            $"{(l.LastSeenAt is null ? "-" : l.LastSeenAt.Value.ToString("yyyy-MM-dd HH:mm:ssZ"))}");
    }
    return 0;
}

int Revoke(string[] a)
{
    if (a.Length != 1)
    {
        Console.Error.WriteLine("revoke <token>");
        return 1;
    }
    using var db = LicenseDbContext.OpenFile(dbPath);
    var license = db.Licenses.FirstOrDefault(l => l.Token == a[0]);
    if (license is null)
    {
        Console.Error.WriteLine($"revoke: no such token {a[0]}");
        return 1;
    }
    license.Revoked = true;
    db.SaveChanges();
    Console.WriteLine($"revoked: {license.Token}");
    return 0;
}

int Show(string[] a)
{
    if (a.Length != 1)
    {
        Console.Error.WriteLine("show <token>");
        return 1;
    }
    using var db = LicenseDbContext.OpenFile(dbPath);
    var l = db.Licenses.FirstOrDefault(x => x.Token == a[0]);
    if (l is null)
    {
        Console.Error.WriteLine($"show: no such token {a[0]}");
        return 1;
    }
    Console.WriteLine($"token:       {l.Token}");
    Console.WriteLine($"client:      {l.ClientName}");
    Console.WriteLine($"created:     {l.CreatedAt:O}");
    Console.WriteLine($"expires:     {l.ExpiresAt:O}");
    Console.WriteLine($"machine_id:  {l.MachineId ?? "(unbound)"}");
    Console.WriteLine($"revoked:     {(l.Revoked ? "yes" : "no")}");
    Console.WriteLine($"last_seen:   {(l.LastSeenAt?.ToString("O") ?? "(never)")}");
    return 0;
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
