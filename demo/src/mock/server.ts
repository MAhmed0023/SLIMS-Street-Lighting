/* eslint-disable @typescript-eslint/no-explicit-any */
// SLIMS demo "server": answers every /api request inside the browser from a snapshot of a real SLIMS database,
// and carries out the main workflows in memory so the demo can be walked end to end. Nothing leaves the page.
import snapshot from "../data/snapshot.json";

type Json = any;
const SNAP = snapshot as { takenAt: string; data: Record<string, Json>; files: Record<string, { type: string; b64: string }> };
const db: Record<string, Json> = SNAP.data;
const files: Record<string, { type: string; b64?: string; blob?: Blob }> = SNAP.files;
export const PHONE_SERVER = "http://172.16.10.229:3001";

class Fail extends Error { constructor(public status: number, message: string, public step: string | null = null) { super(message); } }
const fail = (status: number, message: string, step: string | null = null): never => { throw new Fail(status, message, step); };

// ---------------------------------------------------------------- keys and lookups
function norm(url: string) {
  const u = new URL(url, "http://x");
  const q = [...u.searchParams.entries()].filter(([, v]) => v !== "").sort((a, b) => a[0] === b[0] ? a[1].localeCompare(b[1]) : a[0].localeCompare(b[0]));
  const s = new URLSearchParams(q).toString();
  return decodeURIComponent(u.pathname) + (s ? "?" + s : "");
}
const clone = <T,>(v: T): T => v === undefined ? v : JSON.parse(JSON.stringify(v));
const nowIso = () => new Date().toISOString();
const list = (path: string): Json[] => (db["GET " + path] ??= []);
const find = (path: string, id: number | string) => list(path).find((x: Json) => String(x.id) === String(id));

/** Every copy of an entity (lists, details, per-user views) is patched, so all screens agree after a change. */
function patchAll(match: (o: Json) => boolean, patch: Json | ((o: Json) => void)) {
  const seen = new Set<Json>();
  const walk = (o: Json) => {
    if (!o || typeof o !== "object" || seen.has(o)) return;
    seen.add(o);
    if (Array.isArray(o)) { o.forEach(walk); return; }
    if (match(o)) { if (typeof patch === "function") patch(o); else Object.assign(o, clone(patch)); }
    for (const v of Object.values(o)) if (v && typeof v === "object") walk(v);
  };
  for (const k of Object.keys(db)) walk(db[k]);
}
/** A new record goes into its list and every per-user copy of that list. */
function insert(path: string, obj: Json, front = true) {
  for (const k of Object.keys(db)) if (k === "GET " + path || k.endsWith("::GET " + path)) { const a = db[k]; if (Array.isArray(a)) front ? a.unshift(clone(obj)) : a.push(clone(obj)); }
  if (!db["GET " + path]) db["GET " + path] = [clone(obj)];
}
const nextId = (path: string) => Math.max(0, ...list(path).map((x: Json) => Number(x.id) || 0)) + 1;
function nextNumber(path: string, prefix: string, width = 5) {
  const n = Math.max(0, ...list(path).map((x: Json) => String(x.number ?? "")).filter((s: string) => s.startsWith(prefix)).map((s: string) => parseInt(s.slice(prefix.length), 10) || 0)) + 1;
  return prefix + String(n).padStart(width, "0");
}
const fy = () => { const d = new Date(); const y = d.getMonth() >= 6 ? d.getFullYear() : d.getFullYear() - 1; return `${String(y).slice(2)}-${String(y + 1).slice(2)}`; };

// ---------------------------------------------------------------- who is calling
const users = (): Json[] => db["GET /api/users"] ?? [];
function me(uid: string): Json {
  const u = db[`${uid}::GET /api/auth/me`] ?? users().find((x: Json) => x.id === uid);
  if (!u) fail(401, "Signed out.");
  return u;
}
function callerOf(headers: Headers): string | null {
  const a = headers.get("Authorization") ?? "";
  const m = /^Bearer (?:demo|dev)-(.+)$/.exec(a);
  return m ? m[1] : null;
}
const need = (u: Json, perm: string) => { if (!u.permissions?.[perm]) fail(403, `Your role (${u.roleLabel}) does not have the "${perm}" permission.`, "permission"); };

// ---------------------------------------------------------------- items (one row per QR label)
function allItems(): Json[] {
  const out = new Map<string, Json>();
  for (const k of Object.keys(db)) if (/^GET \/api\/articles\/\d+\/items$/.test(k)) for (const i of db[k] ?? []) out.set(i.id, i);
  return [...out.values()];
}
const itemByQr = (qr: string) => allItems().find((i) => i.qrCode === qr.trim());
const locKind = new Map<number, string>();
const locById = new Map<number, Json>();
for (const s of db["GET /api/stores"] ?? []) for (const l of s.locations) { locKind.set(l.id, l.kind); locById.set(l.id, l); }
function patchItem(id: string, p: Json) { patchAll((o) => o.id === id && "qrCode" in o && "serial" in o, p); }

// ---------------------------------------------------------------- GET: snapshot, with live filtering of lists
const SIMPLE = new Set(["status", "geoStatus", "zone", "storeId", "siteId", "kind", "entityType", "entityRef", "requisitionId", "articleId"]);
function readGet(url: string, uid: string | null): Json {
  const key = norm(url);
  const [path, query = ""] = key.split("?");
  const params = new URLSearchParams(query);
  const own = uid ? db[`${uid}::GET ${key}`] : undefined;
  const base = (uid ? db[`${uid}::GET ${path}`] : undefined) ?? db["GET " + path];
  const simple = [...params.keys()].every((k) => SIMPLE.has(k) || k === "take" || k === "limit" || k === "unread" || k === "q");
  if (Array.isArray(base) && query && simple) {
    let rows = base.filter((r: Json) => [...params.entries()].every(([k, v]) => {
      if (k === "take" || k === "limit" || k === "q") return true;
      if (k === "unread") return v !== "true" || !r.readAt;
      if (!(k in r)) return true;
      return String(r[k]) === v;
    }));
    const q = params.get("q"); if (q) rows = rows.filter((r: Json) => JSON.stringify(r).toLowerCase().includes(q.toLowerCase()));
    const n = Number(params.get("take") ?? params.get("limit") ?? 0); if (n) rows = rows.slice(0, n);
    return rows;
  }
  if (own !== undefined) return own;
  if (db["GET " + key] !== undefined) return db["GET " + key];
  if (base !== undefined) return base;
  // detail of something created during the demo: /api/<thing>/<id>
  const m = /^\/api\/([a-z-]+)\/([^/]+)$/.exec(path);
  if (m) { const hit = find(`/api/${m[1]}`, m[2]); if (hit) return hit; }
  if (/^\/api\/items\/(.+)$/.test(path)) {
    const idOrQr = decodeURIComponent(path.slice(11));
    const it = allItems().find((i) => i.id === idOrQr || i.qrCode === idOrQr);
    if (it) return db[`GET /api/items/${it.id}`] ?? { item: it, history: [], installation: null, return: null };
  }
  return undefined;
}

// ---------------------------------------------------------------- the workflows
type Handler = (m: RegExpExecArray, body: Json, uid: string | null, url: URL) => Json;
const routes: [string, RegExp, Handler][] = [];
const on = (method: string, re: RegExp, h: Handler) => routes.push([method, re, h]);

// ---- sign-in (the demo signs in by user ID; there are no passwords in the demo)
on("POST", /^\/api\/auth\/login$/, (_m, b) => {
  const u = users().find((x: Json) => x.id === String(b?.id ?? "").trim().toLowerCase());
  if (!u) fail(401, "Unknown user ID. Pick one of the demo accounts.");
  if (!u.active) fail(401, "This account is inactive. Contact the System Administrator.");
  return { accessToken: "demo-" + u.id, expiresAt: new Date(Date.now() + 8 * 3600e3).toISOString(), user: me(u.id), mustChangePassword: false };
});
on("POST", /^\/api\/auth\/refresh$/, (_m, _b, uid) => {
  const saved = sessionStorage.getItem("slims.demo.user") ?? uid;
  if (!saved) fail(401, "No session.");
  return { accessToken: "demo-" + saved, expiresAt: new Date(Date.now() + 8 * 3600e3).toISOString(), user: me(saved!), mustChangePassword: false };
});
on("POST", /^\/api\/auth\/logout$/, () => { sessionStorage.removeItem("slims.demo.user"); return null; });
on("POST", /^\/api\/auth\/change-password$/, () => null);

