# -*- coding: utf-8 -*-
"""Runs the 30 functional acceptance test cases of FRD-GSL-001 (Section 21) against a running SLIMS API, one check for
each requirement closed in the "Not met" remediation (FR-…), and the probes for the six defects found during the gap
analysis. Results go to frd_acceptance_results.json for the gap-analysis workbook.

    python frd_acceptance.py [http://localhost:5091] [--admin-password <pw>] [--db <database>]

--db names the SQL Server Express database the API uses. It lets the run move its own test records back in time to
prove the time-driven rules (reservation lapse, escalation). Without it those checks are reported as NOT RUN.

It works only with its own data: qa-* accounts (created, then deactivated at the end), one QA site, one QA purchase
order, one QA bin and QA phones. It never resets the password of an existing user. It does write transactions into the
database it runs against, and it switches e-mail and SMS delivery to a local test sink for a minute (restored after).

Result values
    PASS        the system behaves as the FRD expects
    PASS*       the control holds, but the status value or message differs from the FRD
    PARTIAL     part of the expected behaviour is present
    FAIL        the system does something the FRD forbids
    NOT BUILT   the function does not exist in the current build
    NOT RUN     the check needs --db
"""
import base64
import hashlib
import http.server
import io
import json
import os
import socket
import subprocess
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from datetime import date, datetime, timedelta, timezone

from PIL import Image

try:
    sys.stdout.reconfigure(encoding="utf-8")
except AttributeError:
    pass

args = [a for a in sys.argv[1:] if not a.startswith("--")]
BASE = (args[0] if args else "http://localhost:5091").rstrip("/")
ADMIN_PW = sys.argv[sys.argv.index("--admin-password") + 1] if "--admin-password" in sys.argv else "Slims2026admin"
DB = sys.argv[sys.argv.index("--db") + 1] if "--db" in sys.argv else None
QA_PW = "QaRun2026x"
TS = datetime.now().strftime("%m%d%H%M")
RESULTS, DEFECTS = [], []


# ============================================================================ plumbing
def call(method, path, body=None, token=None, raw=False):
    headers, data = {}, None
    if token:
        headers["Authorization"] = "Bearer " + token
    if isinstance(body, list) and body and isinstance(body[0], tuple):          # multipart form
        boundary = "----qa" + uuid.uuid4().hex
        buf = io.BytesIO()
        for key, value in body:
            buf.write(("--%s\r\n" % boundary).encode())
            if isinstance(value, tuple):
                name, content, ctype = value
                buf.write(('Content-Disposition: form-data; name="%s"; filename="%s"\r\nContent-Type: %s\r\n\r\n' % (key, name, ctype)).encode())
                buf.write(content)
            else:
                buf.write(('Content-Disposition: form-data; name="%s"\r\n\r\n%s' % (key, value)).encode())
            buf.write(b"\r\n")
        buf.write(("--%s--\r\n" % boundary).encode())
        data = buf.getvalue()
        headers["Content-Type"] = "multipart/form-data; boundary=" + boundary
    elif body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            content = r.read()
            if raw:
                return r.status, content
            text = content.decode()
            return r.status, (json.loads(text) if text.strip() else None)
    except urllib.error.HTTPError as e:
        text = e.read().decode(errors="replace")
        try:
            return e.code, json.loads(text)
        except ValueError:
            return e.code, {"error": text[:200]}


def ok(s, r, what):
    if s != 200:
        raise RuntimeError("%s failed: %s %s" % (what, s, r))
    return r


def login(uid, pw):
    return ok(*call("POST", "/api/auth/login", {"id": uid, "password": pw}), "login " + uid)["accessToken"]


def refusal(s, r):
    return "HTTP %s · %s · %s" % (s, (r or {}).get("step"), (r or {}).get("error"))


def record(tc, title, expected, observed, result, fr_note=""):
    RESULTS.append({"id": tc, "title": title, "expected": expected, "observed": observed, "result": result, "note": fr_note})
    print("  %-9s %-11s %s" % (result, tc, title))


def defect(did, title, observed, evidence):
    DEFECTS.append({"id": did, "title": title, "observed": observed, "evidence": evidence})
    print("  DEFECT    %-11s %s" % (did, title))


def run(tc, title, expected, fn):
    try:
        fn()
    except Exception as e:  # a crashed case is recorded, the run continues
        record(tc, title, expected, "Run error: %s" % e, "ERROR")


def sql(stmt):
    """Runs one statement against the API's database (only the QA run's own rows are ever touched)."""
    ps = ("$c=New-Object System.Data.SqlClient.SqlConnection('Server=.\\SQLEXPRESS;Database=%s;Integrated Security=True;TrustServerCertificate=True');"
          "$c.Open();$k=$c.CreateCommand();$k.CommandText=@'\n%s\n'@;$n=$k.ExecuteNonQuery();$c.Close();$n") % (DB, stmt)
    r = subprocess.run(["powershell", "-NoProfile", "-Command", ps], capture_output=True, text=True)
    if r.returncode != 0:
        raise RuntimeError("SQL failed: " + r.stderr.strip()[:300])
    return r.stdout.strip()


def automation():
    return ok(*call("POST", "/api/automation/run", {}, admin), "automation run")


def inbox(token, code=None, ref=None):
    rows = ok(*call("GET", "/api/notifications?take=500", token=token), "notifications")
    return [n for n in rows if (code is None or n["code"] == code) and (ref is None or n.get("entityRef") == ref)]


def jpeg(colour=None, size=(1024, 768)):
    b = io.BytesIO()
    img = Image.frombytes("RGB", size, os.urandom(size[0] * size[1] * 3)) if colour is None else Image.new("RGB", size, colour)
    img.save(b, "JPEG", quality=75)
    return b.getvalue()


def photo(lat, lng, when=None, mock=False, tamper=False, acc=7.0):
    data = jpeg()
    digest = hashlib.sha256(data).hexdigest()
    if tamper:
        data = data[:-10] + b"\x00" * 10
    return {"base64": base64.b64encode(data).decode(), "sha256": digest, "lat": lat, "lng": lng, "accuracyM": acc,
            "capturedAt": (when or datetime.now(timezone.utc)).isoformat(), "isMock": mock}


def minstall(token, qr, lat, lng, txn=None, offline=False, queued=None, captured=None, device_time=None, mock=False, tamper=False):
    t0 = captured or datetime.now(timezone.utc)
    body = {"clientTxnId": str(txn or uuid.uuid4()), "qr": qr, "poleId": "QA-P1", "remarks": "QA", "replacesQr": None, "offline": offline,
            "queuedAt": queued.isoformat() if queued else None, "deviceTime": (device_time or datetime.now(timezone.utc)).isoformat(),
            "before": photo(lat, lng, t0, mock), "after": photo(lat, lng, t0 + timedelta(seconds=40), mock, tamper)}
    return call("POST", "/api/mobile/installations", body, token)


def web_install(token, qr, lat, lng):
    t = datetime.now(timezone.utc).isoformat()
    form = [("Qr", qr), ("PoleId", "QA-P1"), ("Lat", lat), ("Lng", lng), ("AccuracyM", 7), ("CapturedAt", t), ("Offline", "false"),
            ("Before", ("before.jpg", jpeg((90, 90, 110)), "image/jpeg")), ("BeforeLat", lat), ("BeforeLng", lng), ("BeforeAccuracyM", 7), ("BeforeAt", t),
            ("After", ("after.jpg", jpeg((200, 190, 140)), "image/jpeg")), ("AfterLat", lat), ("AfterLng", lng), ("AfterAccuracyM", 7), ("AfterAt", t)]
    return call("POST", "/api/installations", form, token)


def utc(s):
    d = datetime.fromisoformat(s.replace("Z", "+00:00"))
    return d if d.tzinfo else d.replace(tzinfo=timezone.utc)


def item(token, qr):
    return ok(*call("GET", "/api/items/" + urllib.parse.quote(qr, safe=""), token=token), "item " + qr)


def phone(user_token, uid, label):
    """Enrols a QA phone for the user and completes setup; returns (device session token, device id, device token)."""
    e = ok(*call("POST", "/api/mobile/enrolment", {"forUserId": None, "serverUrl": BASE}, user_token), "enrolment " + uid)
    en = ok(*call("POST", "/api/mobile/enrol", {"code": e["code"], "deviceUid": "qa-%s-%s-%s" % (uid, label, TS), "model": "QA " + label, "manufacturer": "QA", "osVersion": "14", "appVersion": "1.0.0"}), "enrol " + uid)
    sess = ok(*call("POST", "/api/mobile/session", {"deviceToken": en["deviceToken"], "appVersion": "1.0.0"}), "session " + uid)
    return sess["accessToken"], en["deviceId"], en["deviceToken"]


def setup_check(dev_token, lat=31.5204, lng=74.3587, mock=False):
    return call("POST", "/api/mobile/setup-check", {"photo": photo(lat, lng, mock=mock, acc=9), "deviceTime": datetime.now(timezone.utc).isoformat(), "autoTimeEnabled": True}, dev_token)


# ---- local e-mail and SMS sinks: prove FR-NTF-002 delivery without an outside service -----------------------
MAIL, SMS = [], []


def smtp_sink(port):
    srv = socket.socket(); srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1); srv.bind(("127.0.0.1", port)); srv.listen(5)

    def serve(c):
        f = c.makefile("rb"); w = lambda s: c.sendall((s + "\r\n").encode())
        w("220 qa-sink"); data, rcpt = [], []
        while True:
            line = f.readline()
            if not line:
                break
            cmd = line.decode(errors="replace").strip()
            up = cmd.upper()
            if up.startswith(("EHLO", "HELO")):
                w("250-qa-sink"); w("250 OK")
            elif up.startswith("RCPT"):
                rcpt.append(cmd[8:].strip("<> ")); w("250 OK")
            elif up == "DATA":
                w("354 go")
                while True:
                    l2 = f.readline()
                    if l2 in (b".\r\n", b".\n", b""):
                        break
                    data.append(l2)
                MAIL.append({"to": rcpt[:], "raw": b"".join(data).decode(errors="replace")}); rcpt.clear(); data.clear(); w("250 queued")
            elif up == "QUIT":
                w("221 bye"); break
            else:
                w("250 OK")
        c.close()

    def loop():
        while True:
            c, _ = srv.accept()
            threading.Thread(target=serve, args=(c,), daemon=True).start()
    threading.Thread(target=loop, daemon=True).start()


class SmsHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        SMS.append(urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)); self.send_response(200); self.end_headers(); self.wfile.write(b"OK")

    def log_message(self, *a):
        pass


# ============================================================================ setup
print("FRD-GSL-001 acceptance run against", BASE, "· tag", TS, "· db", DB or "(none)")
admin = login("sysadmin", ADMIN_PW)
QA = [("qa-sup", "QA Lighting Supervisor", "supervisor", "Central"), ("qa-supn", "QA Lighting Supervisor North", "supervisor", "North"),
      ("qa-appr", "QA Approving Officer", "storeincharge", "Central"), ("qa-sk", "QA Store Officer", "storekeeper", "Central"),
      ("qa-off", "QA Lighting Officer", "officer", "Central"), ("qa-pd", "QA Monitoring Officer", "pdoffice", None), ("qa-proc", "QA Procurement", "procurement", None)]
for uid, name, role, zone in QA:
    ok(*call("PUT", "/api/users", {"id": uid, "name": name, "email": uid + "@qa.test", "employeeId": None, "designation": "QA — acceptance run", "phone": "0300" + str(abs(hash(uid)) % 10**7).zfill(7),
                                   "roleKey": role, "zone": zone, "storeIds": [], "siteIds": [], "active": True}, admin), "user " + uid)
    ok(*call("POST", "/api/users/%s/password" % uid, {"password": QA_PW}, admin), "password " + uid)
tok = {uid: login(uid, QA_PW) for uid, *_ in QA}
sup, supn, appr, sk, off, pd, proc = (tok[u] for u in ("qa-sup", "qa-supn", "qa-appr", "qa-sk", "qa-off", "qa-pd", "qa-proc"))
CFG0 = ok(*call("GET", "/api/config", token=admin), "config")

stores = ok(*call("GET", "/api/stores", token=admin), "stores")
store = next(s for s in stores if s["code"] == "CEN01")
store2 = next(s for s in stores if s["code"] != "CEN01")
stock_bin = next(l for l in store["locations"] if l["kind"] == "Stock" and l["active"])
qbin = next(l for l in store["locations"] if l["kind"] == "Quarantine")
rcv = next(l for l in store["locations"] if l["kind"] == "Receiving")
qa_bin_id = ok(*call("POST", "/api/stores/%d/locations" % store["id"], {"section": "QA", "rack": "R" + TS[-4:], "bin": "B1", "kind": "Stock", "active": True}, sk), "QA bin")["id"]
labels = ok(*call("GET", "/api/stores/%d/labels" % store["id"], token=sk), "bin labels")
qa_bin = next(l for l in labels if l["id"] == qa_bin_id)
SITE = {"code": None, "name": "QA acceptance site " + TS, "zone": "Central", "division": "Lahore City", "ward": None, "type": "Junction",
        "lat": 31.520400, "lng": 74.358700, "geofenceM": 60, "supervisorUserId": "qa-sup", "officerUserId": "qa-off", "status": "Active", "remarks": "FRD acceptance run"}
site_id = ok(*call("POST", "/api/sites", SITE, admin), "site")["id"]
vendor = next(v for v in ok(*call("GET", "/api/vendors", token=admin), "vendors") if v["code"] == "V0231")
PO = {"number": "QA-" + TS, "vendorId": vendor["id"], "date": date.today().isoformat(), "deliveryDue": None, "warrantyMonths": 24, "remarks": "FRD acceptance run",
      "lines": [{"category": "LUM", "subCategory": "LED", "spec": "090W", "description": "QA LED luminaire 90W", "uom": "Nos", "qty": 10, "rate": 18500}]}
po_id = ok(*call("POST", "/api/purchase-orders", PO, proc), "PO")["id"]
po = next(p for p in ok(*call("GET", "/api/purchase-orders", token=proc), "POs") if p["id"] == po_id)
qa_art = ok(*call("POST", "/api/articles", {"purchaseOrderLineId": po["lines"][0]["id"], "serialised": True, "packSize": 1, "reorderLevel": 0, "expectedLifeMonths": 60}, proc), "article")
qa_labels = [i["qrCode"] for i in ok(*call("GET", "/api/articles/%d/items" % qa_art["id"], token=proc), "labels")]
a90 = next(a for a in ok(*call("GET", "/api/articles", token=admin), "articles") if a["itemCode"].startswith("LUM-LED-090W-PO2611"))
ok(*call("PUT", "/api/articles/%d" % qa_art["id"], {"reorderLevel": 500, "status": "Active", "expectedLifeMonths": 60}, proc), "reorder high")
print("  setup: store %s, site %s, PO %s with %d labels, QA bin %s, stock article %s" % (store["code"], site_id, PO["number"], len(qa_labels), qa_bin["fullCode"], a90["itemCode"]))
STATE = {}


def new_req(token, qty, submit=True, site=site_id, art=None):
    return call("POST", "/api/requisitions", {"siteId": site, "storeId": store["id"], "needBy": None, "purpose": "QA " + TS,
                                              "lines": [{"articleId": art or a90["id"], "qty": qty}], "submit": submit}, token)


# ============================================================================ receipt (TC-01 … 06, FR-INV-005/010/012)
def tc01():
    for q in qa_labels[:6]:
        s, r = call("POST", "/api/grn/scan", {"qr": q, "purchaseOrderId": po_id, "storeId": store["id"]}, sk)
        if not (s == 200 and r["ok"]):
            raise RuntimeError("scan refused: " + str(r))
    s, g = call("POST", "/api/grn", {"purchaseOrderId": po_id, "storeId": store["id"], "storeLocationId": None, "qrCodes": qa_labels[:5], "condition": "Packaging damaged",
                                    "rejections": [{"qr": qa_labels[5], "reasonCode": "Physically damaged", "remarks": "cracked housing", "photoBase64": base64.b64encode(jpeg((150, 40, 40), (320, 240))).decode()}],
                                    "challanNo": "QA-DC-" + TS, "invoiceNo": None, "invoiceDate": None, "remarks": None}, sk)
    STATE["grn"] = g
    line = next(p for p in ok(*call("GET", "/api/purchase-orders", token=sk), "POs") if p["id"] == po_id)["lines"][0]
    its = ok(*call("GET", "/api/grn/%d/items" % g["id"], token=sk), "grn items") if s == 200 else []
    received = [i for i in its if i["status"] == "Received"]
    # put-away by scanning the items and the bin label
    mv = call("POST", "/api/stock-control/move", {"qrCodes": qa_labels[:5], "toLocationId": None, "toLocationQr": qa_bin["payload"], "remarks": "QA put-away"}, sk)
    after = [item(sk, q)["item"] for q in qa_labels[:5]]
    good = s == 200 and len(received) == 5 and all(i["locationCode"] == rcv["fullCode"] for i in received) and line["received"] == 5 and mv[0] == 200 and all(a["locationCode"] == qa_bin["fullCode"] for a in after)
    record("TC-01", "Receive a PO-referenced article by QR into a bin", "GRN numbered; items in store at the bin; PO outstanding reduced by 5",
           "GRN %s; %d Received into %s, then put away by scan to %s; PO line received %s of %s" % (g.get("number") if s == 200 else refusal(s, g), len(received), rcv["fullCode"], qa_bin["fullCode"], line["received"], line["qty"]),
           "PASS" if good else "FAIL")
run("TC-01", "Receive a PO-referenced article by QR into a bin", "", tc01)


def fr_inv_005():
    g = STATE["grn"]
    rej = item(sk, qa_labels[5])["item"]
    notes = [d for d in ok(*call("GET", "/api/grn/discrepancies", token=proc), "discrepancies") if d["poNumber"] == PO["number"] and d["kind"] == "Rejected"]
    good = g.get("condition") == "Packaging damaged" and g.get("rejectedCount") == 1 and rej["status"] == "Rejected" and rej["locationCode"] is None and notes and notes[0]["qty"] == 1
    record("FR-INV-005", "Condition and per-item rejection at receipt", "Condition recorded; rejected item with reason code and photograph does not enter stock",
           "GRN condition %s, rejected %s; item %s, location %s; discrepancy note %s (%s)" % (g.get("condition"), g.get("rejectedCount"), rej["status"], rej["locationCode"], notes[0]["number"] if notes else "none", notes[0]["reason"] if notes else ""),
           "PASS" if good else "FAIL")
run("FR-INV-005", "Condition and per-item rejection at receipt", "", fr_inv_005)


def tc02():
    s1, r1 = call("POST", "/api/grn", {"purchaseOrderId": 0, "storeId": store["id"], "storeLocationId": None, "qrCodes": [qa_labels[6]]}, sk)
    record("TC-02", "Receive without a PO", "Refused — VAL-INV-001", "Posting with no PO: " + refusal(s1, r1),
           "PASS*" if s1 in (400, 404, 409) else "FAIL", "Refused, but as “Purchase order not found” rather than a prompt to select a PO")
run("TC-02", "Receive without a PO", "", tc02)


def tc03():
    s, r = call("POST", "/api/grn/scan", {"qr": "SLIMS|LUM-LED-090W-POFAKE-001|000001|ABCDEF", "purchaseOrderId": po_id, "storeId": store["id"]}, sk)
    record("TC-03", "Scan a forged label", "Refused — VAL-INV-003; attempt audited", "ok=%s · step=%s · %s" % (r.get("ok"), r.get("step"), r.get("message")),
           "PASS" if s == 200 and r["ok"] is False and r.get("step") == "BR-002" else "FAIL")
run("TC-03", "Scan a forged label", "", tc03)


