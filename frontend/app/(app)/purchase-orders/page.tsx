"use client";
import { useState } from "react";
import { get, post, put } from "@/lib/api";
import type { PoDto, PoLineUpsert, VendorDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { date, money, qty, today } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

const BLANK: PoLineUpsert = { category: "LUM", subCategory: "LED", spec: "", description: "", uom: "Nos", qty: 0, rate: 0 };

export default function PurchaseOrdersPage() {
  const { config, can } = useAuth();
  const toast = useToast();
  const [status, setStatus] = useState("Open");
  const [edit, setEdit] = useState<PoDto | "new" | null>(null);
  const list = useAsync(() => get<PoDto[]>(`/api/purchase-orders${status ? `?status=${status}` : ""}`), [status]);
  const cur = config?.app.currency ?? "Rs.";

  return (
    <>
      <div className="filters">
        <div className="seg">
          {["Open", "Closed", ""].map((s) => <button key={s || "all"} className={status === s ? "active" : ""} onClick={() => setStatus(s)}>{s || "All"}</button>)}
        </div>
        <span className="spacer" />
        {can("procure") && <button className="btn primary" onClick={() => setEdit("new")}>New purchase order</button>}
      </div>

      <Card flush title="Purchase orders" sub="Approved outside SLIMS and recorded here so receipt can be validated against them (AS-01)">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No purchase orders in this view." cols={["PO", "Date", "Vendor", { t: "Lines", c: "r" }, { t: "Value", c: "r" }, { t: "Received", c: "r" }, "Warranty", "Status", ""]}>
            {list.data!.map((p) => {
              const ordered = p.lines.reduce((s, l) => s + l.qty, 0), received = p.lines.reduce((s, l) => s + l.received, 0);
              return (
                <tr key={p.id} className="row-link" onClick={() => setEdit(p)}>
                  <td><div className="two-line"><b className="mono">{p.number}</b><small>{p.remarks ?? ""}</small></div></td>
                  <td className="tiny">{date(p.date)}{p.deliveryDue && <div className="tiny">due {date(p.deliveryDue)}</div>}</td>
                  <td>{p.vendorName}</td>
                  <td className="r num">{p.lines.length}</td>
                  <td className="r num">{cur} {money(p.value)}</td>
                  <td className="r num">{qty(received)} / {qty(ordered)}
                    {received < ordered && <div className="tiny watch">{qty(ordered - received)} pending</div>}</td>
                  <td className="tiny">{p.warrantyMonths} mo</td>
                  <td><Chip tone={p.status === "Open" ? "ok" : "muted"}>{p.status}</Chip></td>
                  <td><span className="linky">Open</span></td>
                </tr>
              );
            })}
          </Table>
        )}
      </Card>

      {edit && <PoModal po={edit === "new" ? null : edit} onClose={() => setEdit(null)} onDone={() => { setEdit(null); list.reload(); }} cur={cur} canEdit={can("procure")} />}
    </>
  );
}