// ---- requisitions
function reqNumber(zone: string) { return nextNumber("/api/requisitions", `REQ/${(zone || "CEN").slice(0, 3).toUpperCase()}/${fy()}/`); }
on("POST", /^\/api\/requisitions$/, (_m, b, uid) => {
  const u = me(uid!); need(u, "requisition");
  const site = find("/api/sites", b.siteId) ?? fail(404, "Site not found.");
  const store = find("/api/stores", b.storeId) ?? fail(404, "Store not found.");
  if (site.status === "On hold" || site.status === "Completed") fail(409, `Site ${site.code} is ${site.status}.`, "BR-007");
  if (!b.lines?.length) fail(400, "Add at least one item.");
  const avail = db[`GET /api/requisitions/availability/${store.id}`] ?? {};
  const lines = b.lines.map((l: Json, n: number) => {
    const a = find("/api/articles", l.articleId) ?? fail(404, "Article not found.");
    const av = Number(avail[String(a.id)] ?? 0);
    return { id: Date.now() % 100000 + n, articleId: a.id, itemCode: a.itemCode, description: a.description, uom: a.uom, qtyRequested: +l.qty, qtyApproved: 0, qtyIssued: 0,
      shortfall: Math.max(0, +l.qty - av), available: av, indentRaised: +l.qty > av };
  });
  const value = b.lines.reduce((s: number, l: Json) => s + (+l.qty) * (find("/api/articles", l.articleId)?.rate ?? 0), 0);
  const r = { id: nextId("/api/requisitions"), number: reqNumber(site.zone), siteId: site.id, siteCode: site.code, siteName: site.name, zone: site.zone, storeId: store.id, storeCode: store.code,
    raisedBy: u.name, raisedById: u.id, raisedAt: nowIso(), needBy: b.needBy ?? null, status: b.submit ? "Submitted" : "Draft", purpose: b.purpose ?? null, approvedBy: null, decidedAt: null,
    decisionNote: null, issueSlipNo: null, value, lines, issueIds: [], requiredLevel: value > 1_000_000 ? 2 : 1, currentLevel: 0, recommendedBy: null, submittedAt: b.submit ? nowIso() : null, lapsedAt: null };
  insert("/api/requisitions", r);
  return r;
});
const reqOf = (id: string) => find("/api/requisitions", id) ?? fail(404, "Requisition not found.");
const setReq = (r: Json, p: Json) => patchAll((o) => o.id === r.id && typeof o.number === "string" && o.number === r.number, p);
on("POST", /^\/api\/requisitions\/(\d+)\/submit$/, (m, _b, uid) => { const r = reqOf(m[1]); me(uid!); if (r.status !== "Draft") fail(409, `Requisition is ${r.status}.`); setReq(r, { status: "Submitted", submittedAt: nowIso() }); return reqOf(m[1]); });
on("POST", /^\/api\/requisitions\/(\d+)\/cancel$/, (m, b, uid) => {
  const r = reqOf(m[1]); const u = me(uid!);
  if (!["Draft", "Submitted", "Recommended", "Approved"].includes(r.status)) fail(409, `A ${r.status} requisition cannot be cancelled.`);
  if (r.raisedById !== u.id && !u.permissions.approve) fail(403, "Only the originator or an approver can cancel it.", "scope");
  setReq(r, { status: "Cancelled", decisionNote: b?.reason ?? null, decidedAt: nowIso() }); return reqOf(m[1]);
});
on("POST", /^\/api\/requisitions\/(\d+)\/recommend$/, (m, b, uid) => { const r = reqOf(m[1]); const u = me(uid!); need(u, "approve"); setReq(r, { status: "Recommended", recommendedBy: u.name, currentLevel: 1, decisionNote: b?.note ?? null }); return reqOf(m[1]); });
on("POST", /^\/api\/requisitions\/(\d+)\/decide$/, (m, b, uid) => {
  const r = reqOf(m[1]); const u = me(uid!); need(u, "approve");
  if (r.raisedById === u.id) fail(409, "Nobody may approve a requisition they raised themselves.", "BR-026");
  if (!["Submitted", "Recommended"].includes(r.status)) fail(409, `Requisition is ${r.status}; only a submitted requisition can be decided.`);
  if (!b.approve) { if (!b.note?.trim()) fail(400, "Give the reason for the rejection."); setReq(r, { status: "Rejected", approvedBy: u.name, decidedAt: nowIso(), decisionNote: b.note }); return reqOf(m[1]); }
  const lines = r.lines.map((l: Json) => ({ ...l, qtyApproved: b.lines?.find((x: Json) => x.lineId === l.id)?.qtyApproved ?? l.qtyRequested }));
  setReq(r, { status: "Approved", approvedBy: u.name, decidedAt: nowIso(), decisionNote: b.note ?? null, currentLevel: r.requiredLevel,
    issueSlipNo: nextNumber("/api/requisitions", `ISL/${r.storeCode}/${fy()}/`), lines });
  // reserve stock for the approved lines
  for (const l of lines) {
    let left = l.qtyApproved;
    for (const i of allItems().filter((x) => x.articleId === l.articleId && x.status === "Received" && x.storeId === r.storeId && locKind.get(x.locationId) === "Stock")) {
      if (left <= 0) break; patchItem(i.id, { status: "Reserved", requisitionId: r.id }); left -= i.qty;
    }
  }
  return reqOf(m[1]);
});
on("GET", /^\/api\/requisitions\/availability\/(\d+)$/, (m) => {
  const out: Record<string, number> = {};
  for (const i of allItems()) if (i.storeId === +m[1] && i.status === "Received" && locKind.get(i.locationId) === "Stock") out[i.articleId] = (out[i.articleId] ?? 0) + i.qty;
  return out;
});