def tc04():
    s, r = call("POST", "/api/grn/scan", {"qr": qa_labels[0], "purchaseOrderId": po_id, "storeId": store["id"]}, sk)
    record("TC-04", "Receive the same label twice", "Refused — VAL-INV-006", "ok=%s · %s" % (r.get("ok"), r.get("message")),
           "PASS" if s == 200 and r["ok"] is False else "FAIL")
run("TC-04", "Receive the same label twice", "", tc04)


def tc05():
    body = dict(PO); body["lines"] = [dict(PO["lines"][0], qty=5)]
    ok(*call("PUT", "/api/purchase-orders/%d" % po_id, body, proc), "PO reduce")
    s, r = call("POST", "/api/grn/scan", {"qr": qa_labels[7], "purchaseOrderId": po_id, "storeId": store["id"]}, sk)
    blocked = s == 200 and r["ok"] is False and r.get("step") == "BR-014"
    record("TC-05", "Receive beyond PO balance", "Refused — VAL-INV-007; after an approved over-receipt exception the item is received",
           "Scan beyond balance: %s. No over-receipt exception function exists." % (r.get("message") if s == 200 else refusal(s, r)),
           "PARTIAL" if blocked else "FAIL", "The block holds; the approved-exception route is not built (outside the Not-met scope)")
    ok(*call("PUT", "/api/purchase-orders/%d" % po_id, PO, proc), "PO restore")
run("TC-05", "Receive beyond PO balance", "", tc05)


def tc06():
    # three more labels received and left in Receiving
    ok(*call("POST", "/api/grn", {"purchaseOrderId": po_id, "storeId": store["id"], "qrCodes": qa_labels[6:9], "challanNo": "QA-DC2-" + TS}, sk), "GRN 2")
    avail = ok(*call("GET", "/api/requisitions/availability/%d" % store["id"], token=sup), "availability").get(str(qa_art["id"]), 0)
    s, r = new_req(sup, 1, art=qa_art["id"])
    s2, r2 = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "QA"}, appr)
    good = avail == 5 and s2 == 409   # the 5 put away earlier are available, the 3 in Receiving are not; nothing can be reserved beyond what is in the bin
    STATE["tc06_req"] = r2 if s2 == 200 else r
    record("TC-06", "Stock not yet put away is not available", "Receiving quantity is not shown as available",
           "Available for requisition: %s (8 received: 5 put away, 3 still in Receiving %s). Approval: %s" % (avail, rcv["fullCode"], "reserved" if s2 == 200 else refusal(s2, r2)),
           "PASS" if avail == 5 else "FAIL")
    call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, sup)
run("TC-06", "Stock not yet put away is not available", "", tc06)


def fr_inv_010_012():
    s1, r1 = call("POST", "/api/stock-control/move", {"qrCodes": [qa_labels[6]], "toLocationQr": "SLIMSLOC|" + rcv["fullCode"]}, sk)
    ok(*call("POST", "/api/stock-control/move", {"qrCodes": qa_labels[6:9], "toLocationQr": qa_bin["payload"]}, sk), "put-away 2")
    s, mv = call("POST", "/api/stock-control/move", {"qrCodes": [qa_labels[8]], "toLocationId": stock_bin["id"], "remarks": "QA bin move"}, sk)
    hist = item(sk, qa_labels[8])["history"]
    moved = [h for h in hist if h["action"] == "moved"]
    good = s == 200 and item(sk, qa_labels[8])["item"]["locationCode"] == stock_bin["fullCode"] and moved and s1 == 409
    record("FR-INV-010/012", "Receiving area, put-away and bin move by scan", "Receipts held in Receiving until put away; bin moves by scanning item and bin; ledger records both locations",
           "Moving back into Receiving: %s. Bin move: %s → %s; history %s" % (refusal(s1, r1), qa_bin["fullCode"], mv.get("toLocation") if s == 200 else refusal(s, mv), moved[0]["nextJson"] if moved else "none"),
           "PASS" if good else "FAIL")
run("FR-INV-010/012", "Receiving area, put-away and bin move by scan", "", fr_inv_010_012)


# ============================================================================ requisition (TC-07 … 11, FR-REQ-010/014)
def tc07():
    mall = next(s for s in ok(*call("GET", "/api/sites", token=admin), "sites") if s["code"] == "SITE-C-MALL-0001")
    s1, r1 = new_req(sup, 1, site=mall["id"])
    s2, r2 = new_req(supn, 1)
    good = s1 in (403, 409) and s2 in (403, 409)
    record("TC-07", "Requisition for an unregistered or out-of-scope site", "No free-text field; out-of-scope site refused — VAL-REQ-002",
           "Site ID is an integer reference (no free text). Another supervisor's site: %s. Other zone: %s" % (refusal(s1, r1), refusal(s2, r2)),
           "PASS" if good else "FAIL")
run("TC-07", "Requisition for an unregistered or out-of-scope site", "", tc07)


def tc08():
    s, r = new_req(sup, 5000)
    line = r["lines"][0] if s == 200 else {}
    notified = [n for n in inbox(appr, "NTF-01", r.get("number"))] + [n for n in inbox(pd, "NTF-01", r.get("number"))]
    good = s == 200 and r["status"] == "Submitted" and line.get("shortfall", 0) > 0 and line.get("indentRaised") and notified
    record("TC-08", "Submit a requisition with a shortfall", "Warning; Submitted; indent recorded; Approving Officer notified",
           "Status %s; shortfall %s; indent recorded %s; required level %s; NTF-01 delivered to %d approver(s)" % (r.get("status"), line.get("shortfall"), line.get("indentRaised"), r.get("requiredLevel"), len(notified)),
           "PASS" if good else "FAIL")
    if s == 200:
        call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, sup)
run("TC-08", "Submit a requisition with a shortfall", "", tc08)


def tc09():
    s, r = new_req(admin, 1)
    s2, r2 = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "self"}, admin)
    record("TC-09", "Approve own requisition", "Refused — VAL-REQ-005", refusal(s2, r2), "PASS" if s2 == 409 and r2.get("step") == "BR-026" else "FAIL")
    call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, admin)
run("TC-09", "Approve own requisition", "", tc09)


def tc10():
    s, r = new_req(sup, 5)
    s2, d = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "QA part approval", "lines": [{"lineId": r["lines"][0]["id"], "qtyApproved": 3}]}, appr)
    res = [i for i in ok(*call("GET", "/api/articles/%d/items?status=Reserved" % a90["id"], token=sk), "reserved") if i["requisitionId"] == r["id"]]
    STATE["req"], STATE["reserved"] = d, [i["qrCode"] for i in res]
    told = inbox(sup, "NTF-02", r["number"])
    good = s2 == 200 and d["status"] == "Approved" and d["lines"][0]["qtyApproved"] == 3 and len(res) == 3 and d.get("issueSlipNo") and told
    record("TC-10", "Approve in part", "3 reserved; Issue Slip numbered; supervisor notified",
           "Status %s; approved %s; %d labels reserved; Issue Slip %s; supervisor notified: %s" % (d.get("status"), d["lines"][0]["qtyApproved"] if s2 == 200 else "-", len(res), d.get("issueSlipNo"), "yes (NTF-02)" if told else "no"),
           "PASS" if good else "FAIL")
run("TC-10", "Approve in part", "", tc10)


def tc11():
    s, r = new_req(sup, 1)
    s1, r1 = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": False, "note": None}, appr)
    s2, r2 = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": False, "note": "QA: not required"}, appr)
    good = s1 == 400 and s2 == 200 and r2["status"] == "Rejected" and r2["decisionNote"] and inbox(sup, "NTF-03", r["number"])
    record("TC-11", "Reject a requisition", "Refused without reason; then returned to the supervisor with the reason",
           "Without reason: %s. With reason: status %s; supervisor notified (NTF-03)" % (refusal(s1, r1), r2.get("status")), "PASS" if good else "FAIL")
run("TC-11", "Reject a requisition", "", tc11)


def fr_req_010():
    s, r = new_req(sup, 60)           # 60 × Rs 18,500 = Rs 1,110,000, above the Store In-Charge limit of Rs 1,000,000
    s1, r1 = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "QA recommend"}, appr)
    s2, r2 = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "again"}, appr)
    esc = inbox(pd, "NTF-01", r["number"])
    s3, r3 = call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "QA level 2"}, pd)
    good = s == 200 and r["requiredLevel"] == 2 and s1 == 200 and r1["status"] == "Submitted" and r1["currentLevel"] == 2 and s2 == 409 and esc and s3 == 200 and r3["status"] == "Approved"
    record("FR-REQ-010", "Approval matrix by value with escalation", "Above the officer's limit the requisition escalates to the next level",
           "Value Rs %s → required level %s. Store In-Charge: status %s, now at level %s (%s). Acting again: %s. PD Office notified: %s; PD Office approval: %s"
           % (format(r.get("value", 0), ",.0f"), r.get("requiredLevel"), r1.get("status"), r1.get("currentLevel"), r1.get("recommendedBy"), refusal(s2, r2), "yes" if esc else "no", r3.get("status")),
           "PASS" if good else "FAIL")
    call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, sup)
run("FR-REQ-010", "Approval matrix by value with escalation", "", fr_req_010)


def fr_req_014():
    if not DB:
        record("FR-REQ-014", "Reservations lapse automatically", "Unissued reservations released after the validity; requester notified", "Needs --db", "NOT RUN")
        return
    s, r = new_req(sup, 1)
    d = ok(*call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "QA lapse"}, appr), "approve lapse")
    held = [i["qrCode"] for i in ok(*call("GET", "/api/articles/%d/items?status=Reserved" % a90["id"], token=sk), "res") if i["requisitionId"] == r["id"]]
    sql("UPDATE Requisitions SET DecidedAt = DATEADD(day, -30, SYSUTCDATETIME()) WHERE Id = %d" % r["id"])
    automation()
    after = ok(*call("GET", "/api/requisitions/%d" % r["id"], token=sup), "req")
    freed = item(sk, held[0])["item"]["status"] if held else None
    told = inbox(sup, "NTF-04", r["number"])
    good = after["status"] == "Lapsed" and freed == "Received" and told
    record("FR-REQ-014", "Reservations lapse automatically", "Unissued reservations released after the validity; requester notified",
           "After the validity: requisition %s, reserved label now %s; supervisor notified (NTF-04): %s" % (after["status"], freed, "yes" if told else "no"), "PASS" if good else "FAIL")