function PoModal({ po, onClose, onDone, cur, canEdit }: { po: PoDto | null; onClose: () => void; onDone: () => void; cur: string; canEdit: boolean }) {
  const { config } = useAuth();
  const toast = useToast();
  const vendors = useAsync(() => get<VendorDto[]>("/api/vendors"), []);
  const [number, setNumber] = useState(po?.number ?? "");
  const [vendorId, setVendorId] = useState<number | null>(po?.vendorId ?? null);
  const [d, setD] = useState(po?.date?.slice(0, 10) ?? today());
  const [due, setDue] = useState(po?.deliveryDue?.slice(0, 10) ?? "");
  const [warranty, setWarranty] = useState(String(po?.warrantyMonths ?? 24));
  const [remarks, setRemarks] = useState(po?.remarks ?? "");
  const [lines, setLines] = useState<PoLineUpsert[]>(po?.lines.map((l) => ({ category: l.category, subCategory: l.subCategory, spec: l.spec, description: l.description, uom: l.uom, qty: l.qty, rate: l.rate })) ?? [{ ...BLANK }]);
  const [busy, setBusy] = useState(false);
  const cats = config?.categories ?? [];
  const value = lines.reduce((s, l) => s + (+l.qty || 0) * (+l.rate || 0), 0);
  const locked = (i: number) => !!po?.lines[i]?.articleId;

  const save = async () => {
    setBusy(true);
    try {
      const body = { number, vendorId, date: d, deliveryDue: due || null, warrantyMonths: +warranty || 12, remarks: remarks || null, lines };
      if (po) await put(`/api/purchase-orders/${po.id}`, body); else await post("/api/purchase-orders", body);
      toast(po ? "Purchase order updated" : "Purchase order created"); onDone();
    } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };

  return (
    <Modal open onClose={onClose} size="wide" title={po ? po.number : "New purchase order"} sub={po ? `${po.vendorName} · ${po.status}` : "Recorded so that goods receipt can be matched against it"}
      footer={<>
        {canEdit && <button className="btn primary" disabled={busy || !number || !vendorId || lines.length === 0} onClick={save}>{po ? "Save changes" : "Create purchase order"}</button>}
        {po && canEdit && <button className="btn" disabled={busy} onClick={async () => {
          try { await post(`/api/purchase-orders/${po.id}/toggle`, undefined); toast(po.status === "Open" ? "Closed" : "Re-opened"); onDone(); } catch (e) { toast(errText(e), true); }
        }}>{po.status === "Open" ? "Close PO" : "Re-open PO"}</button>}
        <span className="spacer" /><button className="btn" onClick={onClose}>Close</button>
      </>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>PO number</label><input value={number} onChange={(e) => setNumber(e.target.value)} placeholder="PO-2611" disabled={!canEdit} /></div>
          <div className="field"><label>Vendor</label>
            <select value={vendorId ?? ""} onChange={(e) => setVendorId(e.target.value ? +e.target.value : null)} disabled={!canEdit}>
              <option value="">— choose —</option>
              {(vendors.data ?? []).filter((v) => v.active).map((v) => <option key={v.id} value={v.id}>{v.code} — {v.name}</option>)}
            </select>
          </div>
          <div className="field"><label>PO date</label><input type="date" value={d} onChange={(e) => setD(e.target.value)} disabled={!canEdit} /></div>
          <div className="field"><label>Delivery due</label><input type="date" value={due} onChange={(e) => setDue(e.target.value)} disabled={!canEdit} /></div>
          <div className="field"><label>Warranty (months)</label><input type="number" min="0" value={warranty} onChange={(e) => setWarranty(e.target.value)} disabled={!canEdit} />
            <span className="hint">Carried onto every item code from this PO</span></div>
        </div>
        <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} disabled={!canEdit} /></div>

        <div className="fsec">
          <h4>Lines</h4>
          <Table rows={lines.length} cols={["#", "Category", "Sub", "Spec", "Description", "UoM", { t: "Qty", c: "r" }, { t: "Rate", c: "r" }, { t: "Amount", c: "r" }, ""]}>
            {lines.map((l, i) => (
              <tr key={i}>
                <td className="num">{i + 1}{locked(i) && <div className="tiny">article created</div>}</td>
                <td><select value={l.category} disabled={!canEdit || locked(i)} style={{ minWidth: 86 }}
                  onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, category: e.target.value, subCategory: cats.find((c) => c.code === e.target.value)?.subs[0] ?? "" } : x))}>
                  {cats.map((c) => <option key={c.code} value={c.code}>{c.code}</option>)}
                </select></td>
                <td><select value={l.subCategory} disabled={!canEdit || locked(i)} style={{ minWidth: 80 }}
                  onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, subCategory: e.target.value } : x))}>
                  {(cats.find((c) => c.code === l.category)?.subs ?? [l.subCategory]).map((s) => <option key={s}>{s}</option>)}
                </select></td>
                <td><input value={l.spec} disabled={!canEdit || locked(i)} style={{ width: 78 }} placeholder="090W"
                  onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, spec: e.target.value } : x))} /></td>
                <td><input value={l.description} disabled={!canEdit || locked(i)} style={{ width: "100%", minWidth: 240 }}
                  onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, description: e.target.value } : x))} /></td>
                <td><select value={l.uom} disabled={!canEdit || locked(i)} style={{ minWidth: 82 }}
                  onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, uom: e.target.value } : x))}>
                  {(config?.uoms ?? ["Nos"]).map((u) => <option key={u}>{u}</option>)}
                </select></td>
                <td className="r"><input type="number" min="0" step="0.001" value={l.qty} disabled={!canEdit} style={{ width: 88, textAlign: "right" }}
                  onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, qty: +e.target.value } : x))} /></td>
                <td className="r"><input type="number" min="0" step="0.01" value={l.rate} disabled={!canEdit} style={{ width: 96, textAlign: "right" }}
                  onChange={(e) => setLines(lines.map((x, j) => j === i ? { ...x, rate: +e.target.value } : x))} /></td>
                <td className="r num">{money(l.qty * l.rate)}</td>
                <td>{canEdit && !locked(i) && <button className="btn small" onClick={() => setLines(lines.length === 1 ? [{ ...BLANK }] : lines.filter((_, j) => j !== i))}>×</button>}</td>
              </tr>
            ))}
            <tr className="total"><td colSpan={8}>Total</td><td className="r num">{cur} {money(value)}</td><td /></tr>
          </Table>
          {canEdit && <div className="actions"><button className="btn small" onClick={() => setLines([...lines, { ...BLANK }])}>Add line</button></div>}
          {po && <Notice kind="info"><span>Lines that already have an article cannot be removed or re-specified — the item code and its QR series depend on them.</span></Notice>}
        </div>

        {po && <div className="fsec"><h4>Receipt position</h4>
          <Table rows={po.lines.length} cols={["Item code", { t: "Ordered", c: "r" }, { t: "Received", c: "r" }, { t: "Issued", c: "r" }, { t: "Balance", c: "r" }]}>
            {po.lines.map((l) => (
              <tr key={l.id}>
                <td className="mono">{l.itemCode ?? <span className="muted">no article yet</span>}</td>
                <td className="r num">{qty(l.qty)}</td><td className="r num">{qty(l.received)}</td><td className="r num">{qty(l.issued)}</td>
                <td className="r num"><b className={l.qty - l.received > 0 ? "watch" : ""}>{qty(l.qty - l.received)}</b></td>
              </tr>
            ))}
          </Table>
        </div>}
      </div>
    </Modal>
  );
}
