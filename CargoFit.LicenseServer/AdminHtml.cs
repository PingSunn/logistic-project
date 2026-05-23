using System.Net;
using System.Text;
using CargoFit.LicenseServer.Core;

namespace CargoFit.LicenseServer;

internal static class AdminHtml
{
    internal static string RenderList(IEnumerable<License> licenses, string? flash = null)
    {
        var sb = new StringBuilder();
        sb.Append(Shell.Start("License Admin"));

        if (!string.IsNullOrEmpty(flash))
            sb.Append($"<div class='mb-4 rounded bg-emerald-50 border border-emerald-200 text-emerald-800 px-4 py-3 text-sm'>{Escape(flash)}</div>");

        sb.Append("""
        <div class='bg-white rounded-lg shadow-sm border border-slate-200 p-5 mb-6'>
          <h2 class='text-base font-semibold text-slate-800 mb-3'>+ ออกโทเค็นใหม่</h2>
          <form method='post' action='/admin/mint' class='flex flex-wrap gap-3 items-end'>
            <div class='flex-1 min-w-[200px]'>
              <label class='block text-xs font-medium text-slate-600 mb-1'>ชื่อลูกค้า</label>
              <input name='clientName' required maxlength='100' class='w-full border border-slate-300 rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500' placeholder='เช่น ลูกค้า ก.'>
            </div>
            <div>
              <label class='block text-xs font-medium text-slate-600 mb-1'>จำนวนวัน</label>
              <input name='days' type='number' min='1' max='365' value='30' class='w-24 border border-slate-300 rounded px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'>
            </div>
            <button type='submit' class='bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-5 py-2 rounded'>สร้างโทเค็น</button>
          </form>
        </div>
        """);

        sb.Append("<div class='bg-white rounded-lg shadow-sm border border-slate-200 overflow-hidden'>");
        sb.Append("<table class='w-full text-sm'>");
        sb.Append("""
          <thead class='bg-slate-50 text-slate-600 text-xs uppercase'>
            <tr>
              <th class='px-4 py-2 text-left'>Token</th>
              <th class='px-4 py-2 text-left'>ลูกค้า</th>
              <th class='px-4 py-2 text-left'>หมดอายุ <span class='normal-case text-slate-400'>(เวลาไทย)</span></th>
              <th class='px-4 py-2 text-left'>สถานะ</th>
              <th class='px-4 py-2 text-left'>ผูกเครื่อง</th>
              <th class='px-4 py-2 text-left'>ใช้งานล่าสุด <span class='normal-case text-slate-400'>(เวลาไทย)</span></th>
              <th class='px-4 py-2'></th>
            </tr>
          </thead>
        """);
        sb.Append("<tbody class='divide-y divide-slate-100'>");

        var rows = licenses.OrderByDescending(l => l.CreatedAt).ToList();
        if (rows.Count == 0)
        {
            sb.Append("<tr><td colspan='7' class='px-4 py-8 text-center text-slate-400'>ยังไม่มีโทเค็น</td></tr>");
        }
        else
        {
            foreach (var l in rows)
                sb.Append(RenderRow(l));
        }

        sb.Append("</tbody></table></div>");
        sb.Append(Shell.End());
        return sb.ToString();
    }

    internal static string RenderMintSuccess(License license)
    {
        var sb = new StringBuilder();
        sb.Append(Shell.Start("Token created"));
        sb.Append("""
        <div class='bg-emerald-50 border border-emerald-200 rounded-lg p-6 mb-4'>
          <h2 class='text-lg font-semibold text-emerald-900 mb-2'>✓ สร้างโทเค็นสำเร็จ</h2>
          <p class='text-sm text-emerald-800 mb-4'>คัดลอกโทเค็นด้านล่างนี้ส่งให้ลูกค้า — หน้านี้จะไม่แสดงอีก</p>
          <div class='bg-white border border-emerald-300 rounded p-4 mb-4'>
            <div class='flex items-center justify-between mb-1'>
              <div class='text-xs text-slate-500'>Token</div>
              <button type='button'
                      data-token='
        """);
        sb.Append(Escape(license.Token));
        sb.Append("""
                      '
                      onclick='copyToken(this)'
                      class='copy-btn text-slate-400 hover:text-blue-600 text-sm flex items-center gap-1'>
                <span>📋 คัดลอก</span>
              </button>
            </div>
            <div class='font-mono text-lg text-slate-900 select-all break-all'>
        """);
        sb.Append(Escape(license.Token));
        sb.Append("""
            </div>
          </div>
          <div class='grid grid-cols-2 gap-4 text-sm'>
            <div><span class='text-slate-500'>ลูกค้า:</span> <span class='font-medium'>
        """);
        sb.Append(Escape(license.ClientName));
        sb.Append("""
            </span></div>
            <div><span class='text-slate-500'>หมดอายุ:</span> <span class='font-medium'>
        """);
        sb.Append(ToBangkok(license.ExpiresAt).ToString("yyyy-MM-dd HH:mm"));
        sb.Append("""
              (เวลาไทย)</span></div>
          </div>
        </div>
        <a href='/admin' class='inline-block bg-slate-200 hover:bg-slate-300 text-slate-800 text-sm font-medium px-4 py-2 rounded'>← กลับไปหน้ารายการ</a>
        """);
        sb.Append(Shell.End());
        return sb.ToString();
    }

