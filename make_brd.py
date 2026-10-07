# -*- coding: utf-8 -*-
"""Assemble the SLIMS BRD: body sections + landscape Annexure A (process flow)."""
from docx_kit import *
from docx.shared import Pt, Cm
from docx.enum.section import WD_ORIENT, WD_SECTION
from docx.enum.text import WD_ALIGN_PARAGRAPH

import brd_body1   # noqa  (cover .. phase narrative)
import brd_body2   # noqa  (master data .. glossary)

# ------------------------------------------------- Annexure A : landscape
sec = doc.add_section(WD_SECTION.NEW_PAGE)
sec.orientation = WD_ORIENT.LANDSCAPE
sec.page_width, sec.page_height = Cm(29.7), Cm(21.0)
sec.left_margin = sec.right_margin = Cm(1.3)
sec.top_margin = Cm(1.2)
sec.bottom_margin = Cm(1.2)

H1("Annexure A — End-to-End Process Flow (Cross-Functional Swimlane Diagram)")
P("The diagram below is the visual counterpart of Section 8. It is drawn as a Visio-style cross-functional "
  "flowchart: each horizontal lane is a role, each shape is a step performed by that role, and the flow "
  "reads left to right. Band 1 covers procurement inward, store stocking and digital requisition; band 2 "
  "covers issuance, field installation with geo-tagged evidence, return of out-of-order items and MIS. "
  "The off-page connector 'A' links the two bands. The same diagram is supplied separately as a "
  "high-resolution JPG image (SLIMS_Process_Flow.jpg) for printing at A3 or larger.",
  size=9, align="j", space=4)

doc.add_picture("SLIMS_Process_Flow.jpg", width=Cm(27.0))
doc.paragraphs[-1].alignment = WD_ALIGN_PARAGRAPH.CENTER
CAPTION("Figure A-1: SLIMS end-to-end business process flow — QR-based GRN, store location stocking, "
        "digital requisition and approval, QR issuance to site, geo-tagged installation, returns and MIS.")

# ------------------------------------------------- Annexure B : portrait
sec2 = doc.add_section(WD_SECTION.NEW_PAGE)
sec2.orientation = WD_ORIENT.PORTRAIT
sec2.page_width, sec2.page_height = Cm(21.0), Cm(29.7)
sec2.left_margin = sec2.right_margin = Cm(2.0)
sec2.top_margin = sec2.bottom_margin = Cm(1.9)

H1("Annexure B — Step Reference for the Process Flow")
P("Each shape in Annexure A is listed below with the requirement that governs it, so that the diagram, the "
  "narrative in Section 8 and the requirements in Section 10 can be read together.", align="j")

