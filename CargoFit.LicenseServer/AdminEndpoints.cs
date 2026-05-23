using System.Text;
using CargoFit.LicenseServer.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CargoFit.LicenseServer;

internal static class AdminEndpoints
{
    internal static void MapAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/admin", async (LicenseDbContext db, [FromQuery] string? flash) =>
        {
            var rows = await db.Licenses.ToListAsync();
            var logs = await db.AuditLogs
                .OrderByDescending(a => a.Timestamp)
                .Take(100)
                .ToListAsync();
            return Results.Content(AdminHtml.RenderList(rows, logs, flash), "text/html; charset=utf-8");
        });

        app.MapPost("/admin/mint", async (
            [FromForm] string clientName,
            [FromForm] int? days,
            LicenseDbContext db,
            HttpContext httpCtx) =>
        {
            if (string.IsNullOrWhiteSpace(clientName))
                return Results.Redirect("/admin?flash=" + Uri.EscapeDataString("ต้องระบุชื่อลูกค้า"));

            var d = Math.Clamp(days ?? 30, 1, 365);
            var now = DateTime.UtcNow;
            var license = new License
            {
                Token = TokenGenerator.NewToken(),
                ClientName = clientName.Trim(),
                CreatedAt = now,
                ExpiresAt = now.AddDays(d),
            };
            db.Licenses.Add(license);
            await db.SaveChangesAsync();

            var ip = httpCtx.Connection.RemoteIpAddress?.ToString();
            await AuditWriter.WriteAsync(db, "mint", license.Token, license.ClientName, null, ip, true, $"{d} วัน");

            return Results.Content(AdminHtml.RenderMintSuccess(license), "text/html; charset=utf-8");
        }).DisableAntiforgery();

        app.MapPost("/admin/revoke", async ([FromForm] string token, LicenseDbContext db, HttpContext httpCtx) =>
        {
            var license = await db.Licenses.FirstOrDefaultAsync(l => l.Token == token);
            if (license is null)
                return Results.Redirect("/admin?flash=" + Uri.EscapeDataString("ไม่พบโทเค็น"));

            license.Revoked = true;
            await db.SaveChangesAsync();

            var ip = httpCtx.Connection.RemoteIpAddress?.ToString();
            await AuditWriter.WriteAsync(db, "revoke", license.Token, license.ClientName, null, ip, true);

            return Results.Redirect("/admin?flash=" + Uri.EscapeDataString($"ยกเลิกโทเค็น {license.ClientName} แล้ว"));
        }).DisableAntiforgery();
    }
}