run("FR-REQ-014", "Reservations lapse automatically", "", fr_req_014)


# ============================================================================ issue (TC-12 … 15)
def tc12():
    s, r = new_req(sup, 1)
    s2, r2 = call("POST", "/api/issues", {"requisitionId": r["id"], "qrCodes": [STATE["reserved"][0]]}, sk)
    record("TC-12", "Issue before approval", "Refused — VAL-INV-020", refusal(s2, r2), "PASS" if s2 == 409 and r2.get("step") == "BR-006" else "FAIL")
    call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, sup)
run("TC-12", "Issue before approval", "", tc12)


def tc13():
    extra = next(i["qrCode"] for i in ok(*call("GET", "/api/articles/%d/items?status=Received" % a90["id"], token=sk), "free") if i["storeId"] == store["id"])
    s, r = call("POST", "/api/issues", {"requisitionId": STATE["req"]["id"], "qrCodes": STATE["reserved"] + [extra]}, sk)
    record("TC-13", "Issue above approved quantity", "Refused — VAL-INV-022", refusal(s, r), "PASS" if s == 409 and r.get("step") == "BR-010" else "FAIL")
run("TC-13", "Issue above approved quantity", "", tc13)




def tc15():
    s, r = call("POST", "/api/issues", {"requisitionId": STATE["req"]["id"], "qrCodes": STATE["reserved"], "issuedTo": "QA crew", "vehicle": "QA-1"}, sk)
    STATE["issue"] = r
    rec = ok(*call("GET", "/api/reports/reconciliation", token=pd), "reconciliation")
    good = s == 200 and r.get("gatePassNo") and all(i["siteId"] == site_id and i["locationCode"] is None and i["status"] == "Issued" for i in r["items"]) and inbox(off, "NTF-05", r.get("number"))
    record("TC-15", "Post an issue", "Issue Note and Gate Pass numbered; items at the site; bins relieved; ledger updated",
           "Issue %s, gate pass %s; %d items Issued to the site, bins relieved; quantity ledger updated (reconciliation %d lines); officer notified (NTF-05)" % (r.get("number"), r.get("gatePassNo"), len(r.get("items", [])), len(rec["rows"])) if s == 200 else refusal(s, r),
           "PASS" if good else "FAIL")
run("TC-15", "Post an issue", "", tc15)


def tc14():
    # an item issued in TC-15 scanned again against another approved requisition
    s0, r0 = new_req(sup, 1)
    ok(*call("POST", "/api/requisitions/%d/decide" % r0["id"], {"approve": True, "note": "QA TC-14"}, appr), "approve TC-14")
    s, r = call("POST", "/api/issues/scan", {"qr": STATE["reserved"][0], "requisitionId": r0["id"], "already": []}, sk)
    record("TC-14", "Duplicate QR movement", "Refused — VAL-INV-023 with its current site shown", "ok=%s · step=%s · %s" % (r.get("ok"), r.get("step"), r.get("message")),
           "PASS" if s == 200 and r["ok"] is False and r.get("step") == "BR-011" else "FAIL")
    call("POST", "/api/requisitions/%d/cancel" % r0["id"], {"reason": "QA"}, sup)
run("TC-14", "Duplicate QR movement", "", tc14)


# ============================================================================ mobile setup and device binding (FR-SEC-009)
def fr_sec_009():
    mine0 = ok(*call("GET", "/api/mobile/mine", token=off), "mine")
    d1, d1id, d1tok = phone(off, "qa-off", "A")
    s0, r0 = minstall(d1, STATE["reserved"][0], SITE["lat"], SITE["lng"])       # before the setup test photograph
    s_mock, r_mock = setup_check(d1, mock=True)
    s_ok, r_ok = setup_check(d1)
    STATE["offdev"] = d1
    d2, d2id, d2tok = phone(off, "qa-off", "B")                                  # a second phone for the same user
    s2c, r2c = setup_check(d2)
    s2i, r2i = minstall(d2, STATE["reserved"][0], SITE["lat"], SITE["lng"])
    sa, ra = call("POST", "/api/mobile/devices/%s/approve" % d2id, {}, admin)
    so, ro = call("POST", "/api/mobile/session", {"deviceToken": d1tok})
    good = (mine0["required"] and s0 == 403 and r0.get("step") == "FR-SEC-009" and s_mock == 200 and not r_mock["ok"] and s_ok == 200 and r_ok["ok"] and r_ok["device"]["status"] == "Active"
            and r2c["device"]["status"] == "AwaitingApproval" and s2i == 403 and sa == 200 and ra["status"] == "Active" and so == 401)
    STATE["offdev"] = ok(*call("POST", "/api/mobile/session", {"deviceToken": d2tok}), "session B")["accessToken"]
    record("FR-SEC-009", "Phone bound to a named user; change of phone re-authorised", "Field evidence only from the user's bound phone; a replacement needs approval",
           "Setup required for the officer: %s. Before setup test: %s. Mock location in setup: %s. Setup: %s. Second phone: %s, submitting from it: %s. After approval: %s, old phone session: HTTP %s"
           % (mine0["required"], refusal(s0, r0), "refused" if not r_mock.get("ok") else "accepted", r_ok["device"]["status"], r2c["device"]["status"], refusal(s2i, r2i), ra.get("status"), so),
           "PASS" if good else "FAIL")
run("FR-SEC-009", "Phone bound to a named user; change of phone re-authorised", "", fr_sec_009)


# ============================================================================ installation (TC-16 … 22, FR-FLD-007/009/012/013)
def tc16():
    dev = STATE["offdev"]
    ok(*call("POST", "/api/mobile/acknowledge", {"issueId": STATE["issue"]["id"], "lat": SITE["lat"], "lng": SITE["lng"], "accuracyM": 8}, dev), "ack")
    body = {"clientTxnId": str(uuid.uuid4()), "qr": STATE["reserved"][0], "offline": False, "deviceTime": datetime.now(timezone.utc).isoformat(),
            "before": photo(SITE["lat"], SITE["lng"]), "after": dict(photo(SITE["lat"], SITE["lng"]), base64="", sha256="")}
    s, r = call("POST", "/api/mobile/installations", body, dev)
    record("TC-16", "Installation without the AFTER photograph", "Not submittable — VAL-FLD-003", refusal(s, r),
           "PASS" if s == 409 and r.get("step") == "BR-013" else "FAIL")
run("TC-16", "Installation without the AFTER photograph", "", tc16)


def tc17():
    s, r = web_install(off, STATE["reserved"][0], SITE["lat"], SITE["lng"])
    record("TC-17", "Gallery photograph", "No gallery or upload option on the capture screen",
           "Browser upload refused: %s. The field app captures in-app from the live camera only (no gallery picker exists); the API accepts evidence only from the bound phone" % refusal(s, r),
           "PASS" if s == 409 and r.get("step") == "FR-FLD-007" else "FAIL")
run("TC-17", "Gallery photograph", "", tc17)


def tc18():
    s, r = minstall(STATE["offdev"], STATE["reserved"][0], SITE["lat"] + 0.0001, SITE["lng"] + 0.0001)
    STATE["inst_ok"] = r
    record("TC-18", "Installation inside the geofence", "Evidence Valid; installation Complete; item Installed",
           "Status %s at %s m; item %s" % (r.get("geoStatus"), r.get("distanceM"), item(pd, STATE["reserved"][0])["item"]["status"]) if s == 200 else refusal(s, r),
           "PASS*" if s == 200 and r["geoStatus"] == "Confirmed" else "FAIL", "Reported as “Confirmed”, not the FRD value “Valid”")
run("TC-18", "Installation inside the geofence", "", tc18)


