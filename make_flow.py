# -*- coding: utf-8 -*-
"""SLIMS end-to-end Visio-style cross-functional process flow -> JPG."""
from PIL import Image, ImageDraw
from flow_engine import *   # noqa

# ------------------------------------------------------------ canvas chrome
def draw_page_title():
    t1 = "STREET LIGHTING STORE, ISSUANCE & INSTALLATION MANAGEMENT SYSTEM (SLIMS)"
    t2 = "End-to-End Business Process Flow  —  Cross-Functional (Swimlane) Diagram"
    t3 = ("Procurement Inward (QR GRN)  ►  Store Location Stocking  ►  Digital Requisition & Approval  ►  "
          "QR Issuance to Site Location  ►  Geo-Tagged Installation Evidence  ►  Return of Out-of-Order Items  ►  MIS")
    d.rectangle([0, 0, W, 10], fill=C_HDR_FILL)
    w1, _ = tw(d, t1, F_TTL)
    d.text(((W - w1) / 2, 34), t1, font=F_TTL, fill=C_PAGE_TTL)
    w2, _ = tw(d, t2, F_SUB)
    d.text(((W - w2) / 2, 92), t2, font=F_SUB, fill=C_SUB)
    w3, _ = tw(d, t3, F_LEG)
    d.text(((W - w3) / 2, 128), t3, font=F_LEG, fill="#7A8794")


def draw_block(blk, title):
    top = block_top(blk)
    x0 = MARGIN_L
    x1 = W - MARGIN_R
    d.rectangle([x0, top, x1, top + BLK_TTL], fill=C_BLKT_FILL)
    d.text((x0 + 22, top + 12), title, font=F_BLK, fill="#FFFFFF")
    for i, name in enumerate(LANES):
        ly = top + BLK_TTL + i * LANE_H
        d.rectangle([x0, ly, x1, ly + LANE_H],
                    fill=(C_LANE_A if i % 2 == 0 else C_LANE_B))
        d.rectangle([x0, ly, x0 + HDR_W, ly + LANE_H], fill=C_HDR_FILL)
        lab = Image.new("RGB", (LANE_H - 16, HDR_W - 14), C_HDR_FILL)
        ld = ImageDraw.Draw(lab)
        center_text(ld, (LANE_H - 16) / 2, (HDR_W - 14) / 2, name, F_LANE,
                    LANE_H - 40, fill=C_HDR_TXT, lh=25)
        img.paste(lab.rotate(90, expand=True), (int(x0 + 7), int(ly + 8)))
        d.line([x0, ly, x1, ly], fill=C_LANE_LINE, width=2)
    d.rectangle([x0, top, x1, top + BLK_TTL + N_LANES * LANE_H],
                outline="#9BAEC2", width=3)
    d.line([x0 + HDR_W, top + BLK_TTL, x0 + HDR_W, top + BLK_TTL + N_LANES * LANE_H],
           fill="#9BAEC2", width=3)


draw_page_title()
draw_block(0, "PHASE 1 & 2   —   PROCUREMENT INWARD (QR-BASED GRN)   →   STORE STOCKING   →   DIGITAL REQUISITION & APPROVAL")
draw_block(1, "PHASE 3, 4 & 5   —   QR ISSUANCE TO SITE   →   FIELD INSTALLATION WITH GEO-TAGGED EVIDENCE   →   RETURN OF OUT-OF-ORDER ITEMS   →   MIS")

PRO, STO, SUP, OFF, SYS, PDO = 0, 1, 2, 3, 4, 5

# ============================================================== BLOCK A
a01 = Node(0, PRO, 0,  "START\nPurchase Order /\nWork Order issued", "start")
a02 = Node(0, SYS, 1,  "Article master created item-code wise (Article, UoM, Spec, Rate) mapped to PO No.", "sys")
a03 = Node(0, SYS, 2,  "Unique QR / barcode series generated & labels printed (Item Code + PO Ref + Serial)", "sys")
a04 = Node(0, PRO, 3,  "Vendor delivers consignment at store with Delivery Challan and Invoice")
a05 = Node(0, STO, 4,  "Store Keeper affixes label and SCANS QR at inward gate against PO")
a06 = Node(0, STO, 5,  "Qty, spec & PO match?", "dec")
a07 = Node(0, PRO, 5,  "Discrepancy / short-receipt note issued to vendor; penalty clause applied")
a08 = Node(0, STO, 6,  "GRN posted — items binned to Store ► Rack ► Bin location")
a09 = Node(0, SYS, 7,  "Stock ledger updated: store-location wise, PO wise, item-code wise", "sys")
a10 = Node(0, PDO, 8,  "GRN register & inward reports available to PD Office", "doc")
a11 = Node(0, SUP, 9,  "Supervisor raises DIGITAL REQUISITION — site location, item code, qty, need date")
a12 = Node(0, SYS, 10, "Stock available?", "dec")
a13 = Node(0, PRO, 10, "Auto purchase indent raised to Procurement for replenishment")
a14 = Node(0, PRO, 11, "To PO\nCycle", "off")
a15 = Node(0, PDO, 11, "Requisition approved?", "dec")
a16 = Node(0, SUP, 12, "Rejected — returned to supervisor with reason for revision")
a17 = Node(0, SYS, 12, "Stock reserved against requisition; Issue Slip auto-generated", "sys")
a18 = Node(0, SYS, 13, "A", "off")

