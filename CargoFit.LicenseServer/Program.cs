using System.Text;
using CargoFit.LicenseServer;
using CargoFit.LicenseServer.Core;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Environment.GetEnvironmentVariable("LICENSE_SERVER_DB_PATH")
             ?? Path.Combine(AppContext.BaseDirectory, "licenses.db");
var signingKey = Environment.GetEnvironmentVariable("LICENSE_SERVER_SIGNING_KEY")
                 ?? throw new InvalidOperationException(
                     "LICENSE_SERVER_SIGNING_KEY env var is required. " +
                     "Run `license-admin init-keys` to generate one.");
var adminPassword = Environment.GetEnvironmentVariable("LICENSE_SERVER_ADMIN_PASSWORD");
var adminUser = Environment.GetEnvironmentVariable("LICENSE_SERVER_ADMIN_USER") ?? "admin";

// Fail loudly at startup if the key is not a valid 32-byte Ed25519 private key,
// rather than at first request time.
try
{
    var bytes = Convert.FromBase64String(signingKey);
    if (bytes.Length != 32)
        throw new InvalidOperationException(
            $"LICENSE_SERVER_SIGNING_KEY must decode to 32 bytes; got {bytes.Length}.");
    _ = LicenseSigner.Sign(signingKey, new byte[] { 0 });
}
catch (FormatException ex)
{
    throw new InvalidOperationException(
        "LICENSE_SERVER_SIGNING_KEY is not valid base64.", ex);
}

builder.Services.AddDbContext<LicenseDbContext>(opts =>
    opts.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton(new ServerSigningKey(signingKey));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();
    db.Database.EnsureCreated();
    LicenseDbContext.EnsureAuditLogTable(db);
}

// Basic Auth guard for /admin routes — disabled entirely if password is unset.
app.Use(async (ctx, next) =>
{
    if (!ctx.Request.Path.StartsWithSegments("/admin"))
    {
        await next();
        return;
    }
    if (string.IsNullOrEmpty(adminPassword))
    {
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsync("Admin disabled. Set LICENSE_SERVER_ADMIN_PASSWORD env var to enable.");
        return;
    }
    var header = ctx.Request.Headers.Authorization.ToString();
    if (header.StartsWith("Basic ", StringComparison.Ordinal))
    {
        try
        {
            var creds = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..])).Split(':', 2);
            if (creds.Length == 2 && creds[0] == adminUser && creds[1] == adminPassword)
            {
                await next();
                return;
            }
        }
        catch { /* fall through to 401 */ }
    }
    ctx.Response.StatusCode = 401;
    ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"Logistic License Admin\"";
});

app.MapAdminEndpoints();

app.MapGet("/healthz", () => Results.Text("OK"));

app.MapPost("/v1/activate", async (ActivateRequest req, LicenseDbContext db, ServerSigningKey key, HttpContext httpCtx) =>
{
    var ip = httpCtx.Connection.RemoteIpAddress?.ToString();

    if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.MachineId))
        return Results.BadRequest(new { error = "missing_fields" });

    var license = await db.Licenses.FirstOrDefaultAsync(l => l.Token == req.Token);
    if (license is null)
    {
        await AuditWriter.WriteAsync(db, "activate", req.Token, null, req.MachineId, ip, false, "unknown_token");
        return Results.NotFound(new { error = "unknown_token" });
    }
    if (license.Revoked)
    {
        await AuditWriter.WriteAsync(db, "activate", license.Token, license.ClientName, req.MachineId, ip, false, "revoked");
        return Results.Json(new { error = "revoked" }, statusCode: 410);
    }

    var now = DateTime.UtcNow;
    if (now >= license.ExpiresAt)
    {
        await AuditWriter.WriteAsync(db, "activate", license.Token, license.ClientName, req.MachineId, ip, false, "expired");
        return Results.Json(new { error = "expired" }, statusCode: 410);
    }

    if (license.MachineId is null)
    {
        license.MachineId = req.MachineId;
    }
    else if (license.MachineId != req.MachineId)
    {
        await AuditWriter.WriteAsync(db, "activate", license.Token, license.ClientName, req.MachineId, ip, false, "wrong_machine");
        return Results.Conflict(new { error = "wrong_machine" });
    }

    license.LastSeenAt = now;
    await db.SaveChangesAsync();
    await AuditWriter.WriteAsync(db, "activate", license.Token, license.ClientName, req.MachineId, ip, true);

    return Sign(license, now, key);
});

app.MapPost("/v1/heartbeat", async (HeartbeatRequest req, LicenseDbContext db, ServerSigningKey key, HttpContext httpCtx) =>
{
    var ip = httpCtx.Connection.RemoteIpAddress?.ToString();

    if (string.IsNullOrWhiteSpace(req.Token) || string.IsNullOrWhiteSpace(req.MachineId))
        return Results.BadRequest(new { error = "missing_fields" });

    var license = await db.Licenses.FirstOrDefaultAsync(l => l.Token == req.Token);
    if (license is null)
    {
        await AuditWriter.WriteAsync(db, "heartbeat", req.Token, null, req.MachineId, ip, false, "unknown_token");
        return Results.NotFound(new { error = "unknown_token" });
    }
    if (license.Revoked)
    {
        await AuditWriter.WriteAsync(db, "heartbeat", license.Token, license.ClientName, req.MachineId, ip, false, "revoked");
        return Results.Json(new { error = "revoked" }, statusCode: 410);
    }

    var now = DateTime.UtcNow;
    if (now >= license.ExpiresAt)
    {
        await AuditWriter.WriteAsync(db, "heartbeat", license.Token, license.ClientName, req.MachineId, ip, false, "expired");
        return Results.Json(new { error = "expired" }, statusCode: 410);
    }

    if (license.MachineId != req.MachineId)
    {
        await AuditWriter.WriteAsync(db, "heartbeat", license.Token, license.ClientName, req.MachineId, ip, false, "wrong_machine");
        return Results.Conflict(new { error = "wrong_machine" });
    }

    license.LastSeenAt = now;
    await db.SaveChangesAsync();
    await AuditWriter.WriteAsync(db, "heartbeat", license.Token, license.ClientName, req.MachineId, ip, true);

    return Sign(license, now, key);
});

app.Run();

static IResult Sign(License license, DateTime now, ServerSigningKey key)
{
    var payload = SignedPayload.Canonical(license.Token, license.MachineId!, license.ExpiresAt, now);
    var signature = LicenseSigner.Sign(key.PrivateKeyBase64, payload);
    return Results.Ok(new ActivationResponse(
        ExpiresAt: license.ExpiresAt.ToUniversalTime().ToString("O"),
        ServerNow: now.ToUniversalTime().ToString("O"),
        Signature: signature
    ));
}

record ActivateRequest(string Token, string MachineId);
record HeartbeatRequest(string Token, string MachineId);
record ActivationResponse(string ExpiresAt, string ServerNow, string Signature);
record ServerSigningKey(string PrivateKeyBase64);