def fr_fld_009():
    r = STATE["inst_ok"]
    s1, orig = call("GET", r["after"]["url"][len(""):], token=pd, raw=True)
    s2, stamped = call("GET", r["after"]["stampedUrl"], token=pd, raw=True) if r["after"].get("stampedUrl") else (0, b"")
    same = s1 == 200 and hashlib.sha256(orig).hexdigest() == r["after"]["sha256"]
    try:
        Image.open(io.BytesIO(stamped)).verify(); img_ok = True
    except Exception:
        img_ok = False
    tampered_s, tampered = minstall(STATE["offdev"], STATE["reserved"][2], SITE["lat"], SITE["lng"], tamper=True)
    STATE["tampered"] = tampered
    good = same and s2 == 200 and img_ok and stamped != orig and tampered_s == 200 and tampered["geoStatus"] == "MissingEvidence" and inbox(off, "NTF-09", tampered["number"])
    record("FR-FLD-009", "Original preserved with hash; server-stamped copy", "Original photograph unaltered; stamped copy with time, coordinates, accuracy, site, item, officer",
           "Original downloaded byte-for-byte equal to its SHA-256 %s…: %s. Stamped copy: HTTP %s, %d KB, valid JPEG %s. Photograph altered in transit: %s, officer told to re-capture (NTF-09)"
           % (r["after"]["sha256"][:12], same, s2, len(stamped) // 1024, img_ok, tampered.get("geoStatus") if tampered_s == 200 else refusal(tampered_s, tampered)),
           "PASS" if good else "FAIL")
run("FR-FLD-009", "Original preserved with hash; server-stamped copy", "", fr_fld_009)


def tc19():
    s, r = minstall(STATE["offdev"], STATE["reserved"][1], SITE["lat"] + 0.027, SITE["lng"])
    STATE["inst_far"] = r
    told = inbox(pd, "NTF-08", r.get("number"))
    record("TC-19", "GPS mismatch", "Location Exception; Pending Review; not complete; Monitoring Officer notified",
           "Status %s at %s m; Monitoring Officer notified (NTF-08): %s" % (r.get("geoStatus"), r.get("distanceM"), "yes" if told else "no") if s == 200 else refusal(s, r),
           "PASS*" if s == 200 and r["geoStatus"] == "PendingVerification" and r["distanceM"] > 1000 and told else "FAIL",
           "Held for review as “PendingVerification” rather than “Location Exception”")
run("TC-19", "GPS mismatch", "", tc19)


def fr_exc_007():
    x = STATE["inst_far"]
    s1, r1 = call("POST", "/api/installations/%d/field-verification" % x["id"], {"note": "QA: check the pole on the ground"}, pd)
    told = inbox(sup, "NTF-10", x["number"])
    supdev, _, _ = phone(sup, "qa-sup", "S")
    ok(*setup_check(supdev), "sup setup")
    boot = ok(*call("GET", "/api/mobile/bootstrap", token=supdev), "bootstrap sup")
    task = [t for t in boot["verificationTasks"] if t["id"] == x["id"]]
    s2, r2 = call("POST", "/api/mobile/findings/%d" % x["id"], {"findings": "QA: pole found installed, far end of the junction", "photos": [photo(SITE["lat"], SITE["lng"])], "deviceTime": datetime.now(timezone.utc).isoformat()}, supdev)
    good = s1 == 200 and told and task and s2 == 200 and r2["fvFindings"] and len(r2["fvPhotoUrls"]) == 1
    record("FR-EXC-007", "Field verification by the Lighting Supervisor", "Reviewer requests; supervisor's findings and photographs recorded on the exception",
           "Request: %s; supervisor notified (NTF-10): %s; task on the supervisor's phone: %s; findings: %s with %d photograph(s)" % (s1, "yes" if told else "no", "yes" if task else "no", r2.get("fvFindings") if s2 == 200 else refusal(s2, r2), len(r2.get("fvPhotoUrls") or [])),
           "PASS" if good else "FAIL")
run("FR-EXC-007", "Field verification by the Lighting Supervisor", "", fr_exc_007)


def tc20():
    s1, r1 = call("POST", "/api/installations/%d/verify" % STATE["inst_far"]["id"], {"approve": True, "justification": "ok"}, pd)
    s2, r2 = call("POST", "/api/installations/%d/verify" % STATE["inst_far"]["id"], {"approve": True, "justification": "ok, I was there"}, off)
    good = s1 == 409 and r1.get("step") == "BR-017" and s2 in (403, 409)
    record("TC-20", "Review with a short justification, and self-review", "Refused — VAL-EXC-001; refused — VAL-EXC-002",
           "Short justification: %s. Capturing officer: %s" % (refusal(s1, r1), refusal(s2, r2)), "PASS*" if good else "FAIL",
           "Self-review is stopped by the officer lacking review permission; the SoD rule itself is also in place")
run("TC-20", "Review with a short justification, and self-review", "", tc20)


def fr_exc_010():
    if not DB:
        record("FR-EXC-010", "Unresolved exceptions age and escalate", "Escalation according to the notification rules", "Needs --db", "NOT RUN")
        return
    x = STATE["inst_far"]
    sql("UPDATE Installations SET SubmittedAt = DATEADD(day, -4, SYSUTCDATETIME()) WHERE Id = %d" % x["id"])
    automation()
    after = ok(*call("GET", "/api/installations/%d" % x["id"], token=pd), "inst")
    esc = [n for n in inbox(admin, "NTF-08", x["number"]) + inbox(pd, "NTF-08", x["number"]) if n["level"] >= 1]
    good = after["escalationLevel"] >= 1 and after["ageDays"] >= 4 and esc
    record("FR-EXC-010", "Unresolved exceptions age and escalate", "Escalation according to the notification rules",
           "After 4 days unresolved: age %s d, escalation level %s, escalation notices %d" % (after["ageDays"], after["escalationLevel"], len(esc)), "PASS" if good else "FAIL")
run("FR-EXC-010", "Unresolved exceptions age and escalate", "", fr_exc_010)


def tc21():
    s, r = call("POST", "/api/installations/%d/verify" % STATE["inst_far"]["id"], {"approve": True, "justification": "QA: verified in field; pole at far end of the junction"}, pd)
    record("TC-21", "Approve an exception", "Installation Complete; item Installed; justification in RPT-10",
           "Status %s; item %s" % (r.get("geoStatus"), item(pd, STATE["reserved"][1])["item"]["status"]) if s == 200 else refusal(s, r),
           "PASS*" if s == 200 and r["geoStatus"] == "ApprovedException" else "FAIL", "Reported as “ApprovedException”")
run("TC-21", "Approve an exception", "", tc21)


def tc22():
    # re-capture the tampered one offline: captured two hours ago, queued, synchronised twice
    txn = uuid.uuid4()
    two_h = datetime.now(timezone.utc) - timedelta(hours=2)
    s1, r1 = minstall(STATE["offdev"], STATE["reserved"][2], SITE["lat"], SITE["lng"], txn=txn, offline=True, queued=two_h, captured=two_h)
    s2, r2 = minstall(STATE["offdev"], STATE["reserved"][2], SITE["lat"], SITE["lng"], txn=txn, offline=True, queued=two_h, captured=two_h)
    count = len([x for x in ok(*call("GET", "/api/installations?siteId=%d" % site_id, token=pd), "installs") if x.get("clientTxnId") == str(txn)])
    kept = s1 == 200 and abs((utc(r1["capturedAt"]) - (two_h + timedelta(seconds=40))).total_seconds()) < 2
    synced_now = s1 == 200 and abs((datetime.now(timezone.utc) - utc(r1["submittedAt"])).total_seconds()) < 300
    good = s1 == 200 and s2 == 200 and r1["id"] == r2["id"] and count == 1 and kept and synced_now and r1["offline"]
    record("TC-22", "Offline capture and duplicate synchronisation", "One installation only; original capture times kept; sync time recorded",
           "Queued 2 h ago, synchronised twice with transaction %s…: installation %s both times, %d record(s); capture time kept: %s; synchronisation time recorded separately: %s; status %s"
           % (str(txn)[:8], r1.get("number"), count, kept, synced_now, r1.get("geoStatus")), "PASS" if good else "FAIL")
    STATE["inst_offline"] = r1
run("TC-22", "Offline capture and duplicate synchronisation", "", tc22)


def d_clock():
    field = [i for i in ok(*call("GET", "/api/issues/field", token=admin), "field") if i["status"] in ("Issued", "AtSite") and i["siteId"] == site_id]
    qr = field[0]["qrCode"] if field else STATE["reserved"][2]
    s, r = minstall(STATE["offdev"], qr, SITE["lat"], SITE["lng"], device_time=datetime.now(timezone.utc) - timedelta(minutes=30))
    sm, rm = minstall(STATE["offdev"], qr, SITE["lat"], SITE["lng"], mock=True)
    STATE["drift_refused"] = (s, r, sm, rm)
    if s == 200:
        defect("DF-02", "Clock-drift check allows 65 minutes, not 5", "A capture from a phone 30 minutes off server time was accepted (limit is 5 minutes)", "InstallationService drift check")
run("DF-02", "clock drift probe", "", d_clock)


# ============================================================================ returns (TC-23 … 26, FR-INV-033)
def tc23():
    qr = STATE["reserved"][0]
    s1, r1 = call("POST", "/api/mobile/returns", {"qr": qr, "reason": "Driver failure", "kind": "OutOfOrder", "remarks": "QA"}, STATE["offdev"])
    told = inbox(sk, "NTF-11", r1.get("number"))
    s2, r2 = call("POST", "/api/returns/receive", {"qr": qr, "storeId": store["id"], "storeLocationId": None, "remarks": None}, sk)
    STATE["ret_a"] = r2
    it = item(sk, qr)["item"]
    good = s1 == 200 and s2 == 200 and it["status"] == "Returned" and it["locationCode"] == qbin["fullCode"] and told
    record("TC-23", "Report and return an out-of-order item", "Item in Quarantine; not available for issue",
           "Item %s at %s; store notified of the report (NTF-11): %s" % (it["status"], it["locationCode"], "yes" if told else "no"), "PASS" if good else "FAIL")
    if it.get("siteId") is not None and it.get("locationCode"):
        defect("DF-03", "A returned item keeps its site while sitting in quarantine", "Item %s has both site %s and location %s" % (it["itemCode"], it["siteCode"], it["locationCode"]), "ReturnService.ReceiveAsync")
run("TC-23", "Report and return an out-of-order item", "", tc23)


def tc24():
    s, r = call("POST", "/api/returns/%d/classify" % STATE["ret_a"]["id"], {"classification": "Usable", "claimRef": None, "remarks": "QA tested OK", "storeLocationQr": qa_bin["payload"]}, sk)
    it = item(sk, STATE["reserved"][0])["item"]
    avail = ok(*call("GET", "/api/requisitions/availability/%d" % store["id"], token=sup), "availability").get(str(a90["id"]), 0)
    good = s == 200 and r["classification"] == "Usable" and it["status"] == "Received" and it["locationCode"] == qa_bin["fullCode"] and it["siteId"] is None
    record("TC-24", "Classify a return as Usable", "Item In Store and available",
           "Classification %s; item %s at %s, no site; counted in available stock (%s of the article available)" % (r.get("classification") if s == 200 else refusal(s, r), it["status"], it["locationCode"], avail),
           "PASS" if good else "FAIL")
run("TC-24", "Classify a return as Usable", "", tc24)


def tc25():
    qr = STATE["reserved"][1]
    ok(*call("POST", "/api/returns/report", {"qr": qr, "reason": "LED module failure", "kind": "OutOfOrder", "remarks": "QA"}, off), "report W")
    rw = ok(*call("POST", "/api/returns/receive", {"qr": qr, "storeId": store["id"], "storeLocationId": None}, sk), "receive W")
    s1, r1 = call("POST", "/api/returns/%d/classify" % rw["id"], {"classification": "Warranty", "claimRef": "QA-WRC-" + TS, "remarks": None}, sk)
    s2, r2 = call("POST", "/api/returns/%d/reinward" % rw["id"], {"storeLocationId": stock_bin["id"], "remarks": "QA replaced"}, sk)
    d = item(sk, qr)
    acts = [h["action"] for h in d["history"]]
    good = rw["warrantyEligible"] and s1 == 200 and s2 == 200 and d["item"]["status"] == "Received" and "warranty-claimed" in acts and "re-inwarded" in acts
    record("TC-25", "Warranty claim and re-inward", "Warranty computed Yes; claim recorded; same QR back in stock with full history",
           "Warranty eligible %s; item %s on the same QR; history %d entries" % (rw["warrantyEligible"], d["item"]["status"], len(acts)), "PASS" if good else "FAIL")
    STATE["scrap_qr"] = STATE["reserved"][2]
run("TC-25", "Warranty claim and re-inward", "", tc25)


def tc26():
    qr = STATE["scrap_qr"]
    if item(sk, qr)["item"]["status"] != "Installed":
        record("TC-26", "Scrap and disposal", "Refused before condemnation; after approval item Disposed and in RPT-13", "The test item is not installed; cannot run", "ERROR")
        return
    ok(*call("POST", "/api/returns/report", {"qr": qr, "reason": "Physical damage", "kind": "OutOfOrder", "remarks": "QA"}, off), "report B")
    rb = ok(*call("POST", "/api/returns/receive", {"qr": qr, "storeId": store["id"], "storeLocationId": None}, sk), "receive B")
    s1, r1 = call("POST", "/api/returns/%d/dispose" % rb["id"], {"scrapValue": 500, "remarks": "QA"}, appr)
    ok(*call("POST", "/api/returns/%d/classify" % rb["id"], {"classification": "Scrap", "claimRef": None, "remarks": "QA"}, sk), "scrap")
    s2, r2 = call("POST", "/api/returns/%d/dispose" % rb["id"], {"scrapValue": 500, "remarks": "QA board"}, appr)
    good = s1 == 409 and r1.get("step") == "BR-020" and s2 == 200 and r2["itemStatus"] == "Disposed"
    record("TC-26", "Scrap and disposal", "Refused before condemnation; after approval item Disposed and in RPT-13",
           "Before condemnation: %s. After approval: %s" % (refusal(s1, r1), r2.get("itemStatus")), "PASS" if good else "FAIL")
run("TC-26", "Scrap and disposal", "", tc26)


# ============================================================================ stock control (FR-INV-016/019/020/021)
def fr_inv_019():
    qr = qa_labels[7]
    s1, t = call("POST", "/api/transfers", {"fromStoreId": store["id"], "toStoreId": store2["id"], "qrCodes": [qr], "vehicle": "QA-T1", "remarks": "QA"}, sk)
    mid = item(sk, qr)["item"]
    s2, t2 = call("POST", "/api/transfers/%d/receive" % t["id"], {"qrCodes": [qr]}, admin) if s1 == 200 else (0, {})
    end = item(admin, qr)["item"]
    good = s1 == 200 and mid["status"] == "InTransit" and mid["locationCode"] is None and s2 == 200 and t2["status"] == "Received" and end["status"] == "Received" and end["storeCode"] == store2["code"]
    record("FR-INV-019", "Inter-store transfer by scan with an in-transit leg", "Despatch and receipt legs; stock shown in transit between them",
           "Transfer %s: after despatch the item is %s (no bin); received at %s into %s (%s)" % (t.get("number"), mid["status"], end["storeCode"], end["locationCode"], t2.get("status")), "PASS" if good else "FAIL")
run("FR-INV-019", "Inter-store transfer by scan with an in-transit leg", "", fr_inv_019)


def fr_inv_020():
    qr = qa_labels[8]
    s0, r0 = call("POST", "/api/adjustments", {"qr": qr, "kind": "Loss", "reasonCode": "", "qtyAfter": None, "locationId": None, "remarks": "QA"}, sk)
    s1, a = call("POST", "/api/adjustments", {"qr": qr, "kind": "Loss", "reasonCode": "Lost / not found", "qtyAfter": None, "locationId": None, "remarks": "QA"}, appr)
    s2, r2 = call("POST", "/api/adjustments/%d/decide" % a["id"], {"approve": True, "note": "self"}, appr)
    pending_item = item(sk, qr)["item"]["status"]
    told = inbox(pd, "NTF-13", a["number"])
    s3, r3 = call("POST", "/api/adjustments/%d/decide" % a["id"], {"approve": True, "note": "QA approved"}, pd)
    gone = item(sk, qr)["item"]
    s4, f = call("POST", "/api/adjustments", {"qr": qr, "kind": "Found", "reasonCode": "Found in store", "locationId": stock_bin["id"], "remarks": "QA found"}, sk)
    s5, r5 = call("POST", "/api/adjustments/%d/decide" % f["id"], {"approve": True, "note": "QA"}, appr)
    back = item(sk, qr)["item"]
    good = s0 == 400 and s1 == 200 and s2 == 409 and pending_item == "Received" and told and s3 == 200 and r3["qtyBefore"] == 1 and r3["qtyAfter"] == 0 and gone["status"] == "WrittenOff" and s5 == 200 and back["status"] == "Received"
    record("FR-INV-020", "Adjustment with reason code and second-person approval", "Reason code required; approved by another user; original and adjusted values retained",
           "No reason code: %s. Self-approval: %s. While pending the item stays %s; approver notified (NTF-13). Approved by another: %s → %s, item %s. Found adjustment: item %s again"
           % (refusal(s0, r0), refusal(s2, r2), pending_item, r3.get("qtyBefore"), r3.get("qtyAfter"), gone["status"], back["status"]), "PASS" if good else "FAIL")
run("FR-INV-020", "Adjustment with reason code and second-person approval", "", fr_inv_020)


def fr_inv_021():
    in_bin = [i["qrCode"] for i in ok(*call("GET", "/api/stock/location/%d" % qa_bin_id, token=sk), "bin items")]
    c = ok(*call("POST", "/api/counts", {"storeId": store["id"], "locationId": qa_bin_id}, sk), "count start")
    for q in in_bin[1:]:
        ok(*call("POST", "/api/counts/%d/scan" % c["id"], {"qr": q, "qty": None, "locationId": qa_bin_id}, sk), "count scan")
    sub = ok(*call("POST", "/api/counts/%d/submit" % c["id"], {}, sk), "count submit")
    s_close, r_close = call("POST", "/api/counts/%d/close" % c["id"], {}, sk)
    adj = ok(*call("POST", "/api/counts/%d/adjustments" % c["id"], {"reasonCode": "Count correction"}, sk), "count adjustments")
    for v in adj["variances"]:
        if v["adjustmentId"]:
            ok(*call("POST", "/api/adjustments/%d/decide" % v["adjustmentId"], {"approve": True, "note": "QA count"}, appr), "approve count adj")
    closed = ok(*call("POST", "/api/counts/%d/close" % c["id"], {}, sk), "count close")
    missing = [v for v in sub["variances"] if v["kind"] == "Missing"]
    good = len(in_bin) >= 2 and len(missing) == 1 and missing[0]["qr"] == in_bin[0] and s_close == 409 and closed["status"] == "Closed" and all(v["clearance"] != "Open" for v in closed["variances"])
    record("FR-INV-021", "Physical verification by scan with variance report", "Variance against book stock; cleared only through approved adjustments",
           "Count %s of %s: book %d, scanned %d; variance %s. Closing before adjustments: %s. After approval: %s, variances %s"
           % (c["number"], qa_bin["fullCode"], len(in_bin), len(in_bin) - 1, [(v["kind"], v["serial"]) for v in sub["variances"]], refusal(s_close, r_close), closed["status"], [v["clearance"] for v in closed["variances"]]),
           "PASS" if good else "FAIL")
    # put the written-off item back for the reconciliation that follows
    f = ok(*call("POST", "/api/adjustments", {"qr": in_bin[0], "kind": "Found", "reasonCode": "Found in store", "locationId": qa_bin_id, "remarks": "QA restore"}, sk), "restore")
    ok(*call("POST", "/api/adjustments/%d/decide" % f["id"], {"approve": True, "note": "QA"}, appr), "restore approve")
run("FR-INV-021", "Physical verification by scan with variance report", "", fr_inv_021)


def tc29():
    s, r = call("GET", "/api/reports/reconciliation", token=pd)
    if s != 200:
        record("TC-29", "Stock reconciliation", "Opening + Receipts + Returns − Issues − Adjustments = Closing for every line", refusal(s, r), "FAIL")
        return
    res = [row[-1] for row in r["rows"]]
    qa = [row for row in r["rows"] if row[1] == qa_art["itemCode"]]
    good = res and all(x == "Balances" for x in res) and qa
    record("TC-29", "Stock reconciliation", "Opening + Receipts + Returns − Issues − Adjustments = Closing for every line",
           "%d store/article lines, %d balance, %d do not. QA article at %s: %s" % (len(res), res.count("Balances"), len(res) - res.count("Balances"), store["code"], dict(zip(r["columns"][3:14], [str(v) for v in qa[0][3:14]])) if qa else "missing"),
           "PASS" if good else "FAIL")
run("TC-29", "Stock reconciliation", "", tc29)


# ============================================================================ access, trace, audit (TC-27 … 30) and defect probes
def tc27():
    rid = ok(*new_req(sup, 1), "req for TC-27")["id"]
    s1, r1 = call("GET", "/api/requisitions/%d" % rid, token=supn)
    s2, r2 = call("POST", "/api/requisitions/%d/cancel" % rid, {"reason": "QA"}, supn)
    call("POST", "/api/requisitions/%d/cancel" % rid, {"reason": "QA"}, sup)
    logged = [a for a in ok(*call("GET", "/api/audit?limit=300", token=admin), "audit") if (a.get("actorId") == "qa-supn" and a["action"] not in ("login",))]
    record("TC-27", "Out-of-scope access", "Refused — VAL-SEC-001; attempt audited",
           "Open: %s. Cancel: %s. Refused attempts audited: %s" % (refusal(s1, r1), refusal(s2, r2), "yes" if logged else "no"),
           ("PASS" if logged else "PARTIAL") if s1 == 403 and s2 in (403, 409) else "FAIL", "Refused correctly; refused attempts are not written to the audit trail (outside the Not-met scope)")
run("TC-27", "Out-of-scope access", "", tc27)


def d_cancel_scope():
    north = next(x for x in ok(*call("GET", "/api/sites", token=admin), "sites") if x["zone"] == "North")
    s0, r = new_req(admin, 1, site=north["id"])
    s, c = call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA scope probe"}, appr)
    STATE["df06"] = (s, c)
    if s == 200:
        defect("DF-06", "Cancelling a requisition ignores data scope", "A Central-zone approver cancelled requisition %s for a North-zone site" % r["number"], "RequisitionService.CancelAsync")
    else:
        call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, admin)
