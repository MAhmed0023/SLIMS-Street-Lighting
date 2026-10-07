"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import type { ReturnDto, StoreDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { date, dateTime, money, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { AmendButton } from "@/components/Controls";

const STAGES = [
  { k: "reported", t: "Reported at site" },
  { k: "quarantine", t: "In quarantine" },
  { k: "repair", t: "Under repair" },
  { k: "warranty", t: "Warranty claim" },
  { k: "scrap", t: "Scrap / condemned" },
  { k: "closed", t: "Closed" },
  { k: "", t: "All" },
];
const classTone = (c: string) => c === "Usable" ? "ok" : c === "Warranty" ? "info" : c === "Repairable" ? "watch" : c === "Scrap" ? "muted" : "";

export default function ReturnsPage() {
  const { config, can } = useAuth();
  const toast = useToast();
  const [stage, setStage] = useState("quarantine");
  const [open, setOpen] = useState<ReturnDto | null>(null);
  const [report, setReport] = useState(false);
  const [receive, setReceive] = useState(false);
  const list = useAsync(() => get<ReturnDto[]>(`/api/returns${stage ? `?stage=${stage}` : ""}`), [stage]);
  const overdue = useAsync(() => get<ReturnDto[]>("/api/returns/overdue"), []);
  const cur = config?.app.currency ?? "Rs.";
  const reload = () => { list.reload(); overdue.reload(); };

  return (
    <>
      <div className="filters">
        <div className="seg">{STAGES.map((s) => <button key={s.k || "all"} className={stage === s.k ? "active" : ""} onClick={() => setStage(s.k)}>{s.t}</button>)}</div>
        <span className="spacer" />
        {can("install") && <button className="btn" onClick={() => setReport(true)}>Report out of order at site</button>}
        {can("return") && <button className="btn primary" onClick={() => setReceive(true)}>Receive return at store</button>}
      </div>

      {(overdue.data?.length ?? 0) > 0 &&
        <Notice kind="crit"><span>
          <b>{overdue.data!.length} item(s)</b> were removed at site more than {config?.controls.returnDays ?? 7} days ago and have not been scanned back into any store (BR-029).
        </span></Notice>}

      <Card flush title="Returns" sub="A returned item goes into quarantine, never straight back into issuable stock (BR-018)">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="Nothing in this stage." cols={["Return", "Item", "Site", "Reason", { t: "In service", c: "r" }, "Warranty", "Classification", "Item status", ""]}>
            {list.data!.map((r) => (
              <tr key={r.id} className="row-link" onClick={() => setOpen(r)}>
                <td><div className="two-line"><b className="mono">{r.number}</b><small>{date(r.reportedAt)}</small></div></td>
                <td><div className="two-line"><b className="mono">{r.itemCode}</b><small>{r.description}</small></div></td>
                <td className="tiny">{r.siteCode ?? "—"}</td>
                <td style={{ maxWidth: 220 }}>{r.reason}</td>
                <td className="r num">{r.serviceDays != null ? `${r.serviceDays} d` : "—"}</td>
                <td>{r.warrantyEligible ? <Chip tone="ok">eligible</Chip> : r.warrantyExpires ? <Chip tone="muted">expired {date(r.warrantyExpires)}</Chip> : "—"}</td>
                <td>{r.classification === "Pending" ? <Chip tone="watch">to classify</Chip> : <Chip tone={classTone(r.classification)}>{r.classification}</Chip>}</td>
                <td className="tiny">{r.itemStatus}</td>
                <td><span className="linky">Open</span></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>

      {open && <DetailModal r={open} onClose={() => setOpen(null)} cur={cur} onDone={() => { setOpen(null); reload(); }} />}
      {report && <ReportModal onClose={() => setReport(false)} onDone={() => { setReport(false); reload(); toast("Recorded — bring the item back to the store within the prescribed period"); }} />}
      {receive && <ReceiveModal onClose={() => setReceive(false)} onDone={() => { setReceive(false); reload(); }} />}
    </>
  );
}

function DetailModal({ r, onClose, cur, onDone }: { r: ReturnDto; onClose: () => void; cur: string; onDone: () => void }) {
  const { can } = useAuth();
  const toast = useToast();
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const [cls, setCls] = useState(r.classification === "Pending" ? "Repairable" : r.classification);
  const [claim, setClaim] = useState(r.claimRef ?? "");
  const [remarks, setRemarks] = useState("");
  const [scrapValue, setScrapValue] = useState("");
  const [locId, setLocId] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const act = async (path: string, body: unknown, ok: string) => {
    setBusy(true);
    try { await post(`/api/returns/${r.id}/${path}`, body); toast(ok); onDone(); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const stockLocs = (stores.data ?? []).flatMap((s) => s.locations.filter((l) => l.kind === "Stock" && l.active).map((l) => ({ ...l, store: s.code })));

  return (
    <Modal open onClose={onClose} title={`${r.number} — ${r.itemCode}`} sub={`${r.description} · PO ${r.poNumber} · ${r.vendorName}`}
      chips={<><Chip tone={classTone(r.classification)}>{r.classification === "Pending" ? "Awaiting classification" : r.classification}</Chip>
        <Chip tone={r.warrantyEligible ? "ok" : "muted"}>{r.warrantyEligible ? "Within warranty" : "Out of warranty"}</Chip>
        <Chip tone="plain">{r.itemStatus}</Chip></>}
      footer={<>
        {r.receivedAt && r.classification === "Pending" && can("return") &&
          <button className="btn primary" disabled={busy || (cls === "Usable" && !locId)} onClick={() => act("classify", { classification: cls, claimRef: claim || null, remarks: remarks || null, storeLocationId: cls === "Usable" ? locId : null }, cls === "Usable" ? "Back in stock and available" : `Classified as ${cls}`)}>{cls === "Usable" ? "Put back into stock" : `Classify as ${cls}`}</button>}
        <AmendButton entityType="return" entityRef={r.number} current={{ Reason: r.reason, Remarks: r.remarks }} />
        {["Repairable", "Warranty"].includes(r.classification) && !r.closedAt && can("receive") &&
          <button className="btn primary" disabled={busy || !locId} onClick={() => act("reinward", { storeLocationId: locId, remarks: remarks || null }, "Re-inwarded into issuable stock")}>Re-inward to stock</button>}
        {r.classification === "Scrap" && !r.disposalApprovedAt && can("approve") &&
          <button className="btn danger" disabled={busy} onClick={() => act("dispose", { scrapValue: scrapValue ? +scrapValue : null, remarks: remarks || null }, "Disposal approved; written into the scrap register")}>Approve disposal</button>}
        <span className="spacer" /><button className="btn" onClick={onClose}>Close</button>
      </>}>
      <div className="kv">
        <div><span>Reported by</span><b>{r.reportedBy}</b></div>
        <div><span>Reported at</span><b>{dateTime(r.reportedAt)}</b></div>
        <div><span>Kind</span><b>{r.kind}</b></div>
        <div><span>Site</span><b>{r.siteCode ?? "—"}</b></div>
        <div><span>Installed on</span><b>{date(r.installedAt)}</b></div>
        <div><span>Days in service</span><b className="num">{r.serviceDays ?? "—"}</b></div>
        <div><span>Warranty expires</span><b>{date(r.warrantyExpires)}</b></div>
        <div><span>Received at store</span><b>{r.receivedAt ? `${r.storeCode} · ${dateTime(r.receivedAt)}` : "not yet"}</b></div>
        <div><span>Quarantine bin</span><b className="mono tiny">{r.locationCode ?? "—"}</b></div>
        <div><span>Claim reference</span><b>{r.claimRef ?? "—"}</b></div>
        <div><span>Disposal approved</span><b>{dateTime(r.disposalApprovedAt)}</b></div>
        <div><span>Scrap value</span><b className="num">{r.scrapValue != null ? `${cur} ${money(r.scrapValue)}` : "—"}</b></div>
      </div>
      <Notice kind="info"><span><b>Failure reason:</b> {r.reason}{r.remarks ? ` — ${r.remarks}` : ""}</span></Notice>

      {r.receivedAt && r.classification === "Pending" && can("return") && <div className="form">
        <div className="fsec"><h4>Classify this return</h4>
          <div className="frow">
            <div className="field"><label>Route</label>
              <select value={cls} onChange={(e) => setCls(e.target.value)}>
                <option value="Usable">Usable — tested OK, back into stock</option>
                <option value="Repairable">Repairable — send for repair</option>
                <option value="Warranty" disabled={!r.warrantyEligible}>Warranty — claim on the vendor{!r.warrantyEligible ? " (expired)" : ""}</option>
                <option value="Scrap">Scrap — condemn and dispose</option>
              </select>
              {!r.warrantyEligible && <span className="hint">Warranty eligibility is computed by the system from the installation date (BR-019).</span>}
            </div>
            {cls === "Warranty" && <div className="field"><label>Claim reference</label><input value={claim} onChange={(e) => setClaim(e.target.value)} placeholder="WRC/V0231/26-27/0018" /></div>}
            {cls === "Usable" && <div className="field"><label>Stock bin in {r.storeCode} (FR-INV-033)</label>
              <select value={locId ?? ""} onChange={(e) => setLocId(e.target.value ? +e.target.value : null)}>
                <option value="">— choose —</option>
                {stockLocs.filter((l) => l.storeId === r.storeId).map((l) => <option key={l.id} value={l.id}>{l.fullCode}</option>)}
              </select></div>}
          </div>
          <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} /></div>
        </div>
      </div>}

      {["Repairable", "Warranty"].includes(r.classification) && !r.closedAt && can("receive") && <div className="form">
        <div className="fsec"><h4>Repaired or replaced — bring it back into stock</h4>
          <div className="field"><label>Stock bin</label>
            <select value={locId ?? ""} onChange={(e) => setLocId(e.target.value ? +e.target.value : null)}>
              <option value="">— choose a stock location —</option>
              {stockLocs.map((l) => <option key={l.id} value={l.id}>{l.fullCode}</option>)}
            </select>
            <span className="hint">The same label and its whole history follow the item back into stock (FR-RT-07).</span>
          </div>
          <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} placeholder="Driver replaced under warranty; vendor DC-5012" /></div>
        </div>
      </div>}

      {r.classification === "Scrap" && !r.disposalApprovedAt && can("approve") && <div className="form">
        <div className="fsec"><h4>Condemnation</h4>
          <Notice kind="crit">An item cannot be written out of stock without recorded disposal approval (BR-020).</Notice>
          <div className="frow">
            <div className="field"><label>Scrap value ({cur})</label><input type="number" min="0" step="0.01" value={scrapValue} onChange={(e) => setScrapValue(e.target.value)} /></div>
            <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} placeholder="Condemnation board approval 14-09-2026" /></div>
          </div>
        </div>
      </div>}
    </Modal>
  );
}

function ReportModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const { config } = useAuth();
  const toast = useToast();
  const [qr, setQr] = useState(""); const [reason, setReason] = useState(""); const [kind, setKind] = useState("OutOfOrder"); const [remarks, setRemarks] = useState("");
  const [busy, setBusy] = useState(false);
  return (
    <Modal open onClose={onClose} size="narrow" title="Report an item out of order" sub="Recorded against the site; then bring it back to the store"
      footer={<><button className="btn primary" disabled={busy || !qr.trim() || !reason} onClick={async () => {
        setBusy(true);
        try { await post("/api/returns/report", { qr, reason, kind, remarks: remarks || null }); onDone(); }
        catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Record</button><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="form">
        <div className="field"><label>Scan the item at site</label><input className="mono" value={qr} onChange={(e) => setQr(e.target.value)} placeholder="SLIMS|…" autoFocus /></div>
        <div className="field"><label>What happened</label>
          <select value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="OutOfOrder">Failed in service</option>
            <option value="Dismantled">Dismantled during replacement / shifting</option>
            <option value="Unused">Unused — returning surplus material</option>
          </select>
        </div>
        <div className="field"><label>Reason</label>
          <select value={reason} onChange={(e) => setReason(e.target.value)}>
            <option value="">— choose a reason —</option>
            {(config?.failureReasons ?? []).map((r) => <option key={r}>{r}</option>)}
          </select>
        </div>
        <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} /></div>
      </div>
    </Modal>
  );
}

function ReceiveModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const [qr, setQr] = useState(""); const [storeId, setStoreId] = useState<number | null>(null); const [locId, setLocId] = useState<number | null>(null); const [remarks, setRemarks] = useState("");
  const [busy, setBusy] = useState(false);
  const store = stores.data?.find((s) => s.id === storeId);
  return (
    <Modal open onClose={onClose} size="narrow" title="Receive a returned item" sub="Scanned back in — the system recovers its full history"
      footer={<><button className="btn primary" disabled={busy || !qr.trim() || !storeId} onClick={async () => {
        setBusy(true);
        try {
          const r = await post<ReturnDto>("/api/returns/receive", { qr, storeId, storeLocationId: locId, remarks: remarks || null });
          toast(`${r.number} received into ${r.locationCode ?? "quarantine"}${r.warrantyEligible ? " — within warranty" : ""}`);
          onDone();
        } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Receive</button><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="form">
        <div className="field"><label>Scan the returned item</label><input className="mono" value={qr} onChange={(e) => setQr(e.target.value)} placeholder="SLIMS|…" autoFocus /></div>
        <div className="field"><label>Store</label>
          <select value={storeId ?? ""} onChange={(e) => { setStoreId(e.target.value ? +e.target.value : null); setLocId(null); }}>
            <option value="">— choose a store —</option>
            {(stores.data ?? []).filter((s) => s.active).map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
          </select>
        </div>
        <div className="field"><label>Location <span className="hint">(failed items default to the quarantine bin)</span></label>
          <select value={locId ?? ""} onChange={(e) => setLocId(e.target.value ? +e.target.value : null)} disabled={!store}>
            <option value="">— automatic —</option>
            {(store?.locations ?? []).filter((l) => l.active).map((l) => <option key={l.id} value={l.id}>{l.fullCode} ({l.kind})</option>)}
          </select>
        </div>
        <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} /></div>
      </div>
    </Modal>
  );
}