// ---- goods receipt by quantity
on("POST", /^\/api\/grn$/, (_m, b, uid) => {
  const u = me(uid!); need(u, "receive");
  const po = find("/api/purchase-orders", b.purchaseOrderId) ?? fail(404, "Purchase order not found.");
  const store = find("/api/stores", b.storeId) ?? fail(404, "Store not found.");
  const loc = store.locations.find((l: Json) => l.id === b.storeLocationId) ?? store.locations.find((l: Json) => l.kind === "Receiving");
  const lines = (b.lines ?? []).filter((l: Json) => l.qty > 0 || l.rejectedQty > 0);
  if (!lines.length) fail(400, "Enter the quantity received for at least one item.", "BR-001");
  const id = nextId("/api/grn"); const number = nextNumber("/api/grn", `GRN/${store.code}/${fy()}/`);
  const got: Json[] = []; let rejected = 0;
  for (const l of lines) {
    if (l.rejectedQty > 0 && !l.rejectReason) fail(400, "Choose the reason for the rejected quantity.");
    const art = find("/api/articles", l.articleId);
    const open = allItems().filter((i) => i.articleId === l.articleId && i.status === "Created").sort((a, c) => a.serial - c.serial);
    const have = open.reduce((s, i) => s + i.qty, 0);
    if (l.qty + (l.rejectedQty ?? 0) > have) fail(409, `${art.itemCode}: only ${have} ${art.uom} is still due on this PO line; ${l.qty + (l.rejectedQty ?? 0)} entered.`, "BR-014");
    let k = 0;
    const take = (q: number, status: string) => { let s = 0; while (s < q && k < open.length) { const i = open[k++]; s += i.qty;
      const p = { status, grnId: id, receivedAt: status === "Received" ? nowIso() : null, storeId: status === "Received" ? store.id : null, storeCode: status === "Received" ? store.code : null,
        locationId: status === "Received" ? loc.id : null, locationCode: status === "Received" ? loc.fullCode : null, updatedAt: nowIso(), updatedBy: u.id };
      patchItem(i.id, p); got.push({ ...i, ...p }); } if (s !== q) fail(400, `${art.itemCode}: must be in whole packs of ${art.packSize} ${art.uom}.`); };
    take(l.qty, "Received"); take(l.rejectedQty ?? 0, "Rejected"); rejected += open.slice(0, k).filter((i) => got.find((g) => g.id === i.id)?.status === "Rejected").length;
    patchAll((o) => o.id === po.id && o.number === po.number, (o: Json) => { for (const pl of o.lines ?? []) if (pl.articleId === l.articleId) pl.received += l.qty; });
    patchAll((o) => o.id === l.articleId && o.itemCode === art.itemCode, (o: Json) => { o.received += l.qty; o.onHand += l.qty; });
    if (l.rejectedQty > 0) insert("/api/grn/discrepancies", { id: nextId("/api/grn/discrepancies"), number: nextNumber("/api/grn/discrepancies", `DSC/${fy()}/`), purchaseOrderId: po.id, poNumber: po.number,
      vendorName: po.vendorName, articleId: art.id, itemCode: art.itemCode, kind: "Rejected", qty: l.rejectedQty, reason: `Rejected at receipt (${number}): ${l.rejectedQty} × ${l.rejectReason}`, status: "Open", raisedAt: nowIso(), resolution: null, resolvedAt: null });
  }
  const acc = got.filter((g) => g.status === "Received");
  const g = { id, number, purchaseOrderId: po.id, poNumber: po.number, vendorName: po.vendorName, storeId: store.id, storeCode: store.code, challanNo: b.challanNo, invoiceNo: b.invoiceNo,
    invoiceDate: b.invoiceDate, postedBy: u.name, postedAt: nowIso(), itemCount: acc.length, qty: acc.reduce((s, i) => s + i.qty, 0), value: acc.reduce((s, i) => s + i.qty * i.rate, 0),
    remarks: b.remarks, reversed: false, condition: b.condition, rejectedCount: rejected };
  insert("/api/grn", g); db[`GET /api/grn/${id}/items`] = got;
  if (loc.kind === "Receiving") for (const k of Object.keys(db)) if (k.startsWith("GET /api/stock-control/receiving")) db[k] = [...acc, ...(db[k] ?? [])];
  return g;
});
on("POST", /^\/api\/grn\/(\d+)\/reverse$/, (m, b, uid) => { need(me(uid!), "approve"); if (!b?.reason) fail(400, "A reason is required."); patchAll((o) => o.id === +m[1] && String(o.number).startsWith("GRN/"), { reversed: true }); return null; });
on("POST", /^\/api\/grn\/discrepancies$/, (_m, b, uid) => {
  me(uid!); const po = find("/api/purchase-orders", b.purchaseOrderId); const art = b.articleId ? find("/api/articles", b.articleId) : null;
  const d = { id: nextId("/api/grn/discrepancies"), number: nextNumber("/api/grn/discrepancies", `DSC/${fy()}/`), purchaseOrderId: po.id, poNumber: po.number, vendorName: po.vendorName,
    articleId: art?.id ?? null, itemCode: art?.itemCode ?? null, kind: b.kind, qty: b.qty, reason: b.reason, status: "Open", raisedAt: nowIso(), resolution: null, resolvedAt: null };
  insert("/api/grn/discrepancies", d); return d;
});
on("POST", /^\/api\/grn\/discrepancies\/(\d+)\/resolve$/, (m, b) => { patchAll((o) => o.id === +m[1] && String(o.number).startsWith("DSC"), { status: "Resolved", resolution: b.resolution, resolvedAt: nowIso() }); return null; });

// ---- issue by quantity
function stockFor(req: Json, articleId: number) {
  const its = allItems().filter((i) => i.articleId === articleId && i.storeId === req.storeId);
  const reserved = its.filter((i) => i.status === "Reserved" && i.requisitionId === req.id && locKind.get(i.locationId) === "Stock");
  const free = its.filter((i) => i.status === "Received" && locKind.get(i.locationId) === "Stock").sort((a, b) => String(a.receivedAt).localeCompare(String(b.receivedAt)) || a.serial - b.serial);
  const rcv = its.filter((i) => i.status === "Received" && locKind.get(i.locationId) === "Receiving");
  return { reserved, free, rcv };
}
on("GET", /^\/api\/issues\/available$/, (_m, _b, _u, url) => {
  const req = reqOf(url.searchParams.get("requisitionId") ?? "");
  return req.lines.map((l: Json) => { const a = find("/api/articles", l.articleId); const s = stockFor(req, l.articleId); const sum = (x: Json[]) => x.reduce((t, i) => t + i.qty, 0);
    return { articleId: l.articleId, itemCode: l.itemCode, uom: l.uom, packSize: a?.packSize ?? 1, reserved: sum(s.reserved), inStock: sum(s.free), inReceiving: sum(s.rcv) }; });
});
on("POST", /^\/api\/issues$/, (_m, b, uid) => {
  const u = me(uid!); need(u, "issue");
  const req = reqOf(b.requisitionId);
  if (!["Approved", "PartIssued"].includes(req.status)) fail(409, `Requisition is ${req.status}; material can only be issued against an approved requisition.`, "BR-006");
  const site = find("/api/sites", req.siteId);
  const picked: Json[] = [];
  for (const l of (b.lines ?? []).filter((x: Json) => x.qty > 0)) {
    const line = req.lines.find((x: Json) => x.articleId === l.articleId); const bal = line.qtyApproved - line.qtyIssued;
    if (l.qty > bal) fail(409, `${line.itemCode}: ${bal} ${line.uom} is still to be issued on this requisition; ${l.qty} entered.`, "BR-010");
    const s = stockFor(req, l.articleId); let sum = 0;
    for (const i of [...s.reserved, ...s.free]) { if (sum >= l.qty) break; picked.push(i); sum += i.qty; }
    if (sum < l.qty) fail(409, `${line.itemCode}: only ${sum} ${line.uom} is in the stock bins of ${req.storeCode}; ${l.qty} entered.`, "BR-008");
    line.qtyIssued += sum;
  }
  if (!picked.length) fail(400, "Enter the quantity to issue for at least one item.", "BR-001");
  const id = nextId("/api/issues");
  const issued = picked.map((i) => { const p = { status: "Issued", siteId: site.id, siteCode: site.code, siteName: site.name, issueId: id, requisitionId: req.id, storeId: null, storeCode: null, locationId: null, locationCode: null, issuedAt: nowIso() }; patchItem(i.id, p); return { ...i, ...p }; });
  const x = { id, number: nextNumber("/api/issues", `ISS/${req.storeCode}/${fy()}/`), gatePassNo: nextNumber("/api/issues", `GP/${req.storeCode}/${fy()}/`).replace(/^ISS/, "GP"), requisitionId: req.id, requisitionNo: req.number,
    storeId: req.storeId, storeCode: req.storeCode, siteId: site.id, siteCode: site.code, siteName: site.name, issuedBy: u.name, issuedAt: nowIso(), issuedTo: b.issuedTo, vehicle: b.vehicle,
    receivedById: null, receivedAt: null, itemCount: issued.length, qty: issued.reduce((s, i) => s + i.qty, 0), value: issued.reduce((s, i) => s + i.qty * i.rate, 0), remarks: b.remarks, items: issued };
  x.gatePassNo = `GP/${req.storeCode}/${fy()}/${String(id).padStart(5, "0")}`;
  insert("/api/issues", x);
  setReq(req, { lines: req.lines, status: req.lines.every((l: Json) => l.qtyIssued >= l.qtyApproved) ? "Issued" : "PartIssued", issueIds: [...req.issueIds, id] });
  return x;
});
function acknowledge(id: number, uid: string) {
  const x = find("/api/issues", id) ?? fail(404, "Issue not found.");
  if (x.receivedAt) fail(409, "Already acknowledged.");
  const full = db[`GET /api/issues/${id}`] ?? x;
  patchAll((o) => o.id === id && String(o.number).startsWith("ISS"), { receivedAt: nowIso(), receivedById: uid });
  for (const i of full.items ?? []) patchItem(i.id, { status: "AtSite" });
  for (const k of Object.keys(db)) if (k.endsWith("GET /api/issues?unacknowledged=true")) db[k] = (db[k] ?? []).filter((o: Json) => o.id !== id);
  return find("/api/issues", id);
}
on("POST", /^\/api\/issues\/(\d+)\/acknowledge$/, (m, _b, uid) => acknowledge(+m[1], uid!));