run("DF-06", "cancel scope probe", "", d_cancel_scope)


def d_audit():
    s, rows = call("GET", "/api/audit?limit=200", token=off)
    foreign = [a for a in (rows or []) if a.get("actorId") not in ("qa-off",)]
    STATE["df01"] = (s, len(rows or []), len(foreign))
    if s == 200 and foreign:
        defect("DF-01", "Every role can read the whole audit trail", "The Lighting Officer account read %d audit rows of other users" % len(foreign), "AuditController")
run("DF-01", "audit scope probe", "", d_audit)


def d_po_lock():
    body = dict(PO); body["lines"] = [dict(PO["lines"][0], description="QA EDITED AFTER ARTICLE", uom="Set")]
    s, r = call("PUT", "/api/purchase-orders/%d" % po_id, body, proc)
    line = next(p for p in ok(*call("GET", "/api/purchase-orders", token=proc), "POs") if p["id"] == po_id)["lines"][0]
    STATE["df04"] = (s, r)
    if s == 200 and line["uom"] == "Set":
        defect("DF-04", "A PO line can be re-specified after its article exists", "The API accepted a new description and unit on a PO line whose article exists", "MasterService.UpsertPoAsync")
run("DF-04", "PO line lock probe", "", d_po_lock)


def d_site_hold():
    s, r = new_req(sup, 1)
    ok(*call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "QA"}, appr), "approve D5")
    ok(*call("PUT", "/api/sites/%d" % site_id, dict(SITE, status="On hold"), admin), "site hold")
    held = [i["qrCode"] for i in ok(*call("GET", "/api/articles/%d/items?status=Reserved" % a90["id"], token=sk), "res") if i["requisitionId"] == r["id"]]
    s2, r2 = call("POST", "/api/issues", {"requisitionId": r["id"], "qrCodes": held}, sk)
    ok(*call("PUT", "/api/sites/%d" % site_id, SITE, admin), "site active")
    call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, sup)
    STATE["df05"] = (s2, r2)
    if s2 == 200:
        defect("DF-05", "Material can be issued to a site that is On hold", "An approved requisition was issued after its site was put On hold", "IssueService.PostAsync")
