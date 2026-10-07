# -*- coding: utf-8 -*-
"""Snapshots every screen's data from a running SLIMS API into demo/src/data/snapshot.json for the standalone demo.
   python tools/crawl.py http://127.0.0.1:5192 <password>
The keys are normalised request lines ("GET /api/x?a=1&b=2", parameters sorted, empty ones dropped); per-user answers are
stored under "<userId>::GET ...". Evidence photographs referenced by the data are embedded as base64."""
import base64, json, os, sys, urllib.error, urllib.parse, urllib.request
from collections import deque

BASE = sys.argv[1].rstrip("/")
PW = sys.argv[2]
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "data", "snapshot.json")
snap, files = {}, {}


def norm(path):
    u = urllib.parse.urlsplit(path)
    q = sorted((k, v) for k, v in urllib.parse.parse_qsl(u.query, keep_blank_values=True) if v != "")
    return u.path + ("?" + urllib.parse.urlencode(q) if q else "")


def raw(path, token):
    req = urllib.request.Request(BASE + path, headers={"Authorization": "Bearer " + token})
    try:
        with urllib.request.urlopen(req, timeout=120) as r: return r.status, r.read(), r.headers.get("Content-Type", "")
    except urllib.error.HTTPError as e: return e.code, e.read(), ""


def get(path, token, user=None, store=True):
    s, body, _ = raw(path, token)
    if s != 200: return None
    data = json.loads(body) if body.strip() else None
    if store: snap[(user + "::" if user else "") + "GET " + norm(path)] = data
    return data


def login(uid):
    req = urllib.request.Request(BASE + "/api/auth/login", data=json.dumps({"id": uid, "password": PW}).encode(), headers={"Content-Type": "application/json"}, method="POST")
    with urllib.request.urlopen(req) as r: return json.loads(r.read())["accessToken"]


admin = login("sysadmin")
A = lambda p: get(p, admin)

# ---- anonymous and catalogue data
for p in ("/api/meta", "/api/config", "/api/auth/picker", "/api/mobile/app-info", "/api/health"): A(p)
users = A("/api/users") or []
for p in ("/api/users/roles", "/api/vendors", "/api/stores", "/api/articles", "/api/articles?status=Active", "/api/grn", "/api/grn/discrepancies",
          "/api/issues", "/api/installations", "/api/returns", "/api/returns/overdue", "/api/transfers", "/api/adjustments", "/api/counts", "/api/amendments",
          "/api/amendments/fields", "/api/report-schedules", "/api/reports", "/api/notify-settings", "/api/mobile/devices", "/api/stock", "/api/explore/map",
          "/api/audit", "/api/audit?limit=500", "/api/stock-control/receiving", "/api/requisitions", "/api/purchase-orders", "/api/sites", "/api/dashboard"):
    A(p)
cfg = snap["GET /api/config"]
for st in ("Draft", "Submitted", "Recommended", "Approved", "PartIssued", "Issued", "Rejected", "Cancelled", "Lapsed"): A(f"/api/requisitions?status={st}")
for st in ("Open", "Closed"): A(f"/api/purchase-orders?status={st}")
for st in ("Pending", "Approved", "Rejected"): A(f"/api/adjustments?status={st}"); A(f"/api/amendments?status={st}")
for st in ("Confirmed", "PendingVerification", "ApprovedException", "Rejected", "MissingEvidence"): A(f"/api/installations?geoStatus={st}")
for st in ("Reported", "Received", "Classified", "Closed", "Open"): A(f"/api/returns?stage={st}")
for st in ("InTransit", "Received"): A(f"/api/transfers?status={st}")
for z in cfg.get("zones", []): A(f"/api/sites?zone={urllib.parse.quote(z)}")
A("/api/issues?unacknowledged=true")

# ---- details
for r in snap["GET /api/requisitions"] or []: A(f"/api/requisitions/{r['id']}"); A(f"/api/issues/available?requisitionId={r['id']}")
for i in snap["GET /api/issues"] or []: A(f"/api/issues/{i['id']}")
for g in snap["GET /api/grn"] or []: A(f"/api/grn/{g['id']}/items")
for t in snap["GET /api/transfers"] or []: A(f"/api/transfers/{t['id']}")
for c in snap["GET /api/counts"] or []: A(f"/api/counts/{c['id']}")
for x in snap["GET /api/installations"] or []: A(f"/api/installations/{x['id']}")
for s in snap["GET /api/stores"] or []:
    A(f"/api/stock?storeId={s['id']}"); A(f"/api/stores/{s['id']}/labels"); A(f"/api/stock-control/receiving?storeId={s['id']}")
    A(f"/api/requisitions/availability/{s['id']}")
    for l in s["locations"]: A(f"/api/stock/location/{l['id']}")
items = {}
for a in snap["GET /api/articles"] or []:
    for it in A(f"/api/articles/{a['id']}/items") or []: items[it["id"]] = it
    for st in ("Created", "Received", "Reserved", "Issued", "Installed"): A(f"/api/articles/{a['id']}/items?status={st}")
print("items", len(items))
for iid in items: A(f"/api/items/{iid}")
for k in [r["key"] for r in snap["GET /api/reports"] or []]: A(f"/api/reports/{k}")

# ---- explore drill-down, breadth first
seen, qd = set(), deque([("summary", None)])
while qd and len(seen) < 400:
    lv, key = qd.popleft()
    if (lv, key) in seen: continue
    seen.add((lv, key))
    d = A(f"/api/explore/drill?level={lv}" + (f"&key={urllib.parse.quote(key)}" if key else ""))
    for row in (d or {}).get("rows", []):
        if row.get("level"): qd.append((row["level"], row.get("key")))
print("drill nodes", len(seen))

# ---- per-user views (scope, inbox, phone)
for u in users:
    try: t = login(u["id"])
    except Exception as e: print("login", u["id"], e); continue
    uid = u["id"]
    for p in ("/api/auth/me", "/api/mobile/mine", "/api/notifications/unread", "/api/notifications?take=200", "/api/notifications?take=200&unread=true",
              "/api/notifications/prefs", "/api/dashboard", "/api/issues/field", "/api/installations?geoStatus=PendingVerification", "/api/requisitions",
              "/api/sites", "/api/stores", "/api/issues?unacknowledged=true", "/api/installations"):
        get(p, t, uid)

# ---- evidence photographs
def walk(o):
    if isinstance(o, dict):
        for v in o.values(): yield from walk(v)
    elif isinstance(o, list):
        for v in o: yield from walk(v)
    elif isinstance(o, str) and (o.startswith("/api/files/") or o.startswith("/api/report-runs/")): yield o
for url in sorted(set(walk(snap))):
    s, body, ct = raw(url, admin)
    if s == 200: files[norm(url)] = {"type": ct or "image/jpeg", "b64": base64.b64encode(body).decode()}
print("files", len(files), "·", sum(len(f["b64"]) for f in files.values()) // 1024, "KB")

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f: json.dump({"takenAt": snap.get("GET /api/health", {}).get("time"), "data": snap, "files": files}, f, separators=(",", ":"))
print("keys", len(snap), "->", os.path.getsize(OUT) // 1024, "KB")