// ---- installation evidence (web capture is off by policy; the phone app is the only source)
on("POST", /^\/api\/installations$/, () => fail(409, "Installation evidence is accepted only from the SLIMS Field app on a registered phone (live camera, real GPS). Browser upload is switched off under Settings ▸ Parameters.", "FR-FLD-007"));
const instOf = (id: string) => find("/api/installations", id) ?? fail(404, "Installation not found.");
const setInst = (id: number, p: Json) => patchAll((o) => o.id === id && "geoStatus" in o, p);
on("POST", /^\/api\/installations\/(\d+)\/verify$/, (m, b, uid) => {
  const x = instOf(m[1]); const u = me(uid!); need(u, "verify");
  if (x.officerId === u.id) fail(409, "You cannot verify your own installation.", "BR-016");
  if (x.geoStatus !== "PendingVerification") fail(409, `Installation is ${x.geoStatus}.`);
  if (b.approve && (b.justification ?? "").trim().length < 20) fail(400, "Approving a deviation needs a written justification (at least 20 characters).", "BR-017");
  setInst(x.id, { geoStatus: b.approve ? "ApprovedException" : "Rejected", verifiedBy: u.name, verifiedAt: nowIso(), justification: b.justification });
  if (b.approve) patchItem(x.itemId, { status: "Installed" });
  return instOf(m[1]);
});
on("POST", /^\/api\/installations\/(\d+)\/field-verification$/, (m, b, uid) => { const u = me(uid!); setInst(+m[1], { fvRequestedBy: u.name, fvRequestedAt: nowIso(), fvRequestNote: b?.note ?? null }); return instOf(m[1]); });
on("POST", /^\/api\/installations\/(\d+)\/findings$/, (m, b, uid) => { const u = me(uid!); setInst(+m[1], { fvFindings: b.findings, fvFindingsBy: u.name, fvFindingsAt: nowIso() }); return instOf(m[1]); });
on("POST", /^\/api\/installations\/(\d+)\/([a-z-]+)$/, (m, b, uid) => {
  const u = me(uid!); const next: Record<string, string> = { reject: "Rejected", recapture: "MissingEvidence", approve: "ApprovedException" };
  setInst(+m[1], { geoStatus: next[m[2]] ?? instOf(m[1]).geoStatus, verifiedBy: u.name, verifiedAt: nowIso(), justification: b?.justification ?? b?.note ?? null }); return instOf(m[1]);
});

// ---- returns
function newReturn(item: Json, b: Json, u: Json) {
  const r = { id: nextId("/api/returns"), number: nextNumber("/api/returns", `RTN/${fy()}/`), itemId: item.id, qrCode: item.qrCode, itemCode: item.itemCode, description: item.description,
    poNumber: item.poNumber, vendorName: item.vendorName, siteId: item.siteId, siteCode: item.siteCode, siteName: item.siteName, storeId: null, storeCode: null, locationCode: null,
    reason: b.reason, kind: b.kind ?? "OutOfOrder", reportedBy: u.name, reportedAt: nowIso(), receivedBy: null, receivedAt: null, classification: "Pending", warrantyEligible: !!item.warrantyExpires && item.warrantyExpires > nowIso(),
    warrantyExpires: item.warrantyExpires, claimRef: null, classifiedAt: null, disposalApprovedAt: null, scrapValue: null, closedAt: null, remarks: b.remarks ?? null, installedAt: item.installedAt, serviceDays: item.installedAt ? Math.round((Date.now() - Date.parse(item.installedAt)) / 864e5) : null, itemStatus: "OutOfOrder" };
  insert("/api/returns", r); patchItem(item.id, { status: "OutOfOrder", returnNoteId: r.id }); return r;
}
on("POST", /^\/api\/returns\/report$/, (_m, b, uid) => { const u = me(uid!); const it = itemByQr(b.qr) ?? fail(404, "Not a SLIMS label."); if (!["Installed", "AtSite", "Issued"].includes(it.status)) fail(409, `Item is ${it.status}; only material at site can be reported.`); return newReturn(it, b, u); });
on("POST", /^\/api\/returns\/receive$/, (_m, b, uid) => {
  const u = me(uid!); const it = itemByQr(b.qr) ?? fail(404, "Not a SLIMS label.");
  let r = list("/api/returns").find((x: Json) => x.itemId === it.id && !x.receivedAt) ?? newReturn(it, { reason: b.remarks ?? "Returned to store" }, u);
  const store = find("/api/stores", b.storeId); const q = store.locations.find((l: Json) => l.kind === "Quarantine");
  patchAll((o) => o.id === r.id && String(o.number).startsWith("RTN"), { receivedBy: u.name, receivedAt: nowIso(), storeId: store.id, storeCode: store.code, locationCode: q?.fullCode, itemStatus: "Returned" });
  patchItem(it.id, { status: "Returned", storeId: store.id, storeCode: store.code, locationId: q?.id, locationCode: q?.fullCode, siteId: null, siteCode: null });
  r = find("/api/returns", r.id); return r;
});
on("POST", /^\/api\/returns\/(\d+)\/([a-z-]+)$/, (m, b, uid) => {
  const u = me(uid!); const r = find("/api/returns", m[1]) ?? fail(404, "Return not found.");
  const map: Record<string, [string, string]> = { Warranty: ["WarrantyClaim", "Warranty"], Repair: ["UnderRepair", "Repair"], Condemn: ["Scrap", "Condemn"], Condemned: ["Scrap", "Condemn"], Usable: ["Received", "Usable"] };
  let p: Json;
  if (m[2] === "classify" || m[2] === "usable") { const c = m[2] === "usable" ? "Usable" : b.classification; p = { classification: map[c]?.[1] ?? c, itemStatus: map[c]?.[0] ?? r.itemStatus, claimRef: b.claimRef ?? null, classifiedAt: nowIso(), remarks: b.remarks ?? r.remarks }; }
  else if (m[2] === "dispose") { if (r.itemStatus !== "Scrap") fail(409, "An item can only be disposed after it is condemned.", "BR-020"); p = { itemStatus: "Disposed", scrapValue: b.scrapValue ?? 0, disposalApprovedAt: nowIso(), closedAt: nowIso() }; }
  else if (m[2] === "reinward") p = { itemStatus: "Received", closedAt: nowIso(), remarks: b.remarks ?? r.remarks };
  else p = { remarks: b?.remarks ?? r.remarks };
  patchAll((o) => o.id === r.id && String(o.number).startsWith("RTN"), p); patchItem(r.itemId, { status: p.itemStatus ?? r.itemStatus });
  void u; return find("/api/returns", r.id);
});