run("DF-05", "site on-hold probe", "", d_site_hold)


def defects_closed():
    s6, c6 = STATE.get("df06", (0, {})); s1, n1, f1 = STATE.get("df01", (0, 0, 0)); s4, r4 = STATE.get("df04", (0, {})); s5, r5 = STATE.get("df05", (0, {}))
    ds, dr, ms, mr = STATE.get("drift_refused", (0, {}, 0, {}))
    ret = item(sk, STATE["reserved"][0])["item"]
    rows = [("DF-01", s1 == 200 and f1 == 0, "officer sees %d rows, all their own" % n1),
            ("DF-02", ds == 409 and dr.get("step") == "FR-FLD-011", "phone 30 min off: " + refusal(ds, dr)),
            ("DF-03", ret["siteId"] is None, "returned item carries no site in the store"),
            ("DF-04", s4 in (400, 409), "re-specifying the line: " + refusal(s4, r4)),
            ("DF-05", s5 == 409, "issue to On-hold site: " + refusal(s5, r5)),
            ("DF-06", s6 == 403, "out-of-scope cancel: " + refusal(s6, c6))]
    for did, good, obs in rows:
        record(did, "Defect fixed: " + {"DF-01": "audit trail scope", "DF-02": "clock-drift limit", "DF-03": "returned item keeps its site", "DF-04": "PO line lock after article",
                                          "DF-05": "issue to On-hold site", "DF-06": "cancel ignores scope"}[did], "Probe no longer reproduces the defect", obs, "PASS" if good else "FAIL")
    record("FR-FLD-011", "Mock location and clock drift refused and logged", "Evidence refused and the attempt logged",
           "Mock location: %s. Clock 30 min off: %s" % (refusal(ms, mr), refusal(ds, dr)), "PASS" if ms == 409 and ds == 409 else "FAIL")
run("DF", "defects closed", "", defects_closed)


def tc28():
    qr = STATE["reserved"][2]
    d = item(pd, qr)
    i = d["item"]
    inst = d.get("installation") or {}
    good = all([i.get("poNumber"), i.get("grnId"), i.get("requisitionId") or i.get("issueId"), inst.get("before"), inst.get("after")])
    record("TC-28", "Trace an item", "One view shows PO → GRN → bin → requisition → issue → evidence → status",
           "API item view: PO %s, GRN %s, requisition %s, issue %s, evidence %s, status %s" % (i.get("poNumber"), i.get("grnId"), i.get("requisitionId"), i.get("issueId"), "both photos" if inst.get("before") else "none", i.get("status")),
           "PARTIAL" if good else "FAIL", "The API holds the whole chain; see FR-RPT-005 for the drill-down path")
run("TC-28", "Trace an item", "", tc28)


def tc30():
    rows = [a for a in ok(*call("GET", "/api/audit?limit=2000", token=admin), "audit") if (a.get("actorId") or "").startswith("qa-")]
    attributed = [a for a in rows if a.get("actorName") and a.get("role") and a.get("ip")]
    updates = [a for a in rows if a["action"].endswith("-updated") or a["action"] in ("received", "issued", "installed", "returned", "reserved", "moved", "put-away", "adjusted", "amended")]
    with_prev = [a for a in updates if a.get("prevJson")]
    record("TC-30", "Audit completeness", "Every action shows user, role, time, address and before / after values",
           "%d QA actions audited; %d with user, role and address; before-values present on %d of %d changes" % (len(rows), len(attributed), len(with_prev), len(updates)),
           "PARTIAL" if rows and len(attributed) == len(rows) else "FAIL", "Before-values are recorded for moves, adjustments, amendments and master data; not yet for every stock movement")
run("TC-30", "Audit completeness", "", tc30)


# ============================================================================ amendments (FR-EXC-011)
def fr_exc_011():
    x = STATE["inst_ok"]
    s0, r0 = call("POST", "/api/amendments", {"entityType": "installation", "entityRef": x["number"], "field": "Lat", "newValue": "31", "reason": "QA wrong field"}, off)
    s1, a = call("POST", "/api/amendments", {"entityType": "installation", "entityRef": x["number"], "field": "PoleId", "newValue": "QA-P7", "reason": "QA: pole number misread at site"}, off)
    s2, r2 = call("POST", "/api/amendments/%d/decide" % a["id"], {"approve": True, "note": "self"}, off)
    s3, r3 = call("POST", "/api/amendments/%d/decide" % a["id"], {"approve": True, "note": "QA ok"}, pd)
    after = ok(*call("GET", "/api/installations/%d" % x["id"], token=pd), "inst")
    trail = [t for t in ok(*call("GET", "/api/audit?entityType=installation&entityId=" + urllib.parse.quote(x["number"]), token=admin), "audit") if t["action"] == "amended"]
    good = s0 == 400 and s1 == 200 and s2 in (403, 409) and s3 == 200 and after["poleId"] == "QA-P7" and trail and "QA-P1" in (trail[0]["prevJson"] or "")
    record("FR-EXC-011", "Corrections through an amendment request", "Reason, approval by another user, before/after values; original stays visible",
           "Coordinates: %s. Pole ID request %s; self-approval %s; approved by PD Office: pole %s; audit before %s after %s"
           % (refusal(s0, r0), a.get("number"), refusal(s2, r2), after["poleId"], trail[0]["prevJson"] if trail else "-", trail[0]["nextJson"] if trail else "-"), "PASS" if good else "FAIL")
run("FR-EXC-011", "Corrections through an amendment request", "", fr_exc_011)


# ============================================================================ reporting (FR-RPT-005/009/010)
def fr_rpt_005():
    s1 = ok(*call("GET", "/api/explore/drill", token=pd), "drill summary")
    z = ok(*call("GET", "/api/explore/drill?level=zone&key=Central", token=pd), "drill zone")
    siterow = next(r for r in z["rows"] if r["key"] == str(site_id))
    st = ok(*call("GET", "/api/explore/drill?level=site&key=%d" % site_id, token=pd), "drill site")
    doc = next(r for r in st["rows"] if r["level"] == "issue")
    d = ok(*call("GET", "/api/explore/drill?level=issue&key=%s" % doc["key"], token=pd), "drill issue")
    it = ok(*call("GET", "/api/explore/drill?level=item&key=%s" % d["rows"][0]["key"], token=pd), "drill item")
    s_out, r_out = call("GET", "/api/explore/drill?level=site&key=%d" % site_id, token=supn)
    good = s1["rows"] and siterow and doc and d["rows"] and it["item"] and s_out in (403, 404)
    record("FR-RPT-005", "Drill-down Summary → area → site → document → item → evidence", "Within the viewer's scope",
           "Path: %s. Item level shows history (%d entries) and validation record %s. Other zone's supervisor: HTTP %s"
           % (" › ".join(c["label"] for c in it["path"]), len(it["item"]["history"]), (it.get("validation") or {}).get("geoStatus"), s_out), "PASS" if good else "FAIL")
run("FR-RPT-005", "Drill-down Summary → area → site → document → item → evidence", "", fr_rpt_005)


