# -*- coding: utf-8 -*-
"""End-to-end check of the SLIMS business rules against a running API.

Walks one item of material through the whole chain — requisition, approval, scan-based issue,
geo-tagged installation, geo-fence exception, return, warranty classification — and asserts that
each control in the BRD actually refuses what it is supposed to refuse.

    python e2e_check.py [http://localhost:5091]
"""
import io
import json
import sys
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone

from PIL import Image

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5091").rstrip("/")
PW = "Slims2026admin"
ok_count = 0
fail = []


def call(method, path, body=None, token=None, form=None):
    url = BASE + path
    headers = {}
    data = None
    if token:
        headers["Authorization"] = "Bearer " + token
    if form is not None:
        boundary = "----slims" + uuid.uuid4().hex
        buf = io.BytesIO()
        for key, value in form:
            buf.write(("--%s\r\n" % boundary).encode())
            if isinstance(value, tuple):
                name, content, ctype = value
                buf.write(('Content-Disposition: form-data; name="%s"; filename="%s"\r\n' % (key, name)).encode())
                buf.write(("Content-Type: %s\r\n\r\n" % ctype).encode())
                buf.write(content)
            else:
                buf.write(('Content-Disposition: form-data; name="%s"\r\n\r\n' % key).encode())
                buf.write(str(value).encode())
            buf.write(b"\r\n")
        buf.write(("--%s--\r\n" % boundary).encode())
        data = buf.getvalue()
        headers["Content-Type"] = "multipart/form-data; boundary=" + boundary
    elif body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            raw = r.read().decode()
            return r.status, (json.loads(raw) if raw.strip() else None)
    except urllib.error.HTTPError as e:
        raw = e.read().decode()
        try:
            return e.code, json.loads(raw)
        except ValueError:
            return e.code, {"error": raw[:300]}


def check(label, cond, detail=""):
    global ok_count
    if cond:
        ok_count += 1
        print("  PASS  %s" % label)
    else:
        fail.append(label)
        print("  FAIL  %s  %s" % (label, detail))


def refused(label, status, payload, expect_rule=None):
    got = (payload or {}).get("step")
    cond = status in (400, 403, 409) and (expect_rule is None or got == expect_rule)
    check(label, cond, "status=%s step=%s error=%s" % (status, got, (payload or {}).get("error")))


def login(uid, pw=PW):
    s, r = call("POST", "/api/auth/login", {"id": uid, "password": pw})
    if s != 200:
        raise SystemExit("login %s failed: %s %s" % (uid, s, r))
    return r["accessToken"]


def jpeg(colour):
    b = io.BytesIO()
    Image.new("RGB", (640, 480), colour).save(b, "JPEG", quality=70)
    return b.getvalue()


print("SLIMS end-to-end check against", BASE)
admin = login("sysadmin")
print("\n1. Provision accounts")
for uid in ("sup-central", "sic-cen", "sk-cen", "off-central", "proc"):
    s, r = call("POST", "/api/users/%s/password" % uid, {"password": PW}, admin)
    check("temporary password issued to %s" % uid, s == 200, str(r))
    s, _ = call("POST", "/api/auth/login", {"id": uid, "password": PW})
    check("%s can sign in" % uid, s == 200)

sup, sic, sk, off, proc = (login(u) for u in ("sup-central", "sic-cen", "sk-cen", "off-central", "proc"))

print("\n2. Master data in place")
_, stores = call("GET", "/api/stores", token=sk)
_, sites = call("GET", "/api/sites", token=sup)
_, articles = call("GET", "/api/articles", token=sk)
store = next(x for x in stores if x["code"] == "CEN01")
site = next(x for x in sites if x["code"] == "SITE-C-MALL-0001")
art = next(x for x in articles if x["itemCode"].startswith("LUM-LED-090W"))
check("central store has a quarantine bin", any(l["kind"] == "Quarantine" for l in store["locations"]))
check("central store has a scrap bin", any(l["kind"] == "Scrap" for l in store["locations"]))
check("item code carries its PO reference", art["poNumber"] in art["itemCode"].replace("PO", "PO-") or art["poNumber"].replace("-", "") in art["itemCode"])
check("article has stock on hand", art["onHand"] > 0, json.dumps(art)[:200])

print("\n3. Requisition (BR-007, BR-008, BR-026)")
s, req = call("POST", "/api/requisitions", {
    "siteId": site["id"], "storeId": store["id"], "needBy": None,
    "purpose": "E2E — Mall Road poles 12-16", "lines": [{"articleId": art["id"], "qty": 3}], "submit": True}, sup)