// ---- stock controls
on("GET", /^\/api\/stock-control\/lookup$/, (_m, _b, _u, url) => {
  const qr = url.searchParams.get("qr") ?? ""; const it = itemByQr(qr);
  if (it) return { kind: "item", item: it, location: null, message: `${it.itemCode} #${it.serial} — ${it.status}${it.locationCode ? " at " + it.locationCode : ""}` };
  for (const k of Object.keys(db)) if (/^GET \/api\/stores\/\d+\/labels$/.test(k)) { const bl = db[k].find((l: Json) => l.payload === qr.trim()); if (bl) { const loc = locById.get(bl.id); return { kind: "location", item: null, location: loc, message: `Bin ${bl.fullCode} (${bl.kind})` }; } }
  return { kind: "unknown", item: null, location: null, message: "Not a SLIMS label." };
});
on("POST", /^\/api\/stock-control\/move$/, (_m, b, uid) => {
  me(uid!); let to = b.toLocationId ? locById.get(b.toLocationId) : null;
  if (!to && b.toLocationQr) for (const k of Object.keys(db)) if (/^GET \/api\/stores\/\d+\/labels$/.test(k)) { const bl = db[k].find((l: Json) => l.payload === b.toLocationQr); if (bl) to = locById.get(bl.id); }
  if (!to) fail(400, "Scan the destination bin.");
  if (to.kind === "Receiving") fail(409, "Material cannot be moved back into the Receiving area.", "BR-005");
  const moved = (b.qrCodes ?? []).map((q: string) => itemByQr(q) ?? fail(404, `${q}: not a SLIMS label.`)).map((i: Json) => { const p = { locationId: to.id, locationCode: to.fullCode }; patchItem(i.id, p); return { ...i, ...p }; });
  for (const k of Object.keys(db)) if (k.startsWith("GET /api/stock-control/receiving")) db[k] = (db[k] ?? []).filter((i: Json) => !moved.some((x: Json) => x.id === i.id));
  return { moved: moved.length, toLocation: to.fullCode, items: moved };
});
on("POST", /^\/api\/transfers$/, (_m, b, uid) => {
  const u = me(uid!); const from = find("/api/stores", b.fromStoreId), to = find("/api/stores", b.toStoreId);
  const its = (b.qrCodes ?? []).map((q: string) => itemByQr(q) ?? fail(404, `${q}: not a SLIMS label.`));
  if (!its.length) fail(400, "Scan at least one item.");
  for (const i of its) { if (i.status !== "Received" || i.storeId !== from.id) fail(409, `${i.itemCode} #${i.serial} is ${i.status} at ${i.storeCode ?? "—"}; only free stock of ${from.code} can be despatched.`); patchItem(i.id, { status: "InTransit", locationId: null, locationCode: null }); }
  const t = { id: nextId("/api/transfers"), number: nextNumber("/api/transfers", `STN/${from.code}/${fy()}/`), fromStoreId: from.id, fromStoreCode: from.code, toStoreId: to.id, toStoreCode: to.code, status: "InTransit",
    despatchedBy: u.name, despatchedAt: nowIso(), vehicle: b.vehicle, remarks: b.remarks, itemCount: its.length, qty: its.reduce((s: number, i: Json) => s + i.qty, 0), inTransit: its.length, receivedBy: null, receivedAt: null, items: its.map((i: Json) => ({ ...i, status: "InTransit" })) };
  insert("/api/transfers", t); return t;
});
on("POST", /^\/api\/transfers\/(\d+)\/receive$/, (m, _b, uid) => {
  const u = me(uid!); const t = db[`GET /api/transfers/${m[1]}`] ?? find("/api/transfers", m[1]);
  const to = find("/api/stores", t.toStoreId); const rcv = to.locations.find((l: Json) => l.kind === "Receiving");
  for (const i of t.items ?? []) patchItem(i.id, { status: "Received", storeId: to.id, storeCode: to.code, locationId: rcv?.id, locationCode: rcv?.fullCode });
  patchAll((o) => o.id === t.id && String(o.number).startsWith("STN"), { status: "Received", receivedBy: u.name, receivedAt: nowIso(), inTransit: 0 });
  return find("/api/transfers", t.id);
});
on("POST", /^\/api\/adjustments$/, (_m, b, uid) => {
  const u = me(uid!); const it = itemByQr(b.qr) ?? fail(404, "Not a SLIMS label.");
  const a = { id: nextId("/api/adjustments"), number: nextNumber("/api/adjustments", `ADJ/${it.storeCode ?? "CEN01"}/${fy()}/`), storeId: it.storeId, storeCode: it.storeCode, itemId: it.id, qrCode: it.qrCode, itemCode: it.itemCode,
    description: it.description, serial: it.serial, kind: b.kind, reasonCode: b.reasonCode, remarks: b.remarks, qtyBefore: it.qty, qtyAfter: b.kind === "Loss" ? 0 : (b.qtyAfter ?? it.qty), status: "Pending",
    physicalCountId: null, raisedBy: u.name, raisedById: u.id, raisedAt: nowIso(), decidedBy: null, decidedAt: null, decisionNote: null, itemStatus: it.status };
  insert("/api/adjustments", a); return a;
});
on("POST", /^\/api\/adjustments\/(\d+)\/decide$/, (m, b, uid) => {
  const u = me(uid!); const a = find("/api/adjustments", m[1]) ?? fail(404, "Adjustment not found.");
  if (a.raisedById === u.id) fail(409, "A stock adjustment needs a second person: you raised this one.", "FR-INV-013");
  if (a.status !== "Pending") fail(409, `Adjustment is ${a.status}.`);
  patchAll((o) => o.id === a.id && String(o.number).startsWith("ADJ"), { status: b.approve ? "Approved" : "Rejected", decidedBy: u.name, decidedAt: nowIso(), decisionNote: b.note ?? null, itemStatus: b.approve && a.kind === "Loss" ? "WrittenOff" : a.itemStatus });
  if (b.approve && a.kind === "Loss") patchItem(a.itemId, { status: "WrittenOff", locationId: null, locationCode: null });
  return find("/api/adjustments", a.id);
});
on("POST", /^\/api\/counts$/, (_m, b, uid) => {
  const u = me(uid!); const s = find("/api/stores", b.storeId); const loc = b.locationId ? locById.get(b.locationId) : null;
  const book = allItems().filter((i) => i.storeId === s.id && ["Received", "Reserved"].includes(i.status) && (!loc || i.locationId === loc.id)).length;
  const c = { id: nextId("/api/counts"), number: nextNumber("/api/counts", `PV/${s.code}/${fy()}/`), storeId: s.id, storeCode: s.code, locationId: loc?.id ?? null, locationCode: loc?.fullCode ?? null, status: "Open",
    startedBy: u.name, startedAt: nowIso(), submittedAt: null, closedAt: null, bookItems: book, scanned: 0, scans: [], variances: [] };
  insert("/api/counts", c); db[`GET /api/counts/${c.id}`] = clone(c); return c;
});
on("POST", /^\/api\/counts\/(\d+)\/scan$/, (m, b) => {
  const c = db[`GET /api/counts/${m[1]}`] ?? find("/api/counts", m[1]); const it = itemByQr(b.qr);
  const s = { qr: b.qr, itemId: it?.id ?? null, itemCode: it?.itemCode ?? null, serial: it?.serial ?? null, qty: b.qty ?? it?.qty ?? 1, locationId: it?.locationId ?? null, locationCode: it?.locationCode ?? null, at: nowIso(),
    result: !it ? "Unknown label" : c.scans.some((x: Json) => x.qr === b.qr) ? "Already counted" : it.storeId !== c.storeId ? "Belongs to another store" : "Counted" };
  if (s.result === "Counted") { c.scans.unshift(s); c.scanned++; patchAll((o) => o.id === c.id && String(o.number).startsWith("PV/"), { scanned: c.scanned }); }
  return s;
});
on("POST", /^\/api\/counts\/(\d+)\/([a-z-]+)$/, (m) => {
  const c = db[`GET /api/counts/${m[1]}`] ?? find("/api/counts", m[1]);
  const next: Record<string, Json> = { submit: { status: "Submitted", submittedAt: nowIso() }, close: { status: "Closed", closedAt: nowIso() }, "raise-adjustments": { status: "Adjusting" } };
  if (m[2] === "submit") {
    const counted = new Set(c.scans.map((s: Json) => s.itemId));
    c.variances = allItems().filter((i) => i.storeId === c.storeId && ["Received", "Reserved"].includes(i.status) && (!c.locationId || i.locationId === c.locationId) && !counted.has(i.id)).slice(0, 40)
      .map((i) => ({ kind: "Missing", itemId: i.id, qr: i.qrCode, itemCode: i.itemCode, serial: i.serial, bookLocation: i.locationCode, countedLocation: null, bookQty: i.qty, countedQty: 0, adjustmentId: null, adjustmentStatus: null, clearance: "Open" }));
  }
  Object.assign(c, next[m[2]] ?? {}); patchAll((o) => o.id === c.id && String(o.number).startsWith("PV/"), next[m[2]] ?? {});
  return c;
});
on("POST", /^\/api\/amendments$/, (_m, b, uid) => {
  const u = me(uid!);
  const a = { id: nextId("/api/amendments"), number: nextNumber("/api/amendments", `AMD/${fy()}/`), entityType: b.entityType, entityRef: b.entityRef, field: b.field, oldValue: null, newValue: b.newValue, reason: b.reason,
    status: "Pending", raisedBy: u.name, raisedById: u.id, raisedAt: nowIso(), decidedBy: null, decidedAt: null, decisionNote: null };
  insert("/api/amendments", a); return a;
});
on("POST", /^\/api\/amendments\/(\d+)\/decide$/, (m, b, uid) => {
  const u = me(uid!); const a = find("/api/amendments", m[1]); if (a.raisedById === u.id) fail(409, "Someone else must decide an amendment you requested.");
  patchAll((o) => o.id === a.id && String(o.number).startsWith("AMD"), { status: b.approve ? "Approved" : "Rejected", decidedBy: u.name, decidedAt: nowIso(), decisionNote: b.note ?? null }); return find("/api/amendments", a.id);
});