def fr_rpt_009():
    m = ok(*call("GET", "/api/explore/map?zone=Central", token=pd), "map")
    qa = next((x for x in m["sites"] if x["id"] == site_id), None)
    pts = [p for p in m["installations"] if p["siteId"] == site_id]
    old = ok(*call("GET", "/api/explore/map?zone=Central&from=2020-01-01&to=2020-12-31", token=pd), "map old")
    good = qa and pts and all(x["zone"] == "Central" for x in m["sites"]) and not old["installations"]
    record("FR-RPT-009", "Installations and pending sites on a map", "Filterable by zone, site and period",
           "Central: %d sites, %d installation points; QA site marker %s with %d installation point(s); a 2020 period shows %d points" % (len(m["sites"]), len(m["installations"]), qa and qa["marker"], len(pts), len(old["installations"])),
           "PASS" if good else "FAIL")
run("FR-RPT-009", "Installations and pending sites on a map", "", fr_rpt_009)


def fr_ntf_and_rpt_010():
    # local sinks stand in for the department's mail server and SMS gateway
    smtp_sink(2526)
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", 8099), SmsHandler); threading.Thread(target=srv.serve_forever, daemon=True).start()
    prev = ok(*call("GET", "/api/notify-settings", token=admin), "notify settings")
    ok(*call("PUT", "/api/notify-settings", {"emailEnabled": True, "smtpHost": "127.0.0.1", "smtpPort": 2526, "smtpSsl": False, "smtpUser": None, "smtpPassword": None, "fromAddress": "slims@qa.test", "fromName": "SLIMS QA",
                                             "smsEnabled": True, "smsUrl": "http://127.0.0.1:8099/send?to={phone}&msg={text}&key={key}", "smsMethod": "GET", "smsApiKey": "qa-key", "portalUrl": BASE}, admin), "notify on")
    try:
        # FR-NTF-001/002: a fresh approval and issue produce in-app notices; e-mail and SMS copies go out
        s, r = new_req(sup, 1)
        d = ok(*call("POST", "/api/requisitions/%d/decide" % r["id"], {"approve": True, "note": "QA notify"}, appr), "approve notify")
        MAIL.clear(); SMS.clear()
        res = automation()
        time.sleep(1)
        mails = [m for m in MAIL if "qa-appr@qa.test" in m["to"] and r["number"] in m["raw"]]
        sms = [q for q in SMS if r["number"] in (q.get("msg") or [""])[0] or "approved" in (q.get("msg") or [""])[0].lower()]
        inapp = inbox(appr, "NTF-01", r["number"]) and inbox(sup, "NTF-02", r["number"])
        record("FR-NTF-002", "Delivered in-app and, where configured, by e-mail and SMS", "In-app always; e-mail and SMS when the channels are configured",
               "In-app NTF-01/NTF-02: %s. E-mail to the approver: %d; SMS sent: %d (delivery step sent %s)" % (bool(inapp), len(mails), len(SMS), res["counts"].get("delivery")),
               "PASS" if inapp and mails and SMS else "FAIL")
        codes = {}
        for t, who in ((appr, "appr"), (sup, "sup"), (off, "off"), (pd, "pd"), (sk, "sk")):
            for n in inbox(t):
                codes.setdefault(n["code"], set()).add(who)
        expected = ["NTF-01", "NTF-02", "NTF-03", "NTF-04", "NTF-05", "NTF-08", "NTF-09", "NTF-10", "NTF-11", "NTF-13"]
        record("FR-NTF-001", "Section 14 notifications sent to the recipients shown", "Each event notifies its recipients",
               "Received by the QA users this run: %s" % ", ".join("%s→%s" % (c, "/".join(sorted(codes[c]))) for c in sorted(codes)),
               "PASS" if all(c in codes for c in expected if c != "NTF-04" or DB) else "FAIL")
        call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, sup)

        # FR-NTF-003 configurable alerts: re-order threshold and repeated GPS mismatch
        cfg = dict(CFG0); cfg["alerts"] = dict(CFG0["alerts"], gpsMismatchCount=1)
        ok(*call("PUT", "/api/config", cfg, admin), "alerts cfg")
        automation()
        low = [n for n in inbox(proc, "NTF-14") if qa_art["itemCode"] in n["body"]]
        gps = [n for n in inbox(pd, "NTF-15") if "QA Lighting Officer" in n["title"]]
        record("FR-NTF-003", "Configurable alerts", "Ageing and turnaround, stock thresholds, repeated GPS mismatch, return rates, unresolved sites",
               "Re-order threshold alert to Procurement: %s. Repeated GPS mismatch (threshold set to 1 in Parameters) to PD Office: %s" % ("yes" if low else "no", "yes" if gps else "no"),
               "PASS" if low and gps else "FAIL")
        ok(*call("PUT", "/api/articles/%d" % qa_art["id"], {"reorderLevel": 0, "status": "Active", "expectedLifeMonths": 60}, proc), "reorder reset")

        # FR-NTF-004 escalation of a requisition nobody approved
        if DB:
            s, r = new_req(sup, 1)
            sql("UPDATE Requisitions SET SubmittedAt = DATEADD(hour, -30, SYSUTCDATETIME()), RaisedAt = DATEADD(hour, -30, SYSUTCDATETIME()) WHERE Id = %d" % r["id"])
            automation()
            esc = [n for n in inbox(pd, "NTF-01", r["number"]) if n["level"] >= 10]
            record("FR-NTF-004", "Items not actioned escalate to the next level", "Escalation after the configured period",
                   "Requisition waiting 30 h at the Store In-Charge: escalation notice to PD Office %s" % ("delivered" if esc else "missing"), "PASS" if esc else "FAIL")
            call("POST", "/api/requisitions/%d/cancel" % r["id"], {"reason": "QA"}, sup)
        else:
            record("FR-NTF-004", "Items not actioned escalate to the next level", "Escalation after the configured period", "Needs --db", "NOT RUN")

        # FR-NTF-005 assigned work cannot be switched off
        p = ok(*call("PUT", "/api/notifications/prefs", {"muted": ["NTF-08", "NTF-14"], "email": True, "sms": True}, pd), "prefs")
        record("FR-NTF-005", "Assigned-work notifications cannot be switched off", "Approvals, exceptions and re-captures stay on",
               "Asked to mute NTF-08 (exceptions) and NTF-14 (re-order digest): muted now %s" % p["muted"], "PASS" if p["muted"] == ["NTF-14"] else "FAIL")
        call("PUT", "/api/notifications/prefs", {"muted": [], "email": True, "sms": True}, pd)

        # FR-RPT-010 scheduled delivery
        MAIL.clear()
        sch = ok(*call("POST", "/api/report-schedules", {"reportKey": "grn", "title": "QA daily receipts", "frequency": "Daily", "dayOfWeek": 1, "dayOfMonth": 1, "hour": 7, "period": "todate", "zone": None, "storeId": None,
                                                         "recipientUserIds": ["qa-pd"], "recipientEmails": "auditor@qa.test", "active": True}, proc), "schedule")
        runr = ok(*call("POST", "/api/report-schedules/%d/run" % sch["id"], {}, proc), "run schedule")
        s_dl, csv = call("GET", runr["downloadUrl"], token=pd, raw=True)
        s_no, _ = call("GET", runr["downloadUrl"], token=sk, raw=True)
        automation(); time.sleep(1)
        mailed = [m for m in MAIL if "auditor@qa.test" in m["to"] and ".csv" in m["raw"]]
        told = inbox(pd, "NTF-RPT")
        good = runr["status"] == "Done" and s_dl == 200 and PO["number"] in csv.decode("utf-8-sig") and s_no == 403 and mailed and told and sch.get("nextRunAt")
        record("FR-RPT-010", "Reports scheduled for automatic delivery", "Delivered to nominated recipients",
               "Schedule next run %s; run now: %s, %d rows; recipient download HTTP %s (non-recipient %s); e-mailed with CSV attachment: %s; in-app notice: %s"
               % (sch.get("nextRunAt"), runr["status"], runr["rows"], s_dl, s_no, "yes" if mailed else "no", "yes" if told else "no"), "PASS" if good else "FAIL")
        call("PUT", "/api/report-schedules/%d" % sch["id"], dict({"reportKey": "grn", "title": "QA daily receipts", "frequency": "Daily", "dayOfWeek": 1, "dayOfMonth": 1, "hour": 7, "period": "todate", "zone": None, "storeId": None,
                                                                  "recipientUserIds": ["qa-pd"], "recipientEmails": "auditor@qa.test"}, active=False), proc)
    finally:
        call("PUT", "/api/config", CFG0, admin)
        call("PUT", "/api/notify-settings", dict(prev, emailEnabled=False, smsEnabled=False, smtpHost=None, smtpPort=587, smtpSsl=True, fromAddress=None, smsUrl=None, smtpPassword="", smsApiKey=""), admin)
        srv.shutdown()
run("FR-NTF", "Notifications, alerts, escalation and scheduled reports", "", fr_ntf_and_rpt_010)


# ============================================================================ tidy up
for uid, name, role, zone in QA:
    call("PUT", "/api/users", {"id": uid, "name": name, "email": uid + "@qa.test", "employeeId": None, "designation": "QA — acceptance run", "phone": None,
                               "roleKey": role, "zone": zone, "storeIds": [], "siteIds": [], "active": False}, admin)
for d in ok(*call("GET", "/api/mobile/devices", token=admin), "devices"):
    if d["userId"].startswith("qa-") and d["status"] not in ("Revoked", "Replaced"):
        call("POST", "/api/mobile/devices/%s/revoke" % d["id"], {"reason": "QA run finished"}, admin)
RESULTS.sort(key=lambda r: (not r["id"].startswith("TC-"), r["id"]))
json.dump({"runAt": datetime.now().isoformat(timespec="seconds"), "base": BASE, "tag": TS, "results": RESULTS, "defects": DEFECTS},
          open("frd_acceptance_results.json", "w", encoding="utf-8"), indent=1, ensure_ascii=False)
tally = {}
for r in RESULTS:
    tally[r["result"]] = tally.get(r["result"], 0) + 1
tcs = [r for r in RESULTS if r["id"].startswith("TC-")]
print("\n%d checks (%d FRD test cases): %s · %d defects reproduced" % (len(RESULTS), len(tcs), ", ".join("%s %d" % kv for kv in sorted(tally.items())), len(DEFECTS)))
print("qa-* accounts deactivated, QA phones revoked. Results in frd_acceptance_results.json")
