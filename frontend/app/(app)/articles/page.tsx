"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import type { ArticleDto, ItemDetailDto, ItemDto, PoDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { date, int, money, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, StatusChip, Table, errText, useAsync, useToast } from "@/components/ui";
import { ItemModal } from "../stock/page";

export default function ArticlesPage() {
  const { config, can } = useAuth();
  const toast = useToast();
  const [q, setQ] = useState("");
  const [labels, setLabels] = useState<{ a: ArticleDto; items: ItemDto[] } | null>(null);
  const [items, setItems] = useState<ArticleDto | null>(null);
  const [create, setCreate] = useState(false);
  const [detail, setDetail] = useState<ItemDetailDto | null>(null);
  const list = useAsync(() => get<ArticleDto[]>(`/api/articles${q ? `?q=${encodeURIComponent(q)}` : ""}`), [q]);
  const rows = useAsync(() => items ? get<ItemDto[]>(`/api/articles/${items.id}/items`) : Promise.resolve([]), [items?.id]);
  const cur = config?.app.currency ?? "Rs.";

  return (
    <>
      <div className="filters">
        <input type="search" placeholder="Search item code, description or PO…" value={q} onChange={(e) => setQ(e.target.value)} />
        <span className="spacer" />
        {(can("procure") || can("configure") || can("receive")) && <button className="btn primary" onClick={() => setCreate(true)}>Create article &amp; QR series</button>}
      </div>

      <Notice kind="info"><span>
        Every item code is created article-wise against one purchase order line and carries that PO reference for life (BR-003, BR-004).
        The QR series is generated once, at creation, and a code is never reused (BR-002).
      </span></Notice>

      <Card flush title="Articles" sub="Item code · specification · PO reference · quantities across the lifecycle">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No articles yet. Create one from a purchase order line."
            cols={["Item code", "Description", "PO / vendor", { t: "Rate", c: "r" }, { t: "Labels", c: "r" }, { t: "Received", c: "r" }, { t: "In stock", c: "r" }, { t: "Installed", c: "r" }, { t: "Quarantine", c: "r" }, "Status", ""]}>
            {list.data!.map((a) => (
              <tr key={a.id}>
                <td><div className="two-line"><b className="mono">{a.itemCode}</b><small>{a.serialised ? "serialised" : `bulk · ${qty(a.packSize)} ${a.uom}/label`}</small></div></td>
                <td style={{ maxWidth: 280 }}>{a.description}</td>
                <td><div className="two-line"><b className="mono">{a.poNumber}</b><small>{a.vendorName}</small></div></td>
                <td className="r num">{cur} {money(a.rate, 2)}</td>
                <td className="r num">{int(a.itemsGenerated)}</td>
                <td className="r num">{qty(a.received)} / {qty(a.poQty)}</td>
                <td className="r num"><b>{qty(a.onHand)}</b>{a.reserved > 0 && <div className="tiny">{qty(a.reserved)} reserved</div>}</td>
                <td className="r num">{qty(a.installed)}</td>
                <td className="r num">{a.quarantine > 0 ? <span className="watch">{qty(a.quarantine)}</span> : "—"}</td>
                <td><Chip tone={a.status === "Active" ? "ok" : a.status === "Blocked" ? "crit" : "muted"}>{a.status}</Chip>
                  {a.reorderLevel > 0 && a.onHand < a.reorderLevel && <div className="tiny watch">below re-order {qty(a.reorderLevel)}</div>}</td>
                <td className="nowrap">
                  <button className="btn small" onClick={() => setItems(a)}>Items</button>{" "}
                  <button className="btn small" onClick={async () => {
                    try { const r = await post<ItemDto[]>(`/api/articles/${a.id}/labels`, undefined); setLabels({ a, items: r }); }
                    catch (e) { toast(errText(e), true); }
                  }}>Labels</button>
                </td>
              </tr>
            ))}
          </Table>
        )}
      </Card>

      {labels && <LabelSheet a={labels.a} items={labels.items} onClose={() => setLabels(null)} />}
      {items && <Modal open onClose={() => setItems(null)} size="wide" title={`${items.itemCode} — individual labels`} sub={items.description}
        footer={<><span className="tiny">{rows.data?.length ?? 0} label(s)</span><span className="spacer" /><button className="btn" onClick={() => setItems(null)}>Close</button></>}>
        <Table rows={rows.data?.length ?? 0} cols={[{ t: "#", c: "r" }, { t: "Qty", c: "r" }, "Status", "Location", "Site", "Received", "Installed", "QR payload"]}>
          {(rows.data ?? []).map((i) => (
            <tr key={i.id} className="row-link" onClick={() => get<ItemDetailDto>(`/api/items/${i.id}`).then(setDetail)}>
              <td className="r num">{i.serial}</td>
              <td className="r num">{qty(i.qty)}</td>
              <td><StatusChip s={i.status} /></td>
              <td className="mono tiny">{i.locationCode ?? "—"}</td>
              <td className="tiny">{i.siteCode ?? "—"}</td>
              <td className="tiny">{date(i.receivedAt)}</td>
              <td className="tiny">{date(i.installedAt)}</td>
              <td className="mono tiny">{i.qrCode}</td>
            </tr>
          ))}
        </Table>
      </Modal>}

      {create && <CreateArticle onClose={() => setCreate(false)} onDone={() => { setCreate(false); list.reload(); }} />}
      <ItemModal d={detail} onClose={() => setDetail(null)} cur={cur} />
    </>
  );
}

