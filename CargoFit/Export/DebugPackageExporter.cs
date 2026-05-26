using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;

namespace CargoFit;

/// <summary>
/// สร้าง debug package ZIP สำหรับส่งให้ developer ดูบัค
/// ประกอบด้วย: canvas.png + packing-output.json + packing-log.txt
/// </summary>
internal static class DebugPackageExporter
{
    // ── Canvas capture — ต้องเรียกบน UI thread ───────────────────────────────

    /// <summary>
    /// Render IsometricCanvas เป็น PNG byte[]
    /// ต้องเรียกบน UI thread ก่อน Task.Run
    /// </summary>
    internal static byte[] CaptureCanvasPng(IsometricCanvas canvas)
    {
        var bounds = canvas.Bounds;
        if (bounds.Width < 1 || bounds.Height < 1) return [];

        var rtb = new RenderTargetBitmap(
            new PixelSize((int)bounds.Width, (int)bounds.Height),
            new Vector(96, 96));
        rtb.Render(canvas);

        using var ms = new MemoryStream();
        rtb.Save(ms);   // PNG format
        return ms.ToArray();
    }

    // ── ZIP assembly — safe บน background thread ─────────────────────────────

    /// <summary>
    /// สร้าง ZIP byte[] จาก canvas PNG + packing data + log
    /// </summary>
    internal static byte[] Build(
        byte[] canvasPng,
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        PackingOutput output,
        string logText)
    {
        byte[] jsonBytes = BuildPackingJson(container, requests, output);
        byte[] logBytes  = Encoding.UTF8.GetBytes(
            string.IsNullOrWhiteSpace(logText)
                ? "// Packing log empty — กรุณาตั้งค่า CARGOFIT_DEBUG=1 เพื่อเปิด log โดยละเอียด"
                : logText);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "canvas.png",          canvasPng);
            AddEntry(zip, "packing-output.json", jsonBytes);
            AddEntry(zip, "packing-log.txt",     logBytes);
        }
        return ms.ToArray();
    }

    // ── JSON ──────────────────────────────────────────────────────────────────

    private static byte[] BuildPackingJson(
        ContainerSpec container,
        IReadOnlyList<(ProductSpec Spec, int Qty)> requests,
        PackingOutput output)
    {
        double cCbm   = StatsCalculator.ContainerCbm(container);
        double uCbm   = StatsCalculator.UsedCbm(output.Placements);
        double fill   = cCbm > 0 ? uCbm / cCbm * 100.0 : 0;
        double remain = StatsCalculator.RemainingDoorLengthCm(container, output.Placements);
        var    rows   = StatsCalculator.ComputeRows(
                            output.PackInfos, output.Placements,
                            output.MixedMap, output.CondoMap, output.ScatterMap);

        var doc = new
        {
            exportedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            container  = new
            {
                name      = container.Name,
                sizeLabel = container.SizeLabel,
                interiorW = container.InteriorW,
                interiorL = container.InteriorL,
                interiorH = container.InteriorH,
            },
            requests = requests.Select((r, i) => new
            {
                productIndex   = i,
                description    = r.Spec.Description,
                content        = r.Spec.Content,
                packSize       = r.Spec.PackSize,
                boxW           = r.Spec.W,
                boxL           = r.Spec.L,
                boxH           = r.Spec.H,
                maxLayers      = r.Spec.MaxLayers,
                condoCount     = r.Spec.CondoCount,
                weightPerBoxKg = r.Spec.WeightPerBoxKg,
                requestedQty   = r.Qty,
            }).ToList(),
            summary = new
            {
                totalPlacements       = output.Placements.Count,
                containerCbm          = Math.Round(cCbm,   4),
                usedCbm               = Math.Round(uCbm,   4),
                fillPct               = Math.Round(fill,   1),
                remainingDoorLengthCm = Math.Round(remain, 1),
            },
            packInfos = rows.Select(r => new
            {
                productIndex  = r.ProductIndex,
                label         = $"{r.Spec.Description} {r.Spec.Content}",
                requested     = r.Requested,
                totalPacked   = r.TotalPacked,
                hasPattern    = r.HasPattern,
                fullStacks    = r.FullStacks,
                mixedPlaced   = r.MixedPlaced,
                condoPlaced   = r.CondoPlaced,
                scatterPlaced = r.ScatterPlaced,
            }).ToList(),
            replayNote = "To reproduce exact placements: re-run PackingEngine.Calculate(container, requests) with the data above.",
        };

        return JsonSerializer.SerializeToUtf8Bytes(doc, JsonOptions.WriteIndented);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddEntry(ZipArchive zip, string name, byte[] data)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(data, 0, data.Length);
    }
}