check("supervisor raises and submits a requisition", s == 200 and req["status"] == "Submitted", str(req)[:200])

s, r = call("POST", "/api/requisitions/%d/decide" % req["id"], {"approve": True, "note": "self"}, sup)
refused("a supervisor has no approval permission at all", s, r, "permission")

# BR-026 needs an account that may both raise and approve; only the administrator can
s, own = call("POST", "/api/requisitions", {"siteId": site["id"], "storeId": store["id"], "needBy": None,
              "purpose": "E2E self-approval probe", "lines": [{"articleId": art["id"], "qty": 1}], "submit": True}, admin)
s, r = call("POST", "/api/requisitions/%d/decide" % own["id"], {"approve": True, "note": "self"}, admin)
refused("nobody may approve a requisition they raised themselves", s, r, "BR-026")
call("POST", "/api/requisitions/%d/cancel" % own["id"], {"reason": "E2E probe"}, admin)

s, r = call("POST", "/api/issues", {"requisitionId": req["id"], "qrCodes": ["x"]}, sk)
refused("nothing can be issued before approval", s, r, "BR-006")

s, req = call("POST", "/api/requisitions/%d/decide" % req["id"],
              {"approve": True, "note": "Approved for the Mall Road stretch", "lines": None}, sic)
check("store in-charge approves and stock is reserved", s == 200 and req["status"] == "Approved", str(req)[:200])
check("an issue slip is generated on approval", bool(req.get("issueSlipNo")))

_, items = call("GET", "/api/articles/%d/items?status=Reserved" % art["id"], token=sk)
check("three labels are ring-fenced against the requisition", len(items) == 3, "reserved=%d" % len(items))
qrs = [i["qrCode"] for i in items]

print("\n4. Issue by scan (BR-010, BR-011, BR-002)")
s, r = call("POST", "/api/issues/scan", {"qr": "SLIMS|FAKE|000001|ABCDEF", "requisitionId": req["id"], "already": []}, sk)
check("a forged QR payload is rejected", s == 200 and r["ok"] is False and r["step"] == "BR-002", str(r)[:160])

s, r = call("POST", "/api/issues/scan", {"qr": qrs[0], "requisitionId": req["id"], "already": []}, sk)
check("a valid reserved label scans clean", s == 200 and r["ok"] is True, str(r)[:160])

_, spare = call("GET", "/api/articles/%d/items?status=Received" % art["id"], token=sk)
binned = next(i for i in spare if i["locationCode"])
s, r = call("POST", "/api/issues", {"requisitionId": req["id"], "qrCodes": qrs + [binned["qrCode"]]}, sk)
refused("issuing more than the approved quantity is refused", s, r, "BR-010")

s, issue = call("POST", "/api/issues", {"requisitionId": req["id"], "qrCodes": qrs, "issuedTo": "Field team A", "vehicle": "LEA-1234"}, sk)
check("issue posts with a gate pass", s == 200 and issue.get("gatePassNo"), str(issue)[:200])
check("custody moves to the site", all(i["siteCode"] == site["code"] for i in issue["items"]))
check("the store bin is relieved", all(i["locationCode"] is None for i in issue["items"]))

s, r = call("POST", "/api/issues", {"requisitionId": req["id"], "qrCodes": [qrs[0]]}, sk)
refused("a fully-issued requisition cannot be drawn against again", s, r, "BR-006")

s, _ = call("POST", "/api/issues/%d/acknowledge" % issue["id"], {"lat": site["lat"], "lng": site["lng"], "accuracyM": 8}, off)
check("the officer acknowledges receipt at site", s == 200)

print("\n5. Installation with geo-tagged evidence (BR-013, BR-015, BR-016)")
now = datetime.now(timezone.utc).isoformat()


def install(qr, lat, lng, with_after=True):
    form = [("Qr", qr), ("PoleId", "P-014"), ("Lat", lat), ("Lng", lng), ("AccuracyM", 7),
            ("CapturedAt", now), ("Remarks", "E2E"), ("Offline", "false"),
            ("Before", ("before.jpg", jpeg((90, 90, 110)), "image/jpeg")),
            ("BeforeLat", lat), ("BeforeLng", lng), ("BeforeAccuracyM", 7), ("BeforeAt", now)]
    if with_after:
        form += [("After", ("after.jpg", jpeg((200, 190, 140)), "image/jpeg")),
                 ("AfterLat", lat), ("AfterLng", lng), ("AfterAccuracyM", 7), ("AfterAt", now)]
    return call("POST", "/api/installations", token=off, form=form)


s, r = install(qrs[0], site["lat"], site["lng"], with_after=False)
refused("an installation without the AFTER photograph is refused", s, r, "BR-013")