BLOCK_A = [a01, a02, a03, a04, a05, a06, a07, a08, a09, a10,
           a11, a12, a13, a14, a15, a16, a17, a18]

# ============================================================== BLOCK B
b01 = Node(1, SYS, 0,  "A", "off")
b02 = Node(1, STO, 1,  "Store Keeper SCANS QR of each item against the approved requisition")
b03 = Node(1, SYS, 2,  "Scan valid?", "dec")
b04 = Node(1, STO, 2,  "Scan rejected — wrong item / excess qty / duplicate QR; re-scan")
b05 = Node(1, SYS, 3,  "Issue posted: stock moves Store → Site Location. Gate Pass & Issue Note generated", "sys")
b06 = Node(1, OFF, 4,  "Lighting Officer receives and acknowledges items on mobile app at site")
b07 = Node(1, OFF, 5,  "Captures BEFORE photo — GPS coordinates + date/time stamped on the image")
b08 = Node(1, OFF, 6,  "Installs fixture at pole / site point and SCANS item QR at the location")
b09 = Node(1, OFF, 7,  "Captures AFTER photo — GPS coordinates + date/time stamped on the image")
b10 = Node(1, SYS, 8,  "Inside site geo-fence?", "dec")
b11 = Node(1, SUP, 8,  "Exception flagged to Supervisor for field verification / re-capture")
b12 = Node(1, SYS, 9,  "Installation CONFIRMED — asset tagged to site, item status = INSTALLED", "sys")
b13 = Node(1, OFF, 10, "Out-of-order / dismantled items collected and returned to store")
b14 = Node(1, STO, 11, "Store receives return by QR scan — Return Note posted to quarantine")
b15 = Node(1, STO, 12, "Repairable / in warranty?", "dec")
b16 = Node(1, PRO, 12, "Sent to vendor for warranty replacement or repair; claim tracked")
b17 = Node(1, STO, 13, "Scrap / condemnation bin — disposal approval & scrap register")
b18 = Node(1, PRO, 13, "Repaired / replaced item re-inwarded through the GRN cycle")
b19 = Node(1, PRO, 14, "To GRN", "off")
b20 = Node(1, SYS, 14, "Ledgers updated: stock, asset register, scrap register, site history", "sys")
b21 = Node(1, PDO, 15, "Dashboards & reports — installation progress, geo-map, consumption vs PO, out-of-order analysis", "doc")
b22 = Node(1, PDO, 16, "END\nActivity report\npublished", "end")

BLOCK_B = [b01, b02, b03, b04, b05, b06, b07, b08, b09, b10, b11,
           b12, b13, b14, b15, b16, b17, b18, b19, b20, b21, b22]

# ============================================================== connectors
h_conn(a01, a02)
h_conn(a02, a03)
h_conn(a03, a04)
h_conn(a04, a05)
h_conn(a05, a06)
v_conn(a06, a07, "No")
h_back(a07, a04, "Replacement / top-up")
h_conn(a06, a08, "Yes")
h_conn(a08, a09)
h_conn(a09, a10, dash=True)
h_conn(a09, a11)
h_conn(a11, a12)
v_conn(a12, a13, "No")
h_conn(a13, a14)
h_conn(a12, a15, "Yes")
h_conn(a15, a16, "No")
h_back(a16, a11, "Revise & resubmit")
h_conn(a15, a17, "Yes")
h_conn(a17, a18)

h_conn(b01, b02)
h_conn(b02, b03)
v_conn(b03, b04, "No")
loop_back(b04, b02, "Re-scan")
h_conn(b03, b05, "Yes")
h_conn(b05, b06)
h_conn(b06, b07)
h_conn(b07, b08)
h_conn(b08, b09)
h_conn(b09, b10)
v_conn(b10, b11, "No")
cross_back(b11, b07, "Re-capture evidence")
h_conn(b10, b12, "Yes")
h_conn(b12, b13)
h_conn(b13, b14)
h_conn(b14, b15)
v_conn(b15, b16, "Yes")
h_conn(b16, b18)
h_conn(b18, b19)
h_conn(b15, b17, "No")
h_conn(b17, b20)
h_conn(b20, b21)
h_conn(b21, b22)

for n in BLOCK_A + BLOCK_B:
    draw_node(n)

