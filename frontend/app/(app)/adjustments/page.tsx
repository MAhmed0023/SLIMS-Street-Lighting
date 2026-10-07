"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import type { AdjustmentDto, ScanLookupDto, StoreDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { dateTime, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { ScanBox } from "@/components/Controls";

/** FR-INV-020: every adjustment carries a reason code and needs approval by somebody other than the person who raised it.
 * The book changes only on approval; both the original and the adjusted quantity stay on the record. */
export default function AdjustmentsPage() {
  const { can, config, user } = useAuth();
  const toast = useToast();
  const [status, setStatus] = useState("Pending");
  const list = useAsync(() => get<AdjustmentDto[]>(`/api/adjustments${status ? `?status=${status}` : ""}`), [status]);
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const [raise, setRaise] = useState(false);
  const [decide, setDecide] = useState<AdjustmentDto | null>(null);
  const canRaise = can("receive") || can("issue") || can("approve");

  return (
    <>
      <div className="filters">
        <div className="seg">{["Pending", "Approved", "Rejected", ""].map((s) => <button key={s} className={status === s ? "active" : ""} onClick={() => setStatus(s)}>{s || "All"}</button>)}</div>
        <span className="spacer" />
        {canRaise && <button className="btn primary" onClick={() => setRaise(true)}>Raise adjustment</button>}
      </div>
      <Card flush title="Stock adjustments" sub="Loss, found and pack-quantity corrections with their reason code and approval">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No adjustments in this view." cols={["Adjustment", "Raised", "Store", "Item", "Kind · reason", { t: "Before → after", c: "r" }, "Status", ""]}>
            {list.data!.map((a) => (
              <tr key={a.id}>
                <td className="mono">{a.number}{a.physicalCountId ? <div className="tiny">from a physical count</div> : null}</td>
                <td className="tiny">{dateTime(a.raisedAt)}<div className="tiny">{a.raisedBy}</div></td>
                <td>{a.storeCode}</td>
                <td><div className="two-line"><b className="mono">{a.itemCode} #{a.serial}</b><small>{a.description}</small></div></td>
                <td><b>{a.kind}</b><div className="tiny">{a.reasonCode}{a.remarks ? ` — ${a.remarks}` : ""}</div></td>
                <td className="r num">{qty(a.qtyBefore)} → <b>{qty(a.qtyAfter)}</b></td>
                <td><Chip tone={a.status === "Approved" ? "ok" : a.status === "Rejected" ? "muted" : "watch"}>{a.status}</Chip>{a.decidedBy && <div className="tiny">{a.decidedBy} · {dateTime(a.decidedAt)}</div>}</td>
                <td>{a.status === "Pending" && can("approve") && a.raisedById !== user?.id && <button className="btn small" onClick={() => setDecide(a)}>Decide</button>}
                  {a.status === "Pending" && a.raisedById === user?.id && <span className="tiny">awaiting another approver</span>}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      {raise && <RaiseModal reasons={config?.adjustmentReasons ?? []} stores={stores.data ?? []} onClose={() => setRaise(false)} onDone={() => { setRaise(false); setStatus("Pending"); list.reload(); }} />}
      {decide && <Modal open onClose={() => setDecide(null)} size="narrow" title={`Decide ${decide.number}`} sub={`${decide.kind} · ${decide.itemCode} #${decide.serial}`}
        footer={<DecideFooter id={decide.id} onDone={() => { setDecide(null); list.reload(); toast("Decision recorded"); }} onClose={() => setDecide(null)} />}>
        <div className="kv">
          <div><span>Store</span><b>{decide.storeCode}</b></div><div><span>Reason code</span><b>{decide.reasonCode}</b></div>
          <div><span>Book quantity</span><b className="num">{qty(decide.qtyBefore)}</b></div><div><span>Adjusted to</span><b className="num">{qty(decide.qtyAfter)}</b></div>
          <div><span>Raised by</span><b>{decide.raisedBy}</b></div><div><span>Item now</span><b>{decide.itemStatus}</b></div>
        </div>
        {decide.remarks && <Notice kind="info">{decide.remarks}</Notice>}
      </Modal>}
    </>
  );
}

function DecideFooter({ id, onDone, onClose }: { id: number; onDone: () => void; onClose: () => void }) {
  const toast = useToast();
  const [note, setNote] = useState(""); const [busy, setBusy] = useState(false);
  const act = async (approve: boolean) => {
    setBusy(true);
    try { await post(`/api/adjustments/${id}/decide`, { approve, note: note || null }); onDone(); } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  return <>
    <input value={note} onChange={(e) => setNote(e.target.value)} placeholder="Note (required to reject)" style={{ flex: 1 }} />
    <button className="btn primary" disabled={busy} onClick={() => act(true)}>Approve</button>
    <button className="btn danger" disabled={busy || !note.trim()} onClick={() => act(false)}>Reject</button>
    <button className="btn" onClick={onClose}>Cancel</button>
  </>;
}

function RaiseModal({ reasons, stores, onClose, onDone }: { reasons: string[]; stores: StoreDto[]; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const [hit, setHit] = useState<ScanLookupDto | null>(null);
  const [kind, setKind] = useState("Loss"); const [reason, setReason] = useState(reasons[0] ?? ""); const [after, setAfter] = useState(""); const [loc, setLoc] = useState(""); const [remarks, setRemarks] = useState("");
  const [busy, setBusy] = useState(false);
  const item = hit?.item ?? null;
  const bins = stores.flatMap((s) => s.locations).filter((l) => l.kind === "Stock" && l.active);
  return (
    <Modal open onClose={onClose} title="Raise a stock adjustment" size="narrow"
      footer={<><button className="btn primary" disabled={busy || !item || !reason || (kind === "Quantity" && after === "") || (kind === "Found" && !loc)} onClick={async () => {
        setBusy(true);
        try {
          const a = await post<AdjustmentDto>("/api/adjustments", { qr: item!.qrCode, kind, reasonCode: reason, qtyAfter: kind === "Quantity" ? +after : null, locationId: kind === "Found" ? +loc : null, remarks: remarks || null });
          toast(`${a.number} raised — awaiting approval by another officer`); onDone();
        } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Raise for approval</button><button className="btn" onClick={onClose}>Cancel</button></>}>
      <ScanBox onScan={async (qr) => { try { const r = await get<ScanLookupDto>(`/api/stock-control/lookup?qr=${encodeURIComponent(qr)}`); setHit(r); if (r.item?.status === "WrittenOff") setKind("Found"); } catch (e) { toast(errText(e), true); } }} placeholder="Scan the item label" />
      {hit && <Notice kind={item ? "info" : "crit"}>{hit.message}</Notice>}
      <div className="form">
        <div className="field"><label>Kind</label>
          <select value={kind} onChange={(e) => setKind(e.target.value)}>
            <option value="Loss">Loss — write the item off the book</option>
            <option value="Found">Found — bring a written-off item back</option>
            <option value="Quantity">Quantity — correct a bulk pack</option>
          </select></div>
        <div className="field"><label>Reason code</label><select value={reason} onChange={(e) => setReason(e.target.value)}>{reasons.map((r) => <option key={r}>{r}</option>)}</select></div>
        {kind === "Quantity" && <div className="field"><label>Counted quantity {item ? `(book ${qty(item.qty)} ${item.uom})` : ""}</label><input type="number" min="0" step="0.001" value={after} onChange={(e) => setAfter(e.target.value)} /></div>}
        {kind === "Found" && <div className="field"><label>Found in bin</label><select value={loc} onChange={(e) => setLoc(e.target.value)}><option value="">—</option>{bins.map((l) => <option key={l.id} value={l.id}>{l.fullCode}</option>)}</select></div>}
        <div className="field"><label>Remarks</label><textarea value={remarks} onChange={(e) => setRemarks(e.target.value)} /></div>
      </div>
    </Modal>
  );
}