    // Database stores UTC; display in Bangkok time (UTC+7, no DST).
    private static DateTime ToBangkok(DateTime utc) => utc.AddHours(7);

    private static string RenderRow(License l)
    {
        var (statusLabel, statusClass) = StatusBadge(l);
        var lastSeen = l.LastSeenAt is null
            ? "<span class='text-slate-300'>—</span>"
            : Escape(ToBangkok(l.LastSeenAt.Value).ToString("yyyy-MM-dd HH:mm"));
        var bound = l.MachineId is null
            ? "<span class='text-slate-300'>—</span>"
            : "<span class='font-mono text-xs text-slate-500'>" + Escape(l.MachineId.Substring(0, Math.Min(12, l.MachineId.Length))) + "…</span>";

        var revokeBtn = l.Revoked
            ? ""
            : $"<form method='post' action='/admin/revoke' onsubmit=\"return confirm('ยืนยันการยกเลิกโทเค็นของ {Escape(l.ClientName).Replace("'", "&#39;")}?')\" class='inline'><input type='hidden' name='token' value='{Escape(l.Token)}'><button type='submit' class='text-rose-600 hover:text-rose-700 text-xs font-medium'>ยกเลิก</button></form>";

        var maskedToken = Mask(l.Token);
        var tokenCell = $"""
          <div class='inline-flex items-center gap-2'>
            <span class='font-mono text-xs text-slate-700'>{Escape(maskedToken)}</span>
            <button type='button'
                    title='คัดลอกโทเค็นเต็ม'
                    data-token='{Escape(l.Token)}'
                    onclick='copyToken(this)'
                    class='copy-btn text-slate-400 hover:text-blue-600 text-xs'>📋</button>
          </div>
        """;

        return $"""
          <tr class='hover:bg-slate-50'>
            <td class='px-4 py-3'>{tokenCell}</td>
            <td class='px-4 py-3 text-slate-900'>{Escape(l.ClientName)}</td>
            <td class='px-4 py-3 text-slate-700'>{ToBangkok(l.ExpiresAt):yyyy-MM-dd}</td>
            <td class='px-4 py-3'><span class='inline-block px-2 py-0.5 rounded text-xs font-medium {statusClass}'>{statusLabel}</span></td>
            <td class='px-4 py-3'>{bound}</td>
            <td class='px-4 py-3 text-slate-500 text-xs'>{lastSeen}</td>
            <td class='px-4 py-3 text-right'>{revokeBtn}</td>
          </tr>
        """;
    }

    private static string Mask(string token)
    {
        // tr_8HPc-1O-785365lWukDn6A  →  tr_8HPc…Dn6A
        if (token.Length <= 10) return token;
        return $"{token[..7]}…{token[^4..]}";
    }

    private static (string Label, string Class) StatusBadge(License l)
    {
        if (l.Revoked) return ("ยกเลิกแล้ว", "bg-slate-100 text-slate-600");
        if (DateTime.UtcNow >= l.ExpiresAt) return ("หมดอายุ", "bg-rose-100 text-rose-700");
        var daysLeft = (int)Math.Ceiling((l.ExpiresAt - DateTime.UtcNow).TotalDays);
        if (daysLeft <= 3) return ($"เหลือ {daysLeft} วัน", "bg-amber-100 text-amber-800");
        return ($"ใช้งานได้ ({daysLeft} วัน)", "bg-emerald-100 text-emerald-700");
    }

    internal static string Escape(string? s) => s is null ? "" : WebUtility.HtmlEncode(s);

    private static class Shell
    {
        internal static string Start(string title) => $$"""
        <!doctype html>
        <html lang='th'>
        <head>
          <meta charset='utf-8'>
          <meta name='viewport' content='width=device-width,initial-scale=1'>
          <title>{{Escape(title)}}</title>
          <script src='https://cdn.tailwindcss.com'></script>
          <script>
            async function copyToken(btn) {
              const token = btn.dataset.token;
              try {
                await navigator.clipboard.writeText(token);
                const original = btn.innerHTML;
                btn.innerHTML = '✓';
                btn.classList.add('text-emerald-600');
                btn.classList.remove('text-slate-400','hover:text-blue-600');
                setTimeout(() => {
                  btn.innerHTML = original;
                  btn.classList.remove('text-emerald-600');
                  btn.classList.add('text-slate-400','hover:text-blue-600');
                }, 1500);
              } catch (e) {
                window.prompt('คัดลอกโทเค็น (Ctrl+C / ⌘C):', token);
              }
            }
          </script>
        </head>
        <body class='bg-slate-100 min-h-screen'>
          <div class='max-w-5xl mx-auto px-6 py-8'>
            <header class='flex items-center justify-between mb-6'>
              <h1 class='text-xl font-semibold text-slate-900'>Logistic License Admin</h1>
              <a href='/admin' class='text-sm text-slate-500 hover:text-slate-800'>รายการทั้งหมด</a>
            </header>
        """;

        internal static string End() => """
          </div>
        </body>
        </html>
        """;
    }
}