TBL(["Band", "Lane", "Step", "Governing Requirements / Rules"],
    [["1", "Procurement", "Purchase order / work order issued", "INT-01, AS-01"],
     ["1", "System", "Article master created item-code wise, mapped to PO number", "FR-MD-05, BR-003, BR-004"],
     ["1", "System", "Unique QR / barcode series generated and labels printed", "FR-MD-06, FR-MD-07, BR-002"],
     ["1", "Procurement", "Vendor delivers consignment with challan and invoice", "FR-GR-09"],
     ["1", "Store", "Store Keeper affixes label and scans QR at inward gate against PO", "FR-GR-02, FR-GR-03"],
     ["1", "Store", "Decision: quantity, specification and PO match?", "FR-GR-03, FR-GR-04, BR-014"],
     ["1", "Procurement", "Discrepancy / short-receipt note issued to vendor", "FR-GR-05, FR-GR-06"],
     ["1", "Store", "GRN posted; items binned to store, rack and bin", "FR-GR-07, FR-GR-08, FR-GR-10, BR-005"],
     ["1", "System", "Stock ledger updated location-wise, PO-wise, item-code-wise", "FR-IC-01, FR-IC-02"],
     ["1", "PD Office", "GRN register and inward reports available", "RPT-01, FR-RP-01"],
     ["1", "Supervisor", "Digital requisition raised for a site location", "FR-RQ-01 to FR-RQ-04, BR-007, BR-008"],
     ["1", "System", "Decision: stock available?", "FR-RQ-05"],
     ["1", "Procurement", "Automatic purchase indent raised for shortfall", "FR-RQ-06, INT-02"],
     ["1", "PD Office", "Decision: requisition approved?", "FR-RQ-07, FR-RQ-08, BR-026"],
     ["1", "Supervisor", "Rejected requisition returned for revision", "FR-RQ-09"],
     ["1", "System", "Stock reserved; Issue Slip generated", "FR-RQ-10, FR-RQ-11, BR-009"],
     ["2", "Store", "Store Keeper scans QR of each item against approved requisition", "FR-IS-01, FR-IS-02, BR-006"],
     ["2", "System", "Decision: scan valid?", "FR-IS-03, FR-IS-04, FR-IS-05, BR-010, BR-011"],
     ["2", "Store", "Scan rejected; re-scan", "FR-IS-03"],
     ["2", "System", "Issue posted; custody to site location; gate pass and issue note", "FR-IS-06, FR-IS-07, BR-012"],
     ["2", "Officer", "Material received and acknowledged at site on mobile", "FR-IS-09"],
     ["2", "Officer", "BEFORE photo captured with GPS and time stamped on image", "FR-IN-02 to FR-IN-04, BR-015, BR-016"],
     ["2", "Officer", "Fixture installed; item QR scanned at the location", "FR-IN-05"],
     ["2", "Officer", "AFTER photo captured with GPS and time stamped on image", "FR-IN-06"],
     ["2", "System", "Decision: inside site geo-fence?", "FR-IN-07, BR-013, Section 13"],
     ["2", "Supervisor", "Exception flagged for field verification / re-capture", "FR-IN-09, FR-IN-10, BR-017"],
     ["2", "System", "Installation confirmed; asset tagged; status INSTALLED", "FR-IN-08, FR-IN-11, FR-IN-15"],
     ["2", "Officer", "Out-of-order / dismantled items collected and returned", "FR-RT-01, FR-RT-10, BR-029"],
     ["2", "Store", "Return received by QR scan; Return Note into quarantine", "FR-RT-02, FR-RT-03, BR-018"],
     ["2", "Store", "Decision: repairable / in warranty?", "FR-RT-04, FR-RT-05, BR-019"],
     ["2", "Procurement", "Warranty replacement or repair; claim tracked", "FR-RT-06, INT-02"],
     ["2", "Procurement", "Repaired / replaced item re-inwarded through GRN", "FR-RT-07, FR-GR-12"],
     ["2", "Store", "Scrap / condemnation bin; disposal approval; scrap register", "FR-RT-08, FR-RT-09, BR-020"],
     ["2", "System", "Ledgers updated: stock, asset, scrap, site history", "FR-IC-01, FR-IC-09, FR-RT-12"],
     ["2", "PD Office", "Dashboards and reports generated automatically", "FR-RP-01 to FR-RP-06, Section 14"],
     ["2", "PD Office", "Activity report published", "FR-IN-15, RPT-06, RPT-20"]],
    widths=[1.2, 2.4, 7.0, 5.4], font=8.5, hdr_font=8.5, align_center=[0])

P("")
P("— End of Document —", align="c", italic=True, size=9)

# ------------------------------------------------- footers on all sections
for s in doc.sections:
    s.footer.is_linked_to_previous = False
    footer_text(s, "Business Requirements Document — SLIMS  |  Government Street Lighting Project  |  "
                   "Ref. SLP/BRD/SLIMS/2026/01  |  Version 1.0  |  Official")
    add_page_numbers(s)

out = "SLIMS_Business_Requirements_Document.docx"
doc.save(out)
print("saved", out)