// ---- master data (created and edited in memory)
function upsert(path: string, id: string | null, b: Json, extra: Json = {}) {
  if (id) { patchAll((o) => String(o.id) === id && list(path).some((x: Json) => x === o || String(x.id) === id) && ("code" in o || "number" in o || "name" in o), { ...b, ...extra }); return find(path, id); }
  const o = { id: nextId(path), active: true, ...b, ...extra }; insert(path, o, false); return o;
}
on("POST", /^\/api\/vendors$/, (_m, b) => upsert("/api/vendors", null, b, { purchaseOrders: 0 }));
on("PUT", /^\/api\/vendors\/(\d+)$/, (m, b) => upsert("/api/vendors", m[1], b));
on("POST", /^\/api\/stores$/, (_m, b) => upsert("/api/stores", null, b, { items: 0, value: 0, locations: [], inChargeName: users().find((u: Json) => u.id === b.inChargeUserId)?.name ?? null }));
on("PUT", /^\/api\/stores\/(\d+)$/, (m, b) => upsert("/api/stores", m[1], b));
on("POST", /^\/api\/stores\/(\d+)\/locations$/, (m, b) => {
  const s = find("/api/stores", m[1]); const l = { id: 9000 + locById.size, storeId: s.id, storeCode: s.code, section: b.section, rack: b.rack, bin: b.bin, fullCode: `${s.code}-${b.section}-${b.rack}-${b.bin}`, kind: b.kind ?? "Stock", active: true, items: 0 };
  locById.set(l.id, l); locKind.set(l.id, l.kind); patchAll((o) => o.id === s.id && o.code === s.code && Array.isArray(o.locations), (o: Json) => o.locations.push(clone(l))); return l;
});
on("POST", /^\/api\/sites$/, (_m, b) => upsert("/api/sites", null, { ...b, code: b.code || `SITE-${(b.zone ?? "C")[0]}-${String(b.name ?? "NEW").replace(/[^A-Z]/gi, "").slice(0, 4).toUpperCase()}-${String(nextId("/api/sites")).padStart(4, "0")}` },
  { geofenceM: b.geofenceM ?? 60, installed: 0, atSite: 0, pending: 0, createdAt: nowIso(), supervisorName: users().find((u: Json) => u.id === b.supervisorUserId)?.name ?? null, officerName: users().find((u: Json) => u.id === b.officerUserId)?.name ?? null }));
on("PUT", /^\/api\/sites\/(\d+)$/, (m, b) => upsert("/api/sites", m[1], b));
on("POST", /^\/api\/sites\/bulk$/, () => ({ created: 0, updated: 0, errors: ["Bulk upload needs the live server; the demo keeps its sample sites."] }));
on("POST", /^\/api\/purchase-orders$/, (_m, b) => {
  const v = find("/api/vendors", b.vendorId);
  return upsert("/api/purchase-orders", null, { ...b, lines: b.lines.map((l: Json, n: number) => ({ ...l, id: 50000 + n + Date.now() % 10000, lineNo: n + 1, articleId: null, itemCode: null, received: 0, issued: 0 })) },
    { vendorName: v?.name, status: "Open", value: b.lines.reduce((s: number, l: Json) => s + l.qty * l.rate, 0) });
});
on("PUT", /^\/api\/purchase-orders\/(\d+)$/, (m, b) => upsert("/api/purchase-orders", m[1], { ...b, lines: undefined }));
on("POST", /^\/api\/purchase-orders\/(\d+)\/toggle$/, (m) => { const p = find("/api/purchase-orders", m[1]); patchAll((o) => o.id === p.id && o.number === p.number, { status: p.status === "Open" ? "Closed" : "Open" }); return find("/api/purchase-orders", m[1]); });
on("POST", /^\/api\/articles$/, (_m, b) => {
  for (const p of list("/api/purchase-orders")) { const l = p.lines.find((x: Json) => x.id === b.purchaseOrderLineId); if (!l) continue;
    const code = `${l.category}-${l.subCategory}-${l.spec}-${p.number.replace("-", "")}-${String(l.lineNo).padStart(3, "0")}`;
    const a = upsert("/api/articles", null, { itemCode: code, category: l.category, subCategory: l.subCategory, spec: l.spec, description: l.description, uom: l.uom, serialised: b.serialised, packSize: b.packSize,
      purchaseOrderId: p.id, poNumber: p.number, vendorId: p.vendorId, vendorName: p.vendorName, rate: l.rate, warrantyMonths: p.warrantyMonths, expectedLifeMonths: b.expectedLifeMonths, reorderLevel: b.reorderLevel,
      status: "Active", itemsGenerated: Math.ceil(l.qty / (b.serialised ? 1 : b.packSize)), poQty: l.qty, received: 0, onHand: 0, reserved: 0, installed: 0, quarantine: 0, createdAt: nowIso() });
    patchAll((o) => o.id === l.id && o.lineNo === l.lineNo, { articleId: a.id, itemCode: code });
    db[`GET /api/articles/${a.id}/items`] = Array.from({ length: a.itemsGenerated }, (_, n) => ({ id: crypto.randomUUID(), qrCode: `SLIMS|${code}|${String(n + 1).padStart(6, "0")}|DEMO${n}`, articleId: a.id, itemCode: code,
      description: l.description, category: l.category, serial: n + 1, qty: b.serialised ? 1 : b.packSize, uom: l.uom, status: "Created", poNumber: p.number, vendorName: p.vendorName, rate: l.rate, warrantyMonths: p.warrantyMonths, labelPrints: 0, updatedAt: nowIso(), updatedBy: "demo" }));
    return a; }
  return fail(404, "PO line not found.");
});
on("POST", /^\/api\/articles\/(\d+)\/labels$/, (m) => (db[`GET /api/articles/${m[1]}/items`] ?? []).map((i: Json) => { i.labelPrints++; return i; }));
on("PUT", /^\/api\/users$/, (_m, b) => { const role = (db["GET /api/users/roles"] ?? []).find((r: Json) => r.key === b.roleKey);
  const ex = users().find((u: Json) => u.id === b.id);
  if (ex) { patchAll((o) => o.id === b.id && "roleKey" in o, { ...b, roleLabel: role?.label ?? ex.roleLabel }); return ex; }
  const u = { ...b, roleLabel: role?.label ?? b.roleKey, storeIds: b.storeIds ?? [], siteIds: b.siteIds ?? [], hasPassword: false, mustChangePassword: true, lastLogin: null, lockedUntil: null,
    permissions: Object.fromEntries((role?.perms ?? []).map((p: string) => [p, true])), scopeDescription: b.zone ? `${b.zone} zone` : "All zones" };
  insert("/api/users", u, false); insert("/api/auth/picker", { id: u.id, name: u.name, roleKey: u.roleKey, roleLabel: u.roleLabel, designation: u.designation, hasPassword: false, active: u.active }, false); return u; });
on("POST", /^\/api\/users\/([^/]+)\/(password|unlock)$/, () => null);
on("PUT", /^\/api\/config$/, (_m, b) => { db["GET /api/config"] = b; return b; });
on("PUT", /^\/api\/notify-settings$/, (_m, b) => { db["GET /api/notify-settings"] = b; return b; });
on("POST", /^\/api\/notify-settings\/test$/, () => ({ ok: false, message: "The demo does not send e-mail or SMS. On the live server this sends a real test message." }));
on("POST", /^\/api\/report-schedules$/, (_m, b, uid) => upsert("/api/report-schedules", null, { ...b, recipientUserIds: b.recipientUserIds ?? [], recipientEmails: b.recipientEmails ?? "" }, { createdById: uid, createdAt: nowIso(), lastRunAt: null, nextRunAt: null, runs: [] }));
on("PUT", /^\/api\/report-schedules\/(\d+)$/, (m, b) => upsert("/api/report-schedules", m[1], b));
on("POST", /^\/api\/report-schedules\/(\d+)\/run$/, (m) => {
  const s = find("/api/report-schedules", m[1]); const rep = db[`GET /api/reports/${s.reportKey}`];
  const run = { id: Date.now() % 100000, scheduleId: s.id, reportKey: s.reportKey, title: s.title, ranAt: nowIso(), from: null, to: null, rows: rep?.rows?.length ?? 0, status: "Done", error: null, downloadUrl: `/api/report-runs/demo-${s.reportKey}/download` };
  patchAll((o) => o.id === s.id && o.reportKey === s.reportKey && Array.isArray(o.runs), (o: Json) => { o.runs.unshift(clone(run)); o.lastRunAt = run.ranAt; }); return run;
});
on("POST", /^\/api\/automation\/run$/, () => ({ ok: true }));