# --------------------------------------------- off-page connector annotations
def note(x, y, text, anchor="l", col="#10739E"):
    w_, h_ = tw(d, text, F_LBL)
    x0 = x if anchor == "l" else x - w_
    d.text((x0, y - h_ / 2 - 2), text, font=F_LBL, fill=col)

note(a18.cx + a18.w / 2 + 14, a18.cy - 14, "continued at  A")
note(a18.cx + a18.w / 2 + 14, a18.cy + 12, "(next band, below)", col="#6B7C8C")
note(b01.cx - b01.w / 2, b01.cy - b01.h / 2 - 34, "continued from  A")
note(b01.cx - b01.w / 2, b01.cy + b01.h / 2 + 22, "(band above)", col="#6B7C8C")
note(a14.cx + a14.w / 2 + 14, a14.cy, "re-enters the PO cycle", col="#6B7C8C")
note(b19.cx + b19.w / 2 + 14, b19.cy, "re-enters GRN", col="#6B7C8C")

d.rectangle([MARGIN_L, block_top(1) - BLK_GAP + 12, W - MARGIN_R, block_top(1) - 14],
            fill="#EAF2FA", outline="#B1DDF0", width=2)
tie = ("OFF-PAGE CONNECTOR  A   —   the approved requisition, with stock reserved and the Issue Slip generated, "
       "continues in the band below at the issuance step.")
w_, _ = tw(d, tie, F_LEG)
d.text(((W - w_) / 2, block_top(1) - BLK_GAP + 22), tie, font=F_LEG, fill="#14375E")

# ------------------------------------------------------------ legend
ly = TITLE_H + 2 * BLOCK_H + BLK_GAP + 26
d.rectangle([MARGIN_L, ly, W - MARGIN_R, ly + 120], fill="#F5F8FB", outline="#9BAEC2", width=2)
d.text((MARGIN_L + 22, ly + 12), "LEGEND", font=F_BLK, fill=C_PAGE_TTL)

items = [("proc", "Manual / operational activity"),
         ("sys",  "System-automated step (SLIMS)"),
         ("dec",  "Decision / control point"),
         ("doc",  "Report or document output"),
         ("start", "Process start"),
         ("end",  "Process end"),
         ("off",  "Off-page connector")]
x = MARGIN_L + 200
for kind, text in items:
    fill, line = SH[kind]
    if kind == "dec":
        pts = [(x + 34, ly + 52), (x + 68, ly + 74), (x + 34, ly + 96), (x, ly + 74)]
        d.polygon(pts, fill=fill, outline=line)
        d.line(pts + [pts[0]], fill=line, width=3)
    elif kind in ("start", "end"):
        d.rounded_rectangle([x, ly + 58, x + 68, ly + 90], radius=16, fill=fill, outline=line, width=3)
    elif kind == "off":
        pts = [(x, ly + 58), (x + 52, ly + 58), (x + 68, ly + 74), (x + 52, ly + 90), (x, ly + 90)]
        d.polygon(pts, fill=fill, outline=line)
        d.line(pts + [pts[0]], fill=line, width=3)
    elif kind == "doc":
        d.rounded_rectangle([x, ly + 54, x + 68, ly + 84], radius=5, fill=fill, outline=line, width=3)
        wv = [(x, ly + 80), (x + 19, ly + 94), (x + 37, ly + 82), (x + 68, ly + 94),
              (x + 68, ly + 76), (x, ly + 76)]
        d.polygon(wv, fill=fill, outline=fill)
        d.line(wv[:4], fill=line, width=3)
        d.line([(x, ly + 76), (x, ly + 80)], fill=line, width=3)
        d.line([(x + 68, ly + 76), (x + 68, ly + 94)], fill=line, width=3)
    else:
        d.rounded_rectangle([x, ly + 58, x + 68, ly + 90], radius=7, fill=fill, outline=line, width=3)
    d.text((x + 80, ly + 64), text, font=F_LEG, fill="#2B3A47")
    x += 90 + tw(d, text, F_LEG)[0] + 74

d.line([(W - MARGIN_R - 470, ly + 74), (W - MARGIN_R - 360, ly + 74)], fill=C_ARROW, width=3)
head((W - MARGIN_R - 470, ly + 74), (W - MARGIN_R - 362, ly + 74))
d.text((W - MARGIN_R - 348, ly + 64), "Process flow", font=F_LEG, fill="#2B3A47")

foot = ("Business Requirements Document — Annexure A  —  Street Lighting Project  —  "
        "Every QR scan, GPS coordinate, photograph and ledger entry is immutably time-stamped and audit-logged.")
d.text((MARGIN_L + 4, H - 44), foot, font=F_FOOT, fill="#7A8794")
d.rectangle([0, H - 10, W, H], fill=C_HDR_FILL)

img.convert("RGB").save("SLIMS_Process_Flow.jpg", "JPEG", quality=92, subsampling=0, optimize=True)
print("saved", img.size)
