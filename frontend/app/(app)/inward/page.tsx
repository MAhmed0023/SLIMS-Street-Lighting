"use client";
import { useMemo, useState } from "react";
import { get, post } from "@/lib/api";
import type { DiscrepancyDto, GrnDto, ItemDto, PoDto, StoreDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { byItemCode } from "@/lib/items";
import { date, dateTime, int, money, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { AmendButton } from "@/components/Controls";

interface LineQty { qty: string; rejected: string; reason: string; remarks: string; photo: string | null }
const blank: LineQty = { qty: "", rejected: "", reason: "", remarks: "", photo: null };
const num = (v: string) => { const n = Number(v); return Number.isFinite(n) && n > 0 ? n : 0; };
const toB64 = (f: File) => new Promise<string>((res, rej) => { const r = new FileReader(); r.onload = () => res(String(r.result)); r.onerror = rej; r.readAsDataURL(f); });

export default function InwardPage() {
  const { config, can } = useAuth();
  const toast = useToast();
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const pos = useAsync(() => get<PoDto[]>("/api/purchase-orders?status=Open"), []);
  const [storeId, setStoreId] = useState<number | null>(null);
  const [poId, setPoId] = useState<number | null>(null);
  const [condition, setCondition] = useState("");
  const [qtys, setQtys] = useState<Record<number, LineQty>>({});
  const [locId, setLocId] = useState<number | null>(null);
  const [challan, setChallan] = useState(""); const [invoice, setInvoice] = useState(""); const [invDate, setInvDate] = useState("");
  const [remarks, setRemarks] = useState(""); const [busy, setBusy] = useState(false);
  const [tab, setTab] = useState<"receive" | "register" | "discrepancies">("receive");
  const [detail, setDetail] = useState<GrnDto | null>(null);
  const [dNote, setDNote] = useState(false);

  const grns = useAsync(() => get<GrnDto[]>("/api/grn"), [tab === "register"]);
  const discs = useAsync(() => get<DiscrepancyDto[]>("/api/grn/discrepancies"), [tab === "discrepancies"]);
  const items = useAsync(() => detail ? get<ItemDto[]>(`/api/grn/${detail.id}/items`) : Promise.resolve([]), [detail?.id]);

  const store = stores.data?.find((s) => s.id === storeId) ?? null;
  const po = pos.data?.find((p) => p.id === poId) ?? null;
  const cur = config?.app.currency ?? "Rs.";
  // where the material goes: a stock bin (default — available for issue at once) or the Receiving area
  const places = useMemo(() => (store?.locations ?? []).filter((l) => l.active && (l.kind === "Stock" || l.kind === "Receiving"))
    .sort((a, b) => (a.kind === b.kind ? a.fullCode.localeCompare(b.fullCode) : a.kind === "Stock" ? -1 : 1)), [store]);
  const poLines = (po?.lines ?? []).filter((l) => l.articleId);
  // default: the stock bin of the item category still due on the PO (bins are laid out by category, e.g. CEN01-BRK-R03-B01)
  const due = poLines.find((l) => l.qty - l.received > 0);
  const place = places.find((l) => l.id === locId) ?? places.find((l) => l.kind === "Stock" && due && l.section === due.category) ?? places[0] ?? null;
  const set = (id: number, patch: Partial<LineQty>) => setQtys((q) => ({ ...q, [id]: { ...blank, ...q[id], ...patch } }));
  const entered = poLines.map((l) => ({ l, q: qtys[l.articleId!] ?? blank })).filter((x) => num(x.q.qty) > 0 || num(x.q.rejected) > 0);
  const totalIn = entered.reduce((s, x) => s + num(x.q.qty), 0);
  const totalRej = entered.reduce((s, x) => s + num(x.q.rejected), 0);
  const totalValue = entered.reduce((s, x) => s + num(x.q.qty) * x.l.rate, 0);
  const missingReason = entered.some((x) => num(x.q.rejected) > 0 && !x.q.reason);

  const postGrn = async () => {
    if (!storeId || !poId) { toast("Choose the store and the purchase order first.", true); return; }
    setBusy(true);
    try {
      const g = await post<GrnDto>("/api/grn", {
        purchaseOrderId: poId, storeId, storeLocationId: place?.id ?? null, qrCodes: [], condition: condition || null,
        lines: entered.map(({ l, q }) => ({ articleId: l.articleId, qty: num(q.qty), rejectedQty: num(q.rejected), rejectReason: q.reason || null, rejectRemarks: q.remarks || null, rejectPhotoBase64: q.photo })),
        challanNo: challan || null, invoiceNo: invoice || null, invoiceDate: invDate || null, remarks: remarks || null,
      });
      toast(`${g.number} posted — ${qty(g.qty)} received into ${place?.fullCode ?? "the store"}${g.rejectedCount ? `, ${g.rejectedCount} rejected` : ""}, ${cur} ${money(g.value)}`);
      setQtys({}); setCondition(""); setChallan(""); setInvoice(""); setInvDate(""); setRemarks("");
      pos.reload(); grns.reload(); stores.reload();
    } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };

  if (stores.loading || pos.loading) return <Loading what="stores and purchase orders" />;
  if (stores.error) return <ErrorBox error={stores.error} />;

  return (
    <>
      <div className="seg">
        <button className={tab === "receive" ? "active" : ""} onClick={() => setTab("receive")}>Receive against PO</button>
        <button className={tab === "register" ? "active" : ""} onClick={() => setTab("register")}>GRN register</button>
        <button className={tab === "discrepancies" ? "active" : ""} onClick={() => setTab("discrepancies")}>Discrepancies</button>
      </div>

      {tab === "receive" && (!can("receive") ? <Notice kind="crit">Your role cannot receive material.</Notice> : (
        <div className="split wide">
          <Card title="1 · What is arriving" sub="Receipt is validated against the open purchase order">
            <div className="form">
              <div className="field">
                <label>Receiving store</label>
                <select value={storeId ?? ""} onChange={(e) => { setStoreId(e.target.value ? +e.target.value : null); setLocId(null); }}>
                  <option value="">— choose a store —</option>
                  {stores.data!.filter((s) => s.active).map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
                </select>
              </div>
              <div className="field">
                <label>Purchase order</label>
                <select value={poId ?? ""} onChange={(e) => { setPoId(e.target.value ? +e.target.value : null); setQtys({}); }}>
                  <option value="">— choose an open PO —</option>
                  {pos.data!.map((p) => <option key={p.id} value={p.id}>{p.number} — {p.vendorName}</option>)}
                </select>
              </div>
              <div className="field">
                <label>Physical condition of the consignment <span className="hint">(FR-INV-005)</span></label>
                <select value={condition} onChange={(e) => setCondition(e.target.value)}>
                  <option value="">— not recorded —</option>
                  {(config?.receiptConditions ?? []).map((c) => <option key={c}>{c}</option>)}
                </select>
              </div>
              {store && <div className="field">
                <label>Put the material into</label>
                <select value={place?.id ?? ""} onChange={(e) => setLocId(e.target.value ? +e.target.value : null)}>
                  {places.map((l) => <option key={l.id} value={l.id}>{l.fullCode}{l.kind === "Receiving" ? " — Receiving area (put away later)" : ""}</option>)}
                </select>
                <span className="hint">A stock bin makes it available for issue straight away.</span>
              </div>}
              <div className="frow">
                <div className="field"><label>Delivery challan</label><input value={challan} onChange={(e) => setChallan(e.target.value)} placeholder="DC-4471" /></div>
                <div className="field"><label>Invoice no.</label><input value={invoice} onChange={(e) => setInvoice(e.target.value)} placeholder="INV-2026-0912" /></div>
              </div>
              <div className="field"><label>Invoice date</label><input type="date" value={invDate} onChange={(e) => setInvDate(e.target.value)} /></div>
              <div className="field"><label>Remarks</label><textarea value={remarks} onChange={(e) => setRemarks(e.target.value)} placeholder="Anything worth recording about this consignment" /></div>
            </div>

          </Card>

          <div className="grid">
            <Card title="2 · Quantities received" sub="Enter what arrived against each PO line — no scanning. Damaged or wrong items go in Rejected with a reason; a discrepancy note goes to Procurement automatically.">
              {!po ? <Notice kind="info">Choose the store and the purchase order.</Notice> : <>
                <Table rows={po.lines.length} cols={["Item", { t: "Balance", c: "r" }, { t: "Received now", c: "r" }, { t: "Rejected", c: "r" }]}>
                  {po.lines.map((l) => {
                    const bal = l.qty - l.received; const q = qtys[l.articleId ?? -1] ?? blank;
                    return (
                      <tr key={l.id}>
                        <td><div className="two-line"><b className="mono">{l.itemCode ?? "— no article yet —"}</b><small>{l.description} · ordered {qty(l.qty)} {l.uom}, received {qty(l.received)}</small></div>
                          {num(q.rejected) > 0 && <div className="frow" style={{ marginTop: 6 }}>
                            <select value={q.reason} onChange={(e) => set(l.articleId!, { reason: e.target.value })}><option value="">— reason for rejection —</option>{(config?.rejectReasons ?? []).map((r) => <option key={r}>{r}</option>)}</select>
                            <input value={q.remarks} onChange={(e) => set(l.articleId!, { remarks: e.target.value })} placeholder="Remarks (optional)" />
                            <input type="file" accept="image/*" capture="environment" title="Photograph (optional)" onChange={async (e) => { const f = e.target.files?.[0]; set(l.articleId!, { photo: f ? await toB64(f) : null }); }} />
                          </div>}
                        </td>
                        <td className="r num"><b className={bal > 0 ? "watch" : ""}>{qty(bal)}</b></td>
                        <td className="r">{l.articleId && bal > 0 ? <input type="number" min="0" max={bal} step="any" style={{ width: 90, textAlign: "right" }} value={q.qty} placeholder="0" onChange={(e) => set(l.articleId!, { qty: e.target.value })} /> : <span className="tiny muted">{l.articleId ? "complete" : "create the article first"}</span>}</td>
                        <td className="r">{l.articleId && bal > 0 ? <input type="number" min="0" max={bal} step="any" style={{ width: 80, textAlign: "right" }} value={q.rejected} placeholder="0" onChange={(e) => set(l.articleId!, { rejected: e.target.value })} /> : null}</td>
                      </tr>
                    );
                  })}
                </Table>
                {poLines.some((l) => l.qty - l.received > 0) && <div className="actions" style={{ marginTop: 8 }}>
                  <button className="btn small" onClick={() => setQtys(Object.fromEntries(poLines.filter((l) => l.qty - l.received > 0).map((l) => [l.articleId!, { ...blank, qty: String(l.qty - l.received) }])))}>Fill full balance</button>
                  <button className="btn small" onClick={() => setQtys({})}>Clear</button>
                </div>}
              </>}
            </Card>

            <Card title="3 · Post the goods receipt" sub="Stock and the quantity ledger are updated; the GRN is numbered">
              <div className="actions">
                <button className="btn primary" disabled={busy || entered.length === 0 || missingReason} onClick={postGrn}>
                  {busy ? "Posting…" : `Post GRN — ${qty(totalIn)} received${totalRej ? `, ${qty(totalRej)} rejected` : ""}`}
                </button>
                <button className="btn" onClick={() => setDNote(true)} disabled={!poId}>Raise discrepancy / short-receipt note</button>
              </div>
              {missingReason && <Notice kind="watch">Choose a reason for each rejected quantity.</Notice>}
              {totalIn > 0 && <Notice kind="info">{qty(totalIn)} units, value {cur} {money(totalValue)} → <b className="mono">{place?.fullCode ?? "the store"}</b></Notice>}
            </Card>
          </div>
        </div>
      ))}

      {tab === "register" && (
        <Card flush title="Goods receipt register" sub="Every GRN posted, newest first" actions={<button className="btn small" onClick={grns.reload}>Refresh</button>}>
          {grns.loading ? <Loading /> : grns.error ? <ErrorBox error={grns.error} /> : (
            <Table rows={grns.data!.length} empty="No goods receipts posted yet." cols={["GRN", "Posted", "Store", "PO / vendor", "Challan", { t: "Qty", c: "r" }, { t: "Value", c: "r" }, ""]}>
              {grns.data!.map((g) => (
                <tr key={g.id} className="row-link" onClick={() => setDetail(g)}>
                  <td className="mono">{g.number}{g.reversed && <> <Chip tone="muted">reversed</Chip></>}</td>
                  <td className="tiny">{dateTime(g.postedAt)}<div className="tiny">{g.postedBy}</div></td>
                  <td>{g.storeCode}</td>
                  <td><div className="two-line"><b className="mono">{g.poNumber}</b><small>{g.vendorName}</small></div></td>
                  <td className="tiny">{g.challanNo ?? "—"}</td>
                  <td className="r num">{qty(g.qty)}</td>
                  <td className="r num">{cur} {money(g.value)}</td>
                  <td><span className="linky">Open</span></td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      )}

      {tab === "discrepancies" && (
        <Card flush title="Discrepancy and short-receipt notes" sub="Routed to Procurement to pursue with the vendor (FR-GR-06)"
          actions={<button className="btn small" onClick={discs.reload}>Refresh</button>}>
          {discs.loading ? <Loading /> : discs.error ? <ErrorBox error={discs.error} /> : (
            <Table rows={discs.data!.length} empty="No discrepancies raised." cols={["Note", "Raised", "PO / vendor", "Item", "Kind", { t: "Qty", c: "r" }, "Reason", "Status", ""]}>
              {discs.data!.map((d) => (
                <tr key={d.id}>
                  <td className="mono">{d.number}</td>
                  <td className="tiny">{date(d.raisedAt)}</td>
                  <td><div className="two-line"><b className="mono">{d.poNumber}</b><small>{d.vendorName}</small></div></td>
                  <td className="mono tiny">{d.itemCode ?? "—"}</td>
                  <td><Chip tone={d.kind === "Short" ? "crit" : d.kind === "Excess" ? "watch" : "muted"}>{d.kind}</Chip></td>
                  <td className="r num">{qty(d.qty)}</td>
                  <td style={{ maxWidth: 280 }}>{d.reason}</td>
                  <td><Chip tone={d.status === "Open" ? "watch" : "ok"}>{d.status}</Chip></td>
                  <td>{d.status === "Open" && can("procure") && <ResolveButton id={d.id} onDone={discs.reload} />}</td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      )}

      <Modal open={!!detail} onClose={() => setDetail(null)} title={detail?.number ?? ""} sub={`${detail?.poNumber} · ${detail?.vendorName}`}
        footer={<><span className="tiny">Posted by {detail?.postedBy} on {dateTime(detail?.postedAt)}</span><span className="spacer" />
          {detail && !detail.reversed && can("approve") && <ReverseButton id={detail.id} onDone={() => { setDetail(null); grns.reload(); }} />}
          {detail && <AmendButton entityType="grn" entityRef={detail.number} current={{ ChallanNo: detail.challanNo, InvoiceNo: detail.invoiceNo, InvoiceDate: detail.invoiceDate, Remarks: detail.remarks }} />}
          <button className="btn" onClick={() => setDetail(null)}>Close</button></>}>
        <div className="kv">
          <div><span>Store</span><b>{detail?.storeCode}</b></div>
          <div><span>Challan</span><b>{detail?.challanNo ?? "—"}</b></div>
          <div><span>Invoice</span><b>{detail?.invoiceNo ?? "—"}</b></div>
          <div><span>Invoice date</span><b>{date(detail?.invoiceDate)}</b></div>
          <div><span>Quantity</span><b className="num">{qty(detail?.qty)}</b></div>
          <div><span>Value</span><b className="num">{cur} {money(detail?.value)}</b></div>
          <div><span>Condition</span><b>{detail?.condition ?? "—"}</b></div>
          <div><span>Rejected at receipt</span><b className="num">{int(detail?.rejectedCount)}</b></div>
        </div>
        {detail?.remarks && <Notice kind="info">{detail.remarks}</Notice>}
        <Table rows={byItemCode(items.data ?? []).length} cols={["Item code", "Description", { t: "Qty", c: "r" }, { t: "Value", c: "r" }, "Status", "Location"]}>
          {byItemCode(items.data ?? []).map((l) => (
            <tr key={l.itemCode}>
              <td className="mono">{l.itemCode}</td>
              <td className="tiny">{l.description}</td>
              <td className="r num">{qty(l.qty)} {l.uom}</td>
              <td className="r num">{cur} {money(l.value)}</td>
              <td>{l.status}</td>
              <td className="mono tiny">{l.location || "—"}</td>
            </tr>
          ))}
        </Table>
      </Modal>

      <DiscrepancyModal open={dNote} onClose={() => setDNote(false)} poId={poId} po={po} onDone={() => { setDNote(false); discs.reload(); toast("Discrepancy note raised and routed to Procurement"); }} />
    </>
  );
}

function ResolveButton({ id, onDone }: { id: number; onDone: () => void }) {
  const toast = useToast();
  const [open, setOpen] = useState(false); const [text, setText] = useState("");
  return (<>
    <button className="btn small" onClick={(e) => { e.stopPropagation(); setOpen(true); }}>Resolve</button>
    <Modal open={open} onClose={() => setOpen(false)} size="narrow" title="Resolve discrepancy"
      footer={<><button className="btn primary" disabled={!text.trim()} onClick={async () => {
        try { await post(`/api/grn/discrepancies/${id}/resolve`, { resolution: text }); toast("Resolved"); setOpen(false); onDone(); } catch (e) { toast(errText(e), true); }
      }}>Save</button><button className="btn" onClick={() => setOpen(false)}>Cancel</button></>}>
      <div className="field"><label>How was it settled?</label><textarea value={text} onChange={(e) => setText(e.target.value)} placeholder="Replacement delivered on 12-09-2026 against DC-4488 / credit note issued / penalty applied" /></div>
    </Modal>
  </>);
}

function ReverseButton({ id, onDone }: { id: number; onDone: () => void }) {
  const toast = useToast();
  const [open, setOpen] = useState(false); const [text, setText] = useState("");
  return (<>
    <button className="btn danger" onClick={() => setOpen(true)}>Reverse GRN</button>
    <Modal open={open} onClose={() => setOpen(false)} size="narrow" title="Reverse this goods receipt"
      footer={<><button className="btn danger" disabled={!text.trim()} onClick={async () => {
        try { await post(`/api/grn/${id}/reverse`, { reason: text }); toast("GRN reversed"); setOpen(false); onDone(); } catch (e) { toast(errText(e), true); }
      }}>Reverse</button><button className="btn" onClick={() => setOpen(false)}>Cancel</button></>}>
      <Notice kind="crit">Only possible while none of the received items have been reserved, issued or moved. The original entry stays visible in the audit trail (BR-022).</Notice>
      <div className="field"><label>Reason</label><textarea value={text} onChange={(e) => setText(e.target.value)} placeholder="Wrong PO selected at the gate" /></div>
    </Modal>
  </>);
}

function DiscrepancyModal({ open, onClose, poId, po, onDone }: { open: boolean; onClose: () => void; poId: number | null; po: PoDto | null; onDone: () => void }) {
  const toast = useToast();
  const [kind, setKind] = useState("Short"); const [q, setQ] = useState(""); const [articleId, setArticleId] = useState<number | null>(null); const [reason, setReason] = useState("");
  return (
    <Modal open={open} onClose={onClose} size="narrow" title="Discrepancy / short-receipt note" sub={po?.number}
      footer={<><button className="btn primary" disabled={!poId || !q || !reason.trim()} onClick={async () => {
        try {
          await post("/api/grn/discrepancies", { purchaseOrderId: poId, articleId, grnId: null, kind, qty: +q, reason });
          setQ(""); setReason(""); onDone();
        } catch (e) { toast(errText(e), true); }
      }}>Raise note</button><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>Kind</label><select value={kind} onChange={(e) => setKind(e.target.value)}><option>Short</option><option>Excess</option><option>Rejected</option></select></div>
          <div className="field"><label>Quantity</label><input type="number" min="0" step="0.001" value={q} onChange={(e) => setQ(e.target.value)} /></div>
        </div>
        <div className="field"><label>Article (optional)</label>
          <select value={articleId ?? ""} onChange={(e) => setArticleId(e.target.value ? +e.target.value : null)}>
            <option value="">— whole consignment —</option>
            {(po?.lines ?? []).filter((l) => l.articleId).map((l) => <option key={l.articleId!} value={l.articleId!}>{l.itemCode} — {l.description}</option>)}
          </select>
        </div>
        <div className="field"><label>Reason</label><textarea value={reason} onChange={(e) => setReason(e.target.value)} placeholder="20 luminaires short against DC-4471; vendor informed at the gate" /></div>
      </div>
    </Modal>
  );
}