// ---- notifications
on("POST", /^\/api\/notifications\/read$/, (_m, b, uid) => {
  const ids: number[] | null = b?.ids ?? null; const at = nowIso();
  for (const k of Object.keys(db)) if (k.startsWith(`${uid}::GET /api/notifications`) && Array.isArray(db[k])) for (const n of db[k]) if (!ids || ids.includes(n.id)) n.readAt ??= at;
  const left = (db[`${uid}::GET /api/notifications?take=200`] ?? []).filter((n: Json) => !n.readAt).length;
  db[`${uid}::GET /api/notifications/unread`] = { count: left };
  for (const k of Object.keys(db)) if (k.startsWith(`${uid}::GET /api/notifications?take=200&unread=true`)) db[k] = [];
  return null;
});
on("PUT", /^\/api\/notifications\/prefs$/, (_m, b, uid) => { const k = `${uid}::GET /api/notifications/prefs`; db[k] = { ...(db[k] ?? {}), ...b }; return db[k]; });

// ---- phones: set-up codes, devices and the SLIMS Field app's own calls
const enrolments = new Map<string, string>([["DEMO2026", "off-central"]]);
// until the portal shows a code, the demo camera shows this one: it links the phone to Lighting Officer, Central Zone
(window as Json).__demoCameraQr ||= JSON.stringify({ t: "slims-setup", s: PHONE_SERVER, c: "DEMO2026", u: "off-central" });
on("POST", /^\/api\/mobile\/enrolment$/, (_m, b, uid) => {
  const target = b?.forUserId || uid!; const u = me(target);
  const code = Array.from({ length: 8 }, () => "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"[Math.floor(Math.random() * 32)]).join("");
  enrolments.set(code, target);
  const server = (db["GET /api/config"]?.field?.phoneServerUrl || b?.serverUrl || PHONE_SERVER).replace(/\/+$/, "");
  (window as Json).__demoCameraQr = JSON.stringify({ t: "slims-setup", s: server, c: code, u: target });
  return { code, userId: target, userName: u.name, expiresAt: new Date(Date.now() + 15 * 60e3).toISOString(), qrPayload: (window as Json).__demoCameraQr, serverUrl: server };
});
on("POST", /^\/api\/mobile\/devices\/([^/]+)\/(approve|revoke)$/, (m) => { patchAll((o) => o.id === m[1] && "deviceUid" in o, m[2] === "approve" ? { status: "Active", activatedAt: nowIso() } : { status: "Revoked", revokedAt: nowIso() }); return null; });
function deviceOf(uid: string): Json {
  const mine = db[`${uid}::GET /api/mobile/mine`];
  return mine?.active ?? mine?.devices?.[0] ?? { id: "demo-phone-" + uid, userId: uid, userName: me(uid).name, roleLabel: me(uid).roleLabel, deviceUid: "demo", model: "Demo phone", manufacturer: "SLIMS", osVersion: "Android 14", appVersion: "1.0.0", status: "Setup", enrolledAt: nowIso() };
}
on("POST", /^\/api\/mobile\/enrol$/, (_m, b) => {
  const uid = enrolments.get(String(b.code).toUpperCase()) ?? "off-central";
  if (String(b.code).toUpperCase() !== "DEMO2026") enrolments.delete(String(b.code).toUpperCase());
  const u = me(uid); const d = deviceOf(uid);
  return { deviceId: d.id, deviceToken: "phone-" + uid, userId: uid, userName: u.name, roleLabel: u.roleLabel, status: d.status === "Active" ? "Active" : "Setup", replacesAnotherPhone: false };
});
on("POST", /^\/api\/mobile\/session$/, (_m, b) => {
  const uid = String(b.deviceToken ?? "").replace(/^phone-/, ""); const u = me(uid); const cfg = db["GET /api/config"];
  return { accessToken: "dev-" + uid, expiresAt: new Date(Date.now() + 3600e3).toISOString(), user: u, device: deviceOf(uid), geo: cfg.geo, field: cfg.field, serverTime: nowIso() };
});
on("POST", /^\/api\/mobile\/setup-check$/, (_m, b, uid) => {
  const p = b.photo; const cfg = db["GET /api/config"];
  const checks = [
    { check: "Live photograph", ok: !!p?.base64, detail: "Taken with the live camera" },
    { check: "GPS accuracy", ok: p.accuracyM <= cfg.field.setupMaxAccuracyM, detail: `±${Math.round(p.accuracyM)} m (limit ±${cfg.field.setupMaxAccuracyM} m)` },
    { check: "No mock location", ok: !p.isMock, detail: p.isMock ? "A location-faking app is active" : "Android reports a real fix" },
    { check: "Automatic time", ok: !!b.autoTimeEnabled, detail: b.autoTimeEnabled ? "Network time is on" : "Turn on automatic date and time" },
  ];
  const good = checks.every((c) => c.ok);
  const id = crypto.randomUUID(); files[`/api/files/${id}`] = { type: "image/jpeg", b64: p.base64 };
  const d = { ...deviceOf(uid!), status: good ? "Active" : "Setup", setupCompletedAt: good ? nowIso() : null, activatedAt: good ? nowIso() : null, lastSeenAt: nowIso(), setupLat: p.lat, setupLng: p.lng, setupAccuracyM: p.accuracyM, setupPhotoUrl: `/api/files/${id}` };
  const k = `${uid}::GET /api/mobile/mine`; db[k] = { ...(db[k] ?? { required: true, devices: [] }), active: good ? d : null, devices: [d, ...((db[k]?.devices ?? []).filter((x: Json) => x.id !== d.id))] };
  if (!(db["GET /api/mobile/devices"] ?? []).some((x: Json) => x.id === d.id)) insert("/api/mobile/devices", d); else patchAll((o) => o.id === d.id && "deviceUid" in o, d);
  return { ok: good, checks, device: d };
});
on("GET", /^\/api\/mobile\/bootstrap$/, (_m, _b, uid) => {
  const u = me(uid!); const cfg = db["GET /api/config"];
  const sites = (db[`${uid}::GET /api/sites`] ?? db["GET /api/sites"]).filter((s: Json) => s.status !== "Completed" && (!s.officerUserId || s.officerUserId === uid));
  const ids = new Set(sites.map((s: Json) => s.id));
  const work = allItems().filter((i) => ["Issued", "AtSite"].includes(i.status) && ids.has(i.siteId));
  const ack = (db["GET /api/issues"] ?? []).filter((i: Json) => !i.receivedAt && ids.has(i.siteId));
  const inst = db["GET /api/installations"] ?? [];
  // point the demo camera at the next item's label and put the phone's GPS at its site
  const next = work.find((i) => i.status === "AtSite" && i.itemCode.startsWith("LUM")) ?? work.find((i) => i.itemCode.startsWith("LUM")) ?? work[0];
  if (next) { const st = sites.find((s: Json) => s.id === next.siteId); (window as Json).__demoCameraQr = next.qrCode; if (st) Object.assign((window as Json).__demoGps, { lat: st.lat, lng: st.lng }); }
  return { user: u, device: deviceOf(uid!), geo: cfg.geo, field: cfg.field, sites, workList: work, toAcknowledge: ack,
    recapture: inst.filter((x: Json) => x.officerId === uid && x.geoStatus === "MissingEvidence"), verificationTasks: inst.filter((x: Json) => x.fvRequestedAt && !x.fvFindingsAt && ids.has(x.siteId)),
    failureReasons: cfg.failureReasons, unread: db[`${uid}::GET /api/notifications/unread`]?.count ?? 0, serverTime: nowIso() };
});
on("POST", /^\/api\/mobile\/acknowledge$/, (_m, b, uid) => acknowledge(b.issueId, uid!));
on("POST", /^\/api\/mobile\/returns$/, (_m, b, uid) => { const it = itemByQr(b.qr) ?? fail(404, "Not a SLIMS item label."); return newReturn(it, b, me(uid!)); });
on("POST", /^\/api\/mobile\/findings\/(\d+)$/, (m, b, uid) => { const u = me(uid!); setInst(+m[1], { fvFindings: b.findings, fvFindingsBy: u.name, fvFindingsAt: nowIso() }); return instOf(m[1]); });
function metres(aLat: number, aLng: number, bLat: number, bLng: number) {
  const R = 6371000, r = Math.PI / 180, dLat = (bLat - aLat) * r, dLng = (bLng - aLng) * r;
  return 2 * R * Math.asin(Math.sqrt(Math.sin(dLat / 2) ** 2 + Math.cos(aLat * r) * Math.cos(bLat * r) * Math.sin(dLng / 2) ** 2));
}
on("POST", /^\/api\/mobile\/installations$/, (_m, b, uid) => {
  const u = me(uid!); const done = list("/api/installations").find((x: Json) => x.clientTxnId === b.clientTxnId); if (done) return done;
  const it = itemByQr(b.qr) ?? fail(404, "Not a SLIMS label.", "BR-002");
  if (!["Issued", "AtSite"].includes(it.status)) fail(409, `Item is ${it.status}; only material issued to a site can be installed.`, "BR-011");
  if (b.before.isMock || b.after.isMock) fail(409, "A mock-location app supplied the position; the evidence is refused.", "FR-FLD-010");
  const site = find("/api/sites", it.siteId); const cfg = db["GET /api/config"];
  const d = metres(site.lat, site.lng, b.after.lat, b.after.lng); const limit = site.geofenceM + cfg.geo.toleranceM;
  const ev = (p: Json, kind: string) => { const id = crypto.randomUUID(); files[`/api/files/${id}`] = { type: "image/jpeg", b64: p.base64 };
    return { id, kind, name: `${kind}.jpg`, contentType: "image/jpeg", size: Math.round(p.base64.length * 0.75), lat: p.lat, lng: p.lng, accuracyM: p.accuracyM, capturedAt: p.capturedAt, uploadedBy: u.name, uploadedAt: nowIso(),
      url: `/api/files/${id}`, sha256: p.sha256, stampedUrl: null, deviceId: deviceOf(uid!).id }; };
  const x = { id: nextId("/api/installations"), number: nextNumber("/api/installations", `INST/${site.code}/`), itemId: it.id, qrCode: it.qrCode, itemCode: it.itemCode, description: it.description, siteId: site.id, siteCode: site.code,
    siteName: site.name, zone: site.zone, siteLat: site.lat, siteLng: site.lng, geofenceM: site.geofenceM, officer: u.name, officerId: u.id, poleId: b.poleId, lat: b.after.lat, lng: b.after.lng, accuracyM: b.after.accuracyM,
    distanceM: Math.round(d * 10) / 10, geoStatus: d <= limit ? "Confirmed" : "PendingVerification", remarks: b.remarks, capturedAt: b.after.capturedAt, submittedAt: nowIso(), offline: !!b.offline, verifiedBy: null, verifiedAt: null,
    justification: null, replacesItemId: null, before: ev(b.before, "before"), after: ev(b.after, "after"), clientTxnId: b.clientTxnId, deviceId: deviceOf(uid!).id, queuedAt: b.queuedAt, ageDays: 0, escalationLevel: 0, escalatedAt: null,
    fvRequestedBy: null, fvRequestedAt: null, fvRequestNote: null, fvAssignedToId: null, fvFindings: null, fvFindingsBy: null, fvFindingsAt: null, fvPhotoUrls: [] };
  insert("/api/installations", x);
  patchItem(it.id, { status: x.geoStatus === "Confirmed" ? "Installed" : "PendingVerification", installationId: x.id, installedAt: nowIso() });
  return x;
});

