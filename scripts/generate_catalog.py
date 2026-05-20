#!/usr/bin/env python3
"""Generate product catalog HTML from products.json. Open output in browser -> Print -> Save as PDF."""

import json
from pathlib import Path
from datetime import date


def render_section(section):
    sub_rows = section.get("SubRows")
    if sub_rows:
        parts = ['<div class="section mixed">']
        parts.append('<div class="sec-label">Mixed</div>')
        for sr in sub_rows:
            r, c, rot = sr["Rows"], sr["Cols"], sr["Rotated"]
            cls = "cell-rot" if rot else "cell-norm"
            parts.append(f'<div class="sr-label">{r}×{c}{"↺" if rot else ""}</div>')
            parts.append('<div class="cell-grid">')
            for _ in range(r):
                parts.append('<div class="cell-row">')
                for _ in range(c):
                    parts.append(f'<div class="cell {cls}"></div>')
                parts.append("</div>")
            parts.append("</div>")
        parts.append("</div>")
        return "".join(parts)

    rows, cols, rot = section["Rows"], section["Cols"], section["Rotated"]
    cls_sec = "rotated" if rot else "normal"
    cls_cell = "cell-rot" if rot else "cell-norm"
    parts = [f'<div class="section {cls_sec}">']
    parts.append(f'<div class="sec-label">{rows}×{cols}{"↺" if rot else ""}</div>')
    parts.append('<div class="cell-grid">')
    for _ in range(rows):
        parts.append('<div class="cell-row">')
        for _ in range(cols):
            parts.append(f'<div class="cell {cls_cell}"></div>')
        parts.append("</div>")
    parts.append("</div>")
    parts.append("</div>")
    return "".join(parts)


def render_pattern(sections, label):
    if not sections:
        return f'<div class="pattern"><div class="pat-label">{label}</div><div class="no-pat">ไม่มีรูปแบบ</div></div>'
    parts = [f'<div class="pattern"><div class="pat-label">{label}</div><div class="pat-sections">']
    for i, sec in enumerate(sections):
        if i > 0:
            parts.append('<div class="arrow">→</div>')
        parts.append(render_section(sec))
    parts.append("</div></div>")
    return "".join(parts)


def build_card(p):
    name = p["Description"]
    content = p["Content"]
    pack = p["PackSize"]
    weight = p["WeightPerBoxKg"]
    w, l, h = p["W"], p["L"], p["H"]
    max_layers = p["MaxLayers"]
    pat_a = p.get("PatternA", [])
    pat_b = p.get("PatternB", [])

    boxes_a = sum(
        sum(sr["Rows"] * sr["Cols"] for sr in s["SubRows"]) if s.get("SubRows") else s["Rows"] * s["Cols"]
        for s in pat_a
    ) if pat_a else 0
    boxes_b = sum(
        sum(sr["Rows"] * sr["Cols"] for sr in s["SubRows"]) if s.get("SubRows") else s["Rows"] * s["Cols"]
        for s in pat_b
    ) if pat_b else 0

    return f"""
    <div class="card">
      <div class="card-header">
        <div>
          <span class="prod-name">{name}</span>
          <span class="prod-sub">{content} · {pack}</span>
        </div>
        <div class="max-layers">Max Layers: <b>{max_layers}</b></div>
      </div>
      <div class="specs">
        <div class="spec"><div class="sl">น้ำหนัก/กล่อง</div><div class="sv">{weight} kg</div></div>
        <div class="spec"><div class="sl">กว้าง (W)</div><div class="sv">{w} cm</div></div>
        <div class="spec"><div class="sl">ยาว (L)</div><div class="sv">{l} cm</div></div>
        <div class="spec"><div class="sl">สูง (H)</div><div class="sv">{h} cm</div></div>
        <div class="spec"><div class="sl">Pattern A กล่อง</div><div class="sv">{boxes_a if boxes_a else "-"}</div></div>
        <div class="spec"><div class="sl">Pattern B กล่อง</div><div class="sv">{boxes_b if boxes_b else "-"}</div></div>
      </div>
      <div class="patterns">
        {render_pattern(pat_a, "Pattern A")}
        {render_pattern(pat_b, "Pattern B")}
      </div>
    </div>"""


