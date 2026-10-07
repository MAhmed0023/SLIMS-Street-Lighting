# -*- coding: utf-8 -*-
"""Visio-style cross-functional (swimlane) process flow engine."""
from PIL import Image, ImageDraw, ImageFont

HDR_W    = 252
PITCH    = 268
BOX_W    = 232
BOX_H    = 118
LANE_H   = 212
N_COLS   = 17
MARGIN_L = 34
MARGIN_R = 34
TITLE_H  = 168
BLK_TTL  = 54
BLK_GAP  = 78
LEGEND_H = 176
FOOT_H   = 62

LANES = ["PROCUREMENT\n& VENDOR",
         "CENTRAL / ZONAL\nSTORE",
         "LIGHTING\nSUPERVISOR",
         "LIGHTING OFFICER\n(FIELD)",
         "SLIMS SYSTEM\n(AUTOMATED)",
         "PD OFFICE /\nMIS & AUDIT"]
N_LANES = len(LANES)

W = MARGIN_L + HDR_W + N_COLS * PITCH + MARGIN_R
BLOCK_H = BLK_TTL + N_LANES * LANE_H
H = TITLE_H + BLOCK_H + BLK_GAP + BLOCK_H + LEGEND_H + FOOT_H

C_BG        = "#FFFFFF"
C_PAGE_TTL  = "#14375E"
C_SUB       = "#5A6B7B"
C_HDR_FILL  = "#1F4E79"
C_HDR_TXT   = "#FFFFFF"
C_BLKT_FILL = "#2E74B5"
C_LANE_A    = "#FAFCFE"
C_LANE_B    = "#EFF4FA"
C_LANE_LINE = "#B7C6D6"
C_ARROW     = "#3B4650"
C_LBL       = "#B03A2E"

SH = {
    "proc":  ("#DAE8FC", "#2E74B5"),
    "sys":   ("#E4D7EE", "#7D5BA6"),
    "dec":   ("#FFF2CC", "#C9A227"),
    "start": ("#D5E8D4", "#5E9B52"),
    "end":   ("#F8CECC", "#B85450"),
    "doc":   ("#FFE6CC", "#D79B00"),
    "off":   ("#B1DDF0", "#10739E"),
}


def f(path, size):
    try:
        return ImageFont.truetype(path, size)
    except Exception:
        return ImageFont.load_default()


SEG  = "C:/Windows/Fonts/segoeui.ttf"
SEGB = "C:/Windows/Fonts/segoeuib.ttf"
F_TTL  = f(SEGB, 46)
F_SUB  = f(SEG,  23)
F_BLK  = f(SEGB, 25)
F_LANE = f(SEGB, 19)
F_NODE = f(SEG,  16)
F_NODB = f(SEGB, 17)
F_DEC  = f(SEGB, 16)
F_LBL  = f(SEGB, 15)
F_LEG  = f(SEG,  17)
F_FOOT = f(SEG,  16)

img = Image.new("RGB", (W, H), C_BG)
d = ImageDraw.Draw(img)


def tw(draw, s, font):
    b = draw.textbbox((0, 0), s, font=font)
    return b[2] - b[0], b[3] - b[1]


def wrap(draw, text, font, max_w):
    out = []
    for para in text.split("\n"):
        words, line = para.split(), ""
        for word in words:
            t = (line + " " + word).strip()
            if tw(draw, t, font)[0] <= max_w or not line:
                line = t
            else:
                out.append(line)
                line = word
        out.append(line)
    return out


def center_text(draw, cx, cy, text, font, max_w, fill="#16202B", lh=21):
    lines = wrap(draw, text, font, max_w)
    y = cy - (len(lines) * lh) / 2 + 1
    for ln in lines:
        w_, _ = tw(draw, ln, font)
        draw.text((cx - w_ / 2, y), ln, font=font, fill=fill)
        y += lh


def block_top(blk):
    return TITLE_H if blk == 0 else TITLE_H + BLOCK_H + BLK_GAP


# each block stretches its own columns across the full lane width so that
# no block is left with dead space on the right
BLOCK_COLS = {0: 14, 1: 17}
USABLE_W = W - MARGIN_L - HDR_W - MARGIN_R


def block_pitch(blk):
    return USABLE_W / BLOCK_COLS[blk]


class Node:
    def __init__(self, blk, lane, col, text, kind="proc"):
        self.blk, self.lane, self.col = blk, lane, col
        self.text, self.kind = text, kind
        p = block_pitch(blk)
        self.cx = MARGIN_L + HDR_W + col * p + p / 2
        self.cy = block_top(blk) + BLK_TTL + lane * LANE_H + LANE_H / 2
        if kind == "dec":
            self.w, self.h = BOX_W + 28, BOX_H + 36
        elif kind == "off":
            self.w, self.h = 108, 76
        else:
            self.w, self.h = BOX_W, BOX_H

    @property
    def l(self): return self.cx - self.w / 2

    @property
    def r(self): return self.cx + self.w / 2

    @property
    def t(self): return self.cy - self.h / 2

    @property
    def b(self): return self.cy + self.h / 2

    def lane_top(self):
        return block_top(self.blk) + BLK_TTL + self.lane * LANE_H

    def lane_bottom(self):
        return self.lane_top() + LANE_H