s, inst = install(qrs[0], site["lat"] + 0.00012, site["lng"] + 0.00010)
check("evidence inside the geo-fence is confirmed automatically",
      s == 200 and inst["geoStatus"] == "Confirmed", str(inst)[:220])
check("the measured deviation is recorded", inst["distanceM"] < site["geofenceM"] + 20)

s, bad = install(qrs[1], site["lat"] + 0.02, site["lng"] + 0.02)
check("evidence outside the geo-fence is held for verification",
      s == 200 and bad["geoStatus"] == "PendingVerification" and bad["distanceM"] > 1000, str(bad)[:220])

s, r = call("POST", "/api/installations/%d/verify" % bad["id"], {"approve": True, "justification": "ok"}, off)
refused("an officer has no verification permission at all", s, r, "permission")

s, r = call("POST", "/api/installations/%d/verify" % bad["id"], {"approve": True, "justification": "too short"}, sup)
refused("approving a deviation needs a written justification", s, r, "BR-017")

s, r = call("POST", "/api/installations/%d/verify" % bad["id"],
            {"approve": True, "justification": "Verified in the field; the pole sits at the far end of the surveyed stretch."}, sup)
check("the supervisor can approve a deviation with a justification",
      s == 200 and r["geoStatus"] == "ApprovedException", str(r)[:200])

print("\n6. Returns, warranty and condemnation (BR-018, BR-019, BR-020)")
s, ret = call("POST", "/api/returns/report", {"qr": qrs[0], "reason": "Driver failure", "kind": "OutOfOrder", "remarks": "E2E"}, off)
check("the officer reports the item out of order", s == 200 and ret["itemStatus"] == "OutOfOrder", str(ret)[:200])

s, ret = call("POST", "/api/returns/receive", {"qr": qrs[0], "storeId": store["id"], "storeLocationId": None}, sk)
check("the store scans it back into quarantine", s == 200 and ret["itemStatus"] == "Returned", str(ret)[:200])
qbin = next(l for l in store["locations"] if l["kind"] == "Quarantine")
check("it lands in the quarantine bin, not issuable stock", ret["locationCode"] == qbin["fullCode"], str(ret["locationCode"]))
check("warranty eligibility is computed by the system", ret["warrantyEligible"] is True and ret["warrantyExpires"])

s, r = call("POST", "/api/returns/%d/dispose" % ret["id"], {"scrapValue": 100}, sic)
refused("an item cannot be disposed before it is condemned", s, r, "BR-020")

s, r = call("POST", "/api/returns/%d/classify" % ret["id"], {"classification": "Warranty", "claimRef": "WRC/E2E/0001"}, sic)
check("a within-warranty failure is routed as a claim", s == 200 and r["itemStatus"] == "WarrantyClaim", str(r)[:200])

stock_bin = next(l for l in store["locations"] if l["kind"] == "Stock")
s, r = call("POST", "/api/returns/%d/reinward" % ret["id"], {"storeLocationId": stock_bin["id"], "remarks": "Replaced under warranty"}, sk)
check("the replacement re-enters issuable stock on the same label", s == 200 and r["itemStatus"] == "Received", str(r)[:200])

print("\n7. Traceability and reporting")
s, detail = call("GET", "/api/items/%s" % qrs[0].replace("|", "%7C"), token=sic)
check("an item can be traced back from its QR payload", s == 200, str(detail)[:160])
if s == 200:
    actions = [h["action"] for h in detail["history"]]
    for step in ("received", "reserved", "issued", "at-site", "installed", "out-of-order", "returned", "warranty-claimed", "re-inwarded"):
        check("history records '%s'" % step, step in actions, str(actions))
    check("the PO reference survives the whole chain", detail["item"]["poNumber"] == art["poNumber"])

for key in ("grn", "requisitions", "issues", "installations", "exceptions", "returns", "warranty", "consumption-po", "consumption-site", "failures", "ageing", "officers"):
    s, rep = call("GET", "/api/reports/%s" % key, token=sic)
    check("report '%s' runs" % key, s == 200 and "columns" in rep, str(rep)[:140])

s, audit = call("GET", "/api/audit?limit=500", token=sic)
check("the audit trail is populated", s == 200 and len(audit) > 30, "rows=%s" % (len(audit) if s == 200 else audit))
check("every audit row is attributed to a user", all(a["actorId"] for a in audit) if s == 200 else False)

print("\n%d checks passed, %d failed" % (ok_count, len(fail)))
if fail:
    print("FAILED:")
    for f in fail:
        print("  -", f)
sys.exit(1 if fail else 0)
