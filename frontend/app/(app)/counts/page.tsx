"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import type { CountDto, CountScanDto, StoreDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { dateTime, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { ScanBox } from "@/components/Controls";

const clearTone = (c: string) => c === "Open" ? "crit" : "ok";

/** FR-INV-021: count by scanning; the variance against book stock is frozen on submission and cleared only through
 * approved adjustments (or, for misplaced items, a bin move). */
export default function CountsPage() {
  const { can, config } = useAuth();
  const toast = useToast();
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const list = useAsync(() => get<CountDto[]>("/api/counts"), []);
  const [storeId, setStoreId] = useState(""); const [locId, setLocId] = useState("");
  const [open, setOpen] = useState<CountDto | null>(null);
  const allowed = can("receive") || can("approve");

  const start = async () => {
    try { const c = await post<CountDto>("/api/counts", { storeId: +storeId, locationId: locId ? +locId : null }); toast(`${c.number} started`); list.reload(); setOpen(c); }
    catch (e) { toast(errText(e), true); }
  };
  if (stores.loading) return <Loading what="stores" />;
  if (stores.error) return <ErrorBox error={stores.error} />;
  const store = stores.data!.find((s) => s.id === +storeId);
  return (
    <>
      {allowed && <Card title="Start a count" sub="A whole store, or one bin at a time">
        <div className="filters">
          <select value={storeId} onChange={(e) => { setStoreId(e.target.value); setLocId(""); }}><option value="">— store —</option>{stores.data!.map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}</select>
          <select value={locId} onChange={(e) => setLocId(e.target.value)} disabled={!store}><option value="">Whole store</option>{(store?.locations ?? []).filter((l) => l.active).map((l) => <option key={l.id} value={l.id}>{l.fullCode} ({l.items})</option>)}</select>
          <button className="btn primary" disabled={!storeId} onClick={start}>Start count</button>
        </div>
      </Card>}
      <Card flush title="Physical verification" sub="Open counts are scanned; submitted counts show their variance" actions={<button className="btn small" onClick={list.reload}>Refresh</button>}>
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No counts yet." cols={["Count", "Started", "Scope", { t: "Scanned", c: "r" }, "Variances", "Status", ""]}>
            {list.data!.map((c) => (
              <tr key={c.id} className="row-link" onClick={() => setOpen(c)}>
                <td className="mono">{c.number}</td>
                <td className="tiny">{dateTime(c.startedAt)}<div className="tiny">{c.startedBy}</div></td>
                <td>{c.storeCode} {c.locationCode ? <span className="mono tiny">{c.locationCode}</span> : <span className="tiny">whole store</span>}</td>
                <td className="r num">{c.scanned}</td>
                <td>{c.status === "Open" ? <span className="tiny">—</span> : <>{c.variances.length} <span className="tiny">({c.variances.filter((v) => v.clearance === "Open").length} open)</span></>}</td>
                <td><Chip tone={c.status === "Closed" ? "ok" : c.status === "Submitted" ? "watch" : "info"}>{c.status}</Chip></td>
                <td><span className="linky">Open</span></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      {open && <CountModal id={open.id} reasons={config?.adjustmentReasons ?? []} allowed={allowed} onClose={() => { setOpen(null); list.reload(); }} />}
    </>
  );
}

function CountModal({ id, reasons, allowed, onClose }: { id: number; reasons: string[]; allowed: boolean; onClose: () => void }) {
  const toast = useToast();
  const c = useAsync(() => get<CountDto>(`/api/counts/${id}`), [id]);
  const [last, setLast] = useState<CountScanDto | null>(null);
  const [reason, setReason] = useState(reasons.find((r) => r.startsWith("Count")) ?? reasons[0] ?? "");
  const [busy, setBusy] = useState(false);
  const act = async (path: string, body?: unknown, ok?: string) => {
    setBusy(true);
    try { await post(`/api/counts/${id}/${path}`, body ?? {}); if (ok) toast(ok); c.reload(); } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const x = c.data;
  return (
    <Modal open onClose={onClose} size="wide" title={x ? `${x.number} — ${x.storeCode}${x.locationCode ? " · " + x.locationCode : ""}` : "Count"} sub={x ? `Started ${dateTime(x.startedAt)} by ${x.startedBy}` : ""}
      chips={x && <Chip tone={x.status === "Closed" ? "ok" : "watch"}>{x.status}</Chip>}
      footer={x && allowed && <>
        {x.status === "Open" && <button className="btn primary" disabled={busy} onClick={() => act("submit", undefined, "Submitted — variance frozen")}>Submit count</button>}
        {x.status === "Submitted" && x.variances.some((v) => (v.kind === "Missing" || v.kind === "Quantity") && !v.adjustmentId) && <>
          <select value={reason} onChange={(e) => setReason(e.target.value)}>{reasons.map((r) => <option key={r}>{r}</option>)}</select>
          <button className="btn" disabled={busy} onClick={() => act("adjustments", { reasonCode: reason }, "Adjustments raised for approval")}>Raise adjustments for the variances</button></>}
        {x.status === "Submitted" && <button className="btn primary" disabled={busy} onClick={() => act("close", undefined, "Count closed")}>Close count</button>}
        <span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      {c.loading && !x ? <Loading /> : c.error ? <ErrorBox error={c.error} /> : x && <>
        {x.status === "Open" && allowed && <>
          <ScanBox onScan={async (qr) => { try { const r = await post<CountScanDto>(`/api/counts/${id}/scan`, { qr, qty: null, locationId: x.locationId ?? null }); setLast(r); c.reload(); } catch (e) { toast(errText(e), true); } }} placeholder="Scan every label you find" />
          {last && <Notice kind={last.result === "OK" ? "good" : "watch"}><span><b className="mono">{last.itemCode ?? last.qr} {last.serial ? "#" + last.serial : ""}</b> — {last.result}</span></Notice>}
          <div className="tiny" style={{ margin: "6px 0" }}>Book stock in scope: <b>{x.bookItems}</b> label(s) · scanned: <b>{x.scanned}</b></div>
          <Table rows={x.scans.length} empty="Nothing scanned yet." cols={["Item", { t: "Qty", c: "r" }, "Where", "Scanned"]}>
            {x.scans.map((s) => <tr key={s.qr}><td className="mono">{s.itemCode ?? s.qr} {s.serial ? "#" + s.serial : ""}</td><td className="r num">{qty(s.qty)}</td><td className="mono tiny">{s.locationCode ?? "—"}</td><td className="tiny">{dateTime(s.at)}</td></tr>)}
          </Table>
        </>}
        {x.status !== "Open" && <>
          <Notice kind={x.variances.length === 0 ? "good" : "watch"}>{x.variances.length === 0 ? "No variance: the scan matched book stock exactly." : `${x.variances.length} variance line(s). Missing items and quantity differences are cleared only by approved adjustments; misplaced items by moving them.`}</Notice>
          <Table rows={x.variances.length} cols={["Kind", "Item", "Book", "Counted", { t: "Book qty", c: "r" }, { t: "Counted", c: "r" }, "Adjustment", "Clearance"]}>
            {x.variances.map((v, i) => <tr key={i}>
              <td><Chip tone={v.kind === "Missing" ? "crit" : "watch"}>{v.kind}</Chip></td>
              <td className="mono">{v.itemCode ?? v.qr} {v.serial ? "#" + v.serial : ""}</td>
              <td className="mono tiny">{v.bookLocation ?? "—"}</td><td className="mono tiny">{v.countedLocation ?? "—"}</td>
              <td className="r num">{qty(v.bookQty)}</td><td className="r num">{qty(v.countedQty)}</td>
              <td className="tiny">{v.adjustmentId ? `#${v.adjustmentId} · ${v.adjustmentStatus}` : "—"}</td>
              <td><Chip tone={clearTone(v.clearance)}>{v.clearance}</Chip></td>
            </tr>)}
          </Table>
        </>}
      </>}
    </Modal>
  );
}