def draw_node(n):
    fill, line = SH[n.kind]
    if n.kind in ("proc", "sys"):
        d.rounded_rectangle([n.l, n.t, n.r, n.b], radius=12, fill=fill, outline=line, width=3)
        d.line([n.l + 4, n.t + 6, n.l + 4, n.b - 6], fill=line, width=6)
        center_text(d, n.cx + 3, n.cy, n.text, F_NODE, n.w - 28)
    elif n.kind == "dec":
        pts = [(n.cx, n.t), (n.r, n.cy), (n.cx, n.b), (n.l, n.cy)]
        d.polygon(pts, fill=fill, outline=line)
        d.line(pts + [pts[0]], fill=line, width=3)
        center_text(d, n.cx, n.cy, n.text, F_DEC, n.w * 0.60, lh=20)
    elif n.kind in ("start", "end"):
        d.rounded_rectangle([n.l, n.t + 16, n.r, n.b - 16], radius=(n.h - 32) / 2,
                            fill=fill, outline=line, width=3)
        center_text(d, n.cx, n.cy, n.text, F_NODB, n.w - 36, lh=22)
    elif n.kind == "doc":
        bot = n.b - 18
        d.rounded_rectangle([n.l, n.t, n.r, bot], radius=6, fill=fill, outline=line, width=3)
        wave = [(n.l, bot - 8), (n.l + n.w * .28, bot + 14), (n.l + n.w * .55, bot - 4),
                (n.r, bot + 12), (n.r, bot - 12), (n.l, bot - 12)]
        d.polygon(wave, fill=fill, outline=fill)
        d.line(wave[:4], fill=line, width=3)
        d.line([(n.l, bot - 12), (n.l, bot - 8)], fill=line, width=3)
        d.line([(n.r, bot - 12), (n.r, bot + 12)], fill=line, width=3)
        center_text(d, n.cx, n.cy - 10, n.text, F_NODE, n.w - 28)
    elif n.kind == "off":
        pts = [(n.l, n.t), (n.r - 24, n.t), (n.r, n.cy), (n.r - 24, n.b), (n.l, n.b)]
        d.polygon(pts, fill=fill, outline=line)
        d.line(pts + [pts[0]], fill=line, width=3)
        center_text(d, n.cx - 8, n.cy, n.text, F_NODB, n.w - 24)


AH = 13


def head(p, q, col=C_ARROW):
    (x1, y1), (x2, y2) = p, q
    if abs(x1 - x2) < 1:
        s = 1 if y2 > y1 else -1
        d.polygon([(x2, y2), (x2 - AH * .72, y2 - s * AH), (x2 + AH * .72, y2 - s * AH)], fill=col)
    else:
        s = 1 if x2 > x1 else -1
        d.polygon([(x2, y2), (x2 - s * AH, y2 - AH * .72), (x2 - s * AH, y2 + AH * .72)], fill=col)


def label_at(x, y, text):
    w_, h_ = tw(d, text, F_LBL)
    d.rectangle([x - w_ / 2 - 7, y - h_ / 2 - 6, x + w_ / 2 + 7, y + h_ / 2 + 7],
                fill="#FFFFFF", outline="#FFFFFF")
    d.text((x - w_ / 2, y - h_ / 2 - 2), text, font=F_LBL, fill=C_LBL)


def poly(pts, label=None, col=C_ARROW, dash=False, lx=None, ly=None, wdt=3):
    if dash:
        for i in range(len(pts) - 1):
            x1, y1 = pts[i]
            x2, y2 = pts[i + 1]
            seg = max(int((abs(x2 - x1) + abs(y2 - y1)) / 16), 1)
            for k in range(seg):
                if k % 2:
                    continue
                a = (x1 + (x2 - x1) * k / seg, y1 + (y2 - y1) * k / seg)
                b = (x1 + (x2 - x1) * (k + .55) / seg, y1 + (y2 - y1) * (k + .55) / seg)
                d.line([a, b], fill=col, width=wdt)
    else:
        d.line(pts, fill=col, width=wdt, joint="curve")
    head(pts[-2], pts[-1], col)
    if label:
        x = lx if lx is not None else (pts[0][0] + pts[1][0]) / 2
        y = ly if ly is not None else (pts[0][1] + pts[1][1]) / 2
        label_at(x, y, label)


def h_conn(a, b, label=None, dash=False):
    x1, x2 = a.r, b.l
    mx = (x1 + x2) / 2
    if abs(a.cy - b.cy) < 2:
        # in-lane hop: the gap between shapes is too narrow for a label, so it
        # is floated in the clear band above them
        poly([(x1, a.cy), (x2, b.cy)], label, dash=dash,
             lx=(x1 + x2) / 2, ly=min(a.t, b.t) - 20)
    else:
        poly([(x1, a.cy), (mx, a.cy), (mx, b.cy), (x2, b.cy)], label, dash=dash,
             lx=mx + 40, ly=a.cy - 18)


def h_back(a, b, label=None):
    poly([(a.l, a.cy), (b.r, b.cy)], label,
         lx=(a.l + b.r) / 2, ly=a.cy - 24)


def v_conn(a, b, label=None):
    # the branch label sits next to the source shape, Visio-style, rather than
    # at the midpoint of a long cross-lane connector where it would collide
    if b.cy < a.cy:
        poly([(a.cx, a.t), (b.cx, b.b)], label, lx=a.cx + 46, ly=a.t - 38)
    else:
        poly([(a.cx, a.b), (b.cx, b.t)], label, lx=a.cx + 46, ly=a.b + 38)


def loop_back(a, b, label=None, drop=None):
    y = a.lane_bottom() - 22 if drop is None else drop
    poly([(a.cx, a.b), (a.cx, y), (b.cx, y), (b.cx, b.b)], label,
         lx=(a.cx + b.cx) / 2, ly=y - 16)


def cross_back(a, b, label=None, y=None):
    yy = b.lane_top() + 24 if y is None else y
    start = (a.cx, a.b) if yy > a.cy else (a.cx, a.t)
    poly([start, (a.cx, yy), (b.cx, yy), (b.cx, b.t)], label,
         lx=(a.cx + b.cx) / 2, ly=yy - 17)