CSS = """
* { box-sizing: border-box; margin: 0; padding: 0; }
body { font-family: Arial, "Helvetica Neue", sans-serif; font-size: 11px; color: #222; background: #f5f5f5; }
h1 { text-align: center; font-size: 20px; font-weight: 700; margin: 18px 0 4px; color: #1a237e; }
.subtitle { text-align: center; color: #666; margin-bottom: 14px; font-size: 11px; }
.card {
  background: #fff;
  border: 1px solid #ddd;
  border-radius: 5px;
  margin: 8px 12px;
  padding: 10px 12px;
  page-break-inside: avoid;
}
.card-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  margin-bottom: 7px;
  padding-bottom: 6px;
  border-bottom: 1px solid #eee;
}
.prod-name { font-size: 13px; font-weight: 700; color: #1a237e; margin-right: 8px; }
.prod-sub { color: #555; font-size: 11px; }
.max-layers { font-size: 10px; color: #555; }
.specs {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
  margin-bottom: 8px;
}
.spec { min-width: 80px; }
.sl { font-size: 8.5px; color: #888; text-transform: uppercase; letter-spacing: 0.3px; }
.sv { font-weight: 700; font-size: 11px; }
.patterns { display: flex; gap: 10px; }
.pattern { flex: 1; background: #fafafa; border: 1px solid #e8e8e8; border-radius: 3px; padding: 6px; }
.pat-label { font-weight: 700; font-size: 9.5px; text-transform: uppercase; color: #555; margin-bottom: 5px; }
.pat-sections { display: flex; align-items: flex-start; gap: 4px; flex-wrap: wrap; }
.section { border-radius: 2px; padding: 3px; }
.section.normal { border: 1px solid #1976d2; background: #e3f2fd; }
.section.rotated { border: 1px solid #e65100; background: #fff3e0; }
.section.mixed { border: 1px dashed #7b1fa2; background: #f3e5f5; }
.sec-label { font-size: 7.5px; font-weight: 700; text-align: center; margin-bottom: 2px; color: #333; }
.sr-label { font-size: 7px; color: #666; }
.cell-grid { display: inline-flex; flex-direction: column; gap: 1px; }
.cell-row { display: flex; gap: 1px; }
.cell { width: 9px; height: 13px; border-radius: 1px; }
.cell-norm { background: #42a5f5; border: 1px solid #1976d2; }
.cell-rot  { background: #ffa726; border: 1px solid #e65100; }
.arrow { font-size: 13px; color: #bbb; align-self: center; }
.no-pat { color: #aaa; font-style: italic; font-size: 10px; }
.legend {
  display: flex; gap: 14px; align-items: center;
  margin: 0 12px 10px; font-size: 10px; color: #555;
}
.leg-box { width: 12px; height: 16px; display: inline-block; border-radius: 2px; margin-right: 4px; vertical-align: middle; }
@media print {
  body { background: #fff; }
  .card { margin: 4px 6px; border-color: #ccc; }
  @page { margin: 12mm; }
}
"""


def main():
    src = Path(__file__).parent / "products.json"
    out = Path(__file__).parent / "product_catalog.html"

    products = json.loads(src.read_text(encoding="utf-8"))
    cards = [build_card(p) for p in products]
    today = date.today().strftime("%d/%m/%Y")

    html = f"""<!DOCTYPE html>
<html lang="th">
<head>
  <meta charset="UTF-8">
  <title>Product Catalog</title>
  <style>{CSS}</style>
</head>
<body>
  <h1>Product Catalog — รายการสินค้า</h1>
  <p class="subtitle">สร้างเมื่อ {today} · {len(products)} รายการ</p>
  <div class="legend">
    <span><span class="leg-box" style="background:#42a5f5;border:1px solid #1976d2"></span>ปกติ (Normal)</span>
    <span><span class="leg-box" style="background:#ffa726;border:1px solid #e65100"></span>หมุน 90° (Rotated)</span>
    <span><span class="leg-box" style="background:#ce93d8;border:1px solid #7b1fa2"></span>Mixed SubRows</span>
  </div>
  {"".join(cards)}
</body>
</html>"""

    out.write_text(html, encoding="utf-8")
    print(f"Generated: {out}")
    print("Open in browser → File → Print → Save as PDF")


if __name__ == "__main__":
    main()
