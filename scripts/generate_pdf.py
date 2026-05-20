#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Generate box-arrangement PDF from JSON data exported by the logistic app.
Usage: python3 generate_pdf.py <input.json> <output.pdf>
Requires: pip install reportlab
"""

import json
import os
import sys
from datetime import datetime

from reportlab.lib import colors
from reportlab.lib.enums import TA_RIGHT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import cm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import (HRFlowable, Paragraph, SimpleDocTemplate,
                                 Spacer)
from reportlab.platypus.flowables import Flowable

# ── Palette (matches IsometricCanvas / PdfExporter) ──────────────────────────

PALETTE = [
    (0x3B / 255, 0x82 / 255, 0xF6 / 255),
    (0xEF / 255, 0x44 / 255, 0x44 / 255),
    (0x22 / 255, 0xC5 / 255, 0x5E / 255),
    (0xF5 / 255, 0x9E / 255, 0x0B / 255),
    (0xA8 / 255, 0x55 / 255, 0xF7 / 255),
    (0xEC / 255, 0x48 / 255, 0x99 / 255),
    (0x14 / 255, 0xB8 / 255, 0xA6 / 255),
    (0xF9 / 255, 0x73 / 255, 0x16 / 255),
]

# ── Font setup ────────────────────────────────────────────────────────────────

FONT = "Helvetica"
FONT_BOLD = "Helvetica-Bold"


def _setup_fonts():
    global FONT, FONT_BOLD
    candidates = [
        "/Library/Fonts/Arial Unicode.ttf",
        "/Library/Fonts/Arial Unicode MS.ttf",
        "/System/Library/Fonts/Supplemental/Arial Unicode MS.ttf",
        "C:\\Windows\\Fonts\\arialuni.ttf",
    ]
    for path in candidates:
        if not os.path.exists(path):
            continue
        try:
            pdfmetrics.registerFont(TTFont("ArialUnicode", path))
            FONT = "ArialUnicode"
            FONT_BOLD = "ArialUnicode"
            return
        except Exception:
            continue


def _pal(pi):
    return PALETTE[pi % len(PALETTE)]


def _hex(pi):
    r, g, b = _pal(pi)
    return f"#{int(r * 255):02X}{int(g * 255):02X}{int(b * 255):02X}"


# ── Product card Flowable ─────────────────────────────────────────────────────

class ProductCard(Flowable):
    PAD = 10      # inner padding
    GAP = 12      # gap between pattern A and B columns
    MIN_DIAG = 28 # minimum diagram height (points)

    def __init__(self, prod, placements, container_w, condo_base, mixed_base, avail_w):
        Flowable.__init__(self)
        self.prod = prod
        self.container_w = container_w
        self.condo_base = condo_base
        self.mixed_base = mixed_base
        self.width = avail_w

        pi = prod["index"]
        primary = [
            p for p in placements
            if p["productIndex"] == pi and p["stackIndex"] < condo_base
        ]
        self.condo_boxes = [
            p for p in placements
            if p["productIndex"] == pi
            and condo_base <= p["stackIndex"] < mixed_base
        ]

        even_si = sorted({p["stackIndex"] for p in primary if p["stackIndex"] % 2 == 0})
        if even_si:
            fe = even_si[0]
            self.pat_a = [p for p in primary if p["stackIndex"] == fe and p["layerIndex"] == 0]
            self.pat_b = [p for p in primary if p["stackIndex"] == fe and p["layerIndex"] == 1]
        else:
            self.pat_a, self.pat_b = [], []
        self.has_b = bool(self.pat_b)

        inner = avail_w - 2 * self.PAD
        self.diag_w = (inner - self.GAP) / 2
        self.diag_h = max(
            self._boxes_h(self.pat_a, self.diag_w),
            self._boxes_h(self.pat_b, self.diag_w) if self.has_b else 0,
            self.MIN_DIAG,
        )
        self.condo_diag_w = inner
        self.condo_h = self._boxes_h(self.condo_boxes, inner) if self.condo_boxes else 0

        H = (self.PAD   # top
             + 18       # header
             + 6        # gap
             + 1        # divider
             + 6        # gap
             + 12       # pattern labels
             + 4        # gap
             + self.diag_h
             + 4        # gap
             + 11       # box-count labels
             + 8        # gap
             + 1        # divider
             + 7        # gap
             + 13       # stats
             + self.PAD # bottom
             )
        if self.condo_boxes:
            H += 8 + 11 + 4 + self.condo_h + 4
        self.height = H

    # ── helpers ──────────────────────────────────────────────────────────────

    def _boxes_h(self, boxes, width):
        if not boxes:
            return 0
        min_y = min(b["y"] for b in boxes)
        max_y = max(b["y"] + b["bl"] for b in boxes)
        return (max_y - min_y) * (width / self.container_w)

    def _draw_diagram(self, c, boxes, pi, x0, y0, w, h):
        """Draw top-down footprint diagram. (x0,y0) = bottom-left in flowable coords."""
        r, g, b = _pal(pi)
        # Background box
        c.setFillColor(colors.Color(0.975, 0.98, 0.99))
        c.setStrokeColor(colors.Color(0.80, 0.84, 0.88))
        c.setLineWidth(1.0)
        c.roundRect(x0, y0, w, h, 3, fill=1, stroke=1)
        if not boxes:
            return
        scale = w / self.container_w
        min_y = min(bx["y"] for bx in boxes)
        for bx in boxes:
            rx = x0 + bx["x"] * scale
            top_off = (bx["y"] - min_y) * scale
            rw = max(bx["bw"] * scale, 0.5)
            rl = max(bx["bl"] * scale, 0.5)
            pdf_y = y0 + h - top_off - rl   # flip y axis
            alpha = 0.55 if bx["rotated"] else 0.82
            c.setFillColor(colors.Color(r, g, b, alpha))
            c.setStrokeColor(colors.Color(r * 0.45, g * 0.45, b * 0.45))
            c.setLineWidth(0.6)
            c.roundRect(rx, pdf_y, rw, rl, 1, fill=1, stroke=1)
            if bx["rotated"] and rw > 10 and rl > 10:
                fs = max(min(rw, rl) * 0.30, 5)
                c.setFillColor(colors.Color(1, 1, 1, 0.88))
                c.setFont(FONT, fs)
                c.drawCentredString(rx + rw / 2, pdf_y + rl * 0.28, "R")

    # ── draw ─────────────────────────────────────────────────────────────────

    def draw(self):
        c = self.canv
        pi = self.prod["index"]
        r, g, b = _pal(pi)
        w = self.width
        h = self.height
        pad = self.PAD
        inner_w = w - 2 * pad
        diag_w = self.diag_w
        diag_h = self.diag_h

        # Card border
        c.setFillColor(colors.white)
        c.setStrokeColor(colors.Color(0.86, 0.89, 0.92))
        c.setLineWidth(0.8)
        c.roundRect(0, 0, w, h, 6, fill=1, stroke=1)

        # Left accent bar
        c.setFillColor(colors.Color(r, g, b))
        c.setLineWidth(0)
        c.roundRect(0, 0, 4, h, 3, fill=1, stroke=0)

        y = h - pad  # cursor (top → bottom)

        # ── Header ───────────────────────────────────────────────────────────
        y -= 16
        c.setFillColor(colors.Color(r, g, b))
        c.roundRect(pad + 4, y + 1, 11, 11, 2, fill=1, stroke=0)

        name_p = Paragraph(
            f'<font color="{_hex(pi)}"><b>{self.prod["description"]} {self.prod["content"]}</b></font>',
            ParagraphStyle("cn", fontName=FONT, fontSize=12, leading=14),
        )
        name_p.wrapOn(c, inner_w - 95, 20)
        name_p.drawOn(c, pad + 20, y - 2)

        dim_p = Paragraph(
            f'{self.prod["w"]:.0f}×{self.prod["l"]:.0f}×{self.prod["h"]:.0f} cm',
            ParagraphStyle("cd", fontName=FONT, fontSize=9,
                           textColor=colors.Color(0.58, 0.63, 0.70), alignment=TA_RIGHT),
        )
        dim_p.wrapOn(c, 95, 20)
        dim_p.drawOn(c, w - pad - 95, y - 2)

        y -= 6
        c.setStrokeColor(colors.Color(0.90, 0.92, 0.95))
        c.setLineWidth(0.5)
        c.line(pad, y, w - pad, y)
        y -= 1
        y -= 6

        # ── Pattern labels ────────────────────────────────────────────────────
        y -= 12
        c.setFont(FONT, 8)
        c.setFillColor(colors.Color(0.29, 0.34, 0.43))
        c.drawString(pad, y, "ชั้นคี่  (Pattern A)")
        b_x = pad + diag_w + self.GAP
        if self.has_b:
            c.drawString(b_x, y, "ชั้นคู่  (Pattern B)")
        else:
            c.setFillColor(colors.Color(0.70, 0.74, 0.80))
            c.drawString(b_x, y, "ชั้นคู่  (Pattern B)")

        y -= 4

        # ── Diagrams ─────────────────────────────────────────────────────────
        y -= diag_h
        diag_bot = y
        self._draw_diagram(c, self.pat_a, pi, pad, diag_bot, diag_w, diag_h)

        if self.has_b:
            self._draw_diagram(c, self.pat_b, pi, b_x, diag_bot, diag_w, diag_h)
        else:
            c.setFillColor(colors.Color(0.95, 0.96, 0.97))
            c.setStrokeColor(colors.Color(0.87, 0.89, 0.92))
            c.setLineWidth(0.5)
            c.roundRect(b_x, diag_bot, diag_w, diag_h, 3, fill=1, stroke=1)
            c.setFont(FONT, 9)
            c.setFillColor(colors.Color(0.68, 0.73, 0.80))
            c.drawCentredString(b_x + diag_w / 2, diag_bot + diag_h / 2 - 4, "เหมือน Pattern A")

        y -= 4

        # ── Box count labels ──────────────────────────────────────────────────
        y -= 11
        c.setFont(FONT, 8)
        c.setFillColor(colors.Color(0.58, 0.63, 0.70))
        c.drawString(pad, y, f'{len(self.pat_a)} กล่อง/ชั้น')
        if self.has_b:
            c.drawString(b_x, y, f'{len(self.pat_b)} กล่อง/ชั้น')
        else:
            c.setFillColor(colors.Color(0.72, 0.76, 0.82))
            c.drawString(b_x, y, "เหมือน Pattern A")

        y -= 8
        c.setStrokeColor(colors.Color(0.91, 0.93, 0.96))
        c.setLineWidth(0.4)
        c.line(pad, y, w - pad, y)
        y -= 1
        y -= 7

        # ── Stats row ────────────────────────────────────────────────────────
        y -= 13
        sc = self.prod["stackCount"]
        ly_min, ly_max = self.prod["layerMin"], self.prod["layerMax"]
        layer_desc = (f"{ly_max} ชั้น" if (ly_min == ly_max or sc == 0)
                      else f"{ly_min}–{ly_max} ชั้น")
        primary_cnt = (self.prod["totalPacked"]
                       - self.prod["condoPlaced"]
                       - self.prod["scatterPlaced"])
        full = self.prod["totalPacked"] >= self.prod["requested"]
        total_col = "#16A34A" if full else "#D97706"

        parts = [f"<b>หลัก</b>  {sc} ตั้ง × {layer_desc} = {primary_cnt} กล่อง"]
        if self.prod["condoPlaced"] > 0:
            parts.append(f'<b>คอนโด</b>  {self.prod["condoPlaced"]} กล่อง')
        if self.prod["scatterPlaced"] > 0:
            parts.append(f'<b>กระจาย</b>  {self.prod["scatterPlaced"]} กล่อง')
        parts.append(
            f'<b>รวม</b>  <font color="{total_col}">'
            f'{self.prod["totalPacked"]}/{self.prod["requested"]} ลัง</font>'
        )
        stat_p = Paragraph(
            "   ".join(parts),
            ParagraphStyle("st", fontName=FONT, fontSize=9, leading=11,
                           textColor=colors.Color(0.20, 0.25, 0.34)),
        )
        stat_p.wrapOn(c, inner_w, 20)
        stat_p.drawOn(c, pad, y - 2)

        # ── Condo section ────────────────────────────────────────────────────
        if self.condo_boxes:
            y -= 8
            y -= 11
            c.setFont(FONT, 8)
            c.setFillColor(colors.Color(0.29, 0.34, 0.43))
            c.drawString(pad, y, "คอนโด")
            y -= 4
            y -= self.condo_h
            self._draw_diagram(c, self.condo_boxes, pi, pad, y, self.condo_diag_w, self.condo_h)
            y -= 4


# ── CBM bar Flowable ─────────────────────────────────────────────────────────

class CbmBar(Flowable):
    def __init__(self, used, total, width, height=8):
        Flowable.__init__(self)
        self.used = used
        self.total = total
        self.width = width
        self.height = height

    def draw(self):
        c = self.canv
        w, h = self.width, self.height
        pct = min(self.used / self.total, 1.0) if self.total > 0 else 0
        # Track
        c.setFillColor(colors.Color(0.91, 0.93, 0.96))
        c.roundRect(0, 0, w, h, h / 2, fill=1, stroke=0)
        # Fill
        fill_w = max(pct * w, h)
        col = colors.Color(0.22, 0.77, 0.37) if pct < 0.95 else colors.Color(0.94, 0.27, 0.27)
        c.setFillColor(col)
        c.roundRect(0, 0, fill_w, h, h / 2, fill=1, stroke=0)


# ── Document generation ───────────────────────────────────────────────────────

def generate(data: dict, output_path: str):
    _setup_fonts()

    container = data["container"]
    stats_data = data["stats"]
    products = [p for p in data["products"] if p["hasPattern"]]
    placements = data["placements"]
    condo_base = data.get("condoStackBase", 1000)
    mixed_base = data.get("mixedStackBase", 2000)

    container_w = container["interiorW"]
    cbm_total = stats_data["containerCbm"]
    cbm_used = stats_data["usedCbm"]
    pct = cbm_used / cbm_total * 100 if cbm_total > 0 else 0
    today = datetime.now().strftime("%d/%m/%Y")

    doc = SimpleDocTemplate(
        output_path,
        pagesize=A4,
        leftMargin=1.5 * cm,
        rightMargin=1.5 * cm,
        topMargin=1.5 * cm,
        bottomMargin=1.5 * cm,
    )
    avail_w = A4[0] - 3 * cm

    s_title = ParagraphStyle("title", fontName=FONT_BOLD, fontSize=17, leading=21,
                              textColor=colors.Color(0.118, 0.161, 0.235))
    s_sub = ParagraphStyle("sub", fontName=FONT, fontSize=10, leading=14,
                            textColor=colors.Color(0.392, 0.455, 0.545))

    story = []

    # Header
    story.append(Paragraph("รายงานการจัดเรียงสินค้า", s_title))
    story.append(Spacer(1, 3))
    story.append(Paragraph(
        f"{container['name']}  ({container['sizeLabel']})  &nbsp;&nbsp;"
        f" วันที่ {today}  &nbsp;&nbsp;"
        f" CBM {cbm_used:.2f} / {cbm_total:.2f} m³  ({pct:.0f}%)",
        s_sub,
    ))
    story.append(Spacer(1, 5))
    story.append(CbmBar(cbm_used, cbm_total, float(avail_w), height=6))
    story.append(Spacer(1, 6))
    story.append(HRFlowable(width="100%", thickness=1.5,
                             color=colors.Color(0.886, 0.906, 0.937)))
    story.append(Spacer(1, 10))

    # Product cards
    for prod in products:
        card = ProductCard(prod, placements, container_w, condo_base, mixed_base, float(avail_w))
        story.append(card)
        story.append(Spacer(1, 8))

    def _footer(canvas, doc):
        canvas.saveState()
        canvas.setFont(FONT, 8)
        canvas.setFillColor(colors.Color(0.60, 0.65, 0.72))
        canvas.drawCentredString(A4[0] / 2, 0.9 * cm, f"หน้า {doc.page}")
        canvas.restoreState()

    doc.build(story, onFirstPage=_footer, onLaterPages=_footer)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <input.json> <output.pdf>", file=sys.stderr)
        sys.exit(1)
    with open(sys.argv[1], encoding="utf-8") as f:
        payload = json.load(f)
    generate(payload, sys.argv[2])