function LabelSheet({ a, items, onClose }: { a: ArticleDto; items: ItemDto[]; onClose: () => void }) {
  return (
    <Modal open onClose={onClose} size="wide" title={`QR labels — ${a.itemCode}`} sub={`${items.length} label(s) · ${a.description}`}
      footer={<><button className="btn primary" onClick={() => window.print()}>Print label sheet</button>
        <span className="tiny">Print on weather-resistant synthetic stock; re-prints keep the same code and are logged (FR-MD-14).</span>
        <span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="labels">
        {items.map((i) => (
          <div className="label" key={i.id}>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src={`/api/items/qr.png?payload=${encodeURIComponent(i.qrCode)}&size=5`} alt="" />
            <div className="lt">
              <b>{i.itemCode}</b>
              <span>{a.description}</span>
              <span className="po">{a.poNumber} · #{String(i.serial).padStart(6, "0")} · {qty(i.qty)} {i.uom}</span>
            </div>
          </div>
        ))}
      </div>
    </Modal>
  );
}

function CreateArticle({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const pos = useAsync(() => get<PoDto[]>("/api/purchase-orders?status=Open"), []);
  const [lineId, setLineId] = useState<number | null>(null);
  const [serialised, setSerialised] = useState(true);
  const [pack, setPack] = useState("1");
  const [reorder, setReorder] = useState("0");
  const [life, setLife] = useState("");
  const [busy, setBusy] = useState(false);
  const line = (pos.data ?? []).flatMap((p) => p.lines.map((l) => ({ ...l, po: p }))).find((l) => l.id === lineId);
  const count = line ? (serialised ? Math.ceil(line.qty) : Math.ceil(line.qty / Math.max(+pack || 1, 0.001))) : 0;

  return (
    <Modal open onClose={onClose} title="Create article and generate its QR series" sub="One article per purchase order line"
      footer={<><button className="btn primary" disabled={busy || !lineId || count < 1 || count > 20000} onClick={async () => {
        setBusy(true);
        try {
          const a = await post<ArticleDto>("/api/articles", { purchaseOrderLineId: lineId, serialised, packSize: +pack || 1, reorderLevel: +reorder || 0, expectedLifeMonths: life ? +life : null });
          toast(`${a.itemCode} created — ${a.itemsGenerated} QR labels generated`); onDone();
        } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Create &amp; generate {count > 0 ? `${count} labels` : ""}</button><button className="btn" onClick={onClose}>Cancel</button></>}>
      {pos.loading ? <Loading /> : <div className="form">
        <div className="field"><label>Purchase order line</label>
          <select value={lineId ?? ""} onChange={(e) => setLineId(e.target.value ? +e.target.value : null)}>
            <option value="">— choose a line that has no article yet —</option>
            {(pos.data ?? []).map((p) => p.lines.filter((l) => !l.articleId).map((l) => (
              <option key={l.id} value={l.id}>{p.number} · line {l.lineNo} — {l.description} ({qty(l.qty)} {l.uom})</option>
            )))}
          </select>
        </div>
        {line && <>
          <div className="kv">
            <div><span>Item code will be</span><b className="mono">{line.category}-{line.subCategory}-{line.spec}-PO{line.po.number.replace(/\D/g, "")}-{String(line.lineNo).padStart(3, "0")}</b></div>
            <div><span>Ordered</span><b className="num">{qty(line.qty)} {line.uom}</b></div>
            <div><span>Vendor</span><b>{line.po.vendorName}</b></div>
            <div><span>Warranty</span><b>{line.po.warrantyMonths} months</b></div>
          </div>
          <div className="frow">
            <div className="field"><label>Tracking</label>
              <select value={serialised ? "1" : "0"} onChange={(e) => setSerialised(e.target.value === "1")}>
                <option value="1">Serialised — one QR per piece</option>
                <option value="0">Bulk — one QR per pack or batch</option>
              </select>
            </div>
            {!serialised && <div className="field"><label>Pack size ({line.uom} per label)</label><input type="number" min="1" step="0.001" value={pack} onChange={(e) => setPack(e.target.value)} /></div>}
            <div className="field"><label>Re-order level</label><input type="number" min="0" step="0.001" value={reorder} onChange={(e) => setReorder(e.target.value)} /></div>
            <div className="field"><label>Expected life (months)</label><input type="number" min="0" value={life} onChange={(e) => setLife(e.target.value)} placeholder="60" /></div>
          </div>
          <Notice kind={count > 20000 ? "crit" : "info"}>
            <span>{count > 20000 ? `${int(count)} labels exceeds the 20,000 limit — use a larger pack size.` : `${int(count)} unique QR label(s) will be generated now and can be printed straight away.`}</span>
          </Notice>
        </>}
      </div>}
    </Modal>
  );
}