// ---------------------------------------------------------------- the fetch interceptor
function json(status: number, body: Json) { return new Response(body === null || body === undefined ? "" : JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } }); }
function b64Blob(f: { type: string; b64?: string; blob?: Blob }) {
  if (f.blob) return f.blob;
  const bin = atob(f.b64!); const a = new Uint8Array(bin.length); for (let i = 0; i < bin.length; i++) a[i] = bin.charCodeAt(i);
  return (f.blob = new Blob([a], { type: f.type }));
}

export async function handle(method: string, rawUrl: string, headers: Headers, bodyText: string | null): Promise<Response> {
  const u = new URL(rawUrl, "http://demo");
  const path = decodeURIComponent(u.pathname);
  const uid = callerOf(headers) ?? sessionStorage.getItem("slims.demo.user");
  await new Promise((r) => setTimeout(r, 40 + Math.random() * 120));
  try {
    if (method === "GET" && (path.startsWith("/api/files/") || path.startsWith("/api/report-runs/"))) {
      const f = files[norm(path + u.search)] ?? files[path];
      if (f) return new Response(b64Blob(f), { status: 200, headers: { "Content-Type": f.type } });
      if (path.startsWith("/api/report-runs/")) { const key = /demo-([a-z-]+)/.exec(path)?.[1] ?? "installations"; const r = db[`GET /api/reports/${key}`];
        const csv = r ? [r.columns.join(","), ...r.rows.map((row: Json[]) => row.map((v) => `"${String(v ?? "").replace(/"/g, '""')}"`).join(","))].join("\r\n") : "";
        return new Response(new Blob(["﻿" + csv], { type: "text/csv" }), { status: 200, headers: { "Content-Type": "text/csv" } }); }
      return json(404, { error: "File not in the demo snapshot." });
    }
    if (path === "/api/mobile/app.apk") return json(404, { error: "The APK is served by the live server (Settings ▸ Mobile Setup there)." });
    const body = bodyText ? (() => { try { return JSON.parse(bodyText); } catch { return null; } })() : null;
    for (const [m, re, h] of routes) {
      if (m !== method) continue;
      const hit = re.exec(path);
      if (hit) {
        const out = h(hit, body, uid, u);
        if (path === "/api/auth/login" && out?.user) sessionStorage.setItem("slims.demo.user", out.user.id);
        return json(200, clone(out));
      }
    }
    if (method === "GET") {
      if (path === "/api/meta") return json(200, { ...db["GET /api/meta"], loginRequired: false, showUserPicker: true, firstRun: false, serverTime: nowIso() });
      if (path === "/api/auth/me") { if (!uid) return json(401, { error: "Signed out." }); return json(200, clone(me(uid))); }
      if (path === "/api/mobile/app-info") return json(200, { ...db["GET /api/mobile/app-info"], phoneServerUrl: PHONE_SERVER });
      const v = readGet(path + u.search, uid);
      if (v !== undefined) return json(200, clone(v));
      if (/s$|\/(field|overdue|discrepancies|fields|receiving|labels|roles|devices|picker)$/.test(path)) return json(200, []);
      return json(404, { error: "This view has no data in the demo snapshot." });
    }
    // anything else the portal can do: accepted, so the screen carries on
    return json(200, body && typeof body === "object" ? { ...body, id: body.id ?? Date.now() % 100000 } : null);
  } catch (e) {
    if (e instanceof Fail) return json(e.status, { error: e.message, step: e.step });
    console.error("[demo]", method, path, e);
    return json(500, { error: "Demo: " + (e as Error).message });
  }
}

/** Routes every "/api/..." request (relative, or to the phone's server address) to the demo server. */
export function installFetch() {
  const real = window.fetch.bind(window);
  window.fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    const rel = url.startsWith("/api/") ? url : url.startsWith(PHONE_SERVER + "/api/") ? url.slice(PHONE_SERVER.length) : /^https?:\/\/[^/]+\/api\//.test(url) && !/tile|openstreetmap|fonts/.test(url) ? url.replace(/^https?:\/\/[^/]+/, "") : null;
    if (!rel) return real(input, init);
    const method = (init?.method ?? (input instanceof Request ? input.method : "GET")).toUpperCase();
    const headers = new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined));
    let text: string | null = null;
    if (typeof init?.body === "string") text = init.body;
    else if (init?.body instanceof FormData) text = JSON.stringify(Object.fromEntries([...init.body.entries()].map(([k, v]) => [k, typeof v === "string" ? v : "(file)"])));
    return handle(method, rel, headers, text);
  };
}
export const snapshotTakenAt = SNAP.takenAt;
export const fileUrl = (path: string) => { const f = files[path]; return f ? URL.createObjectURL(b64Blob(f)) : null; };
