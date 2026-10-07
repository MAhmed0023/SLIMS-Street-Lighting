"use client";
import { useState } from "react";
import { get } from "@/lib/api";
import type { ItemDetailDto, ItemDto, StockSummaryDto, StoreDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { date, dateTime, int, money, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Kpi, Loading, Modal, Notice, StatusChip, Table, useAsync } from "@/components/ui";

export default function StockPage() {
  const { config } = useAuth();
  const [storeId, setStoreId] = useState<number | null>(null);
  const [q, setQ] = useState("");
  const [locId, setLocId] = useState<number | null>(null);
  const [item, setItem] = useState<ItemDetailDto | null>(null);
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const stock = useAsync(() => get<StockSummaryDto>(`/api/stock${storeId ? `?storeId=${storeId}` : ""}${q ? `${storeId ? "&" : "?"}q=${encodeURIComponent(q)}` : ""}`), [storeId, q]);
  const locItems = useAsync(() => locId ? get<ItemDto[]>(`/api/stock/location/${locId}`) : Promise.resolve([]), [locId]);
  const cur = config?.app.currency ?? "Rs.";

  if (stock.loading && !stock.data) return <Loading what="stock position" />;
  if (stock.error) return <ErrorBox error={stock.error} />;
  const s = stock.data!;
  const ageThreshold = config?.controls.ageingDays ?? 180;

  return (
    <>
      <div className="filters">
        <select value={storeId ?? ""} onChange={(e) => setStoreId(e.target.value ? +e.target.value : null)}>
          <option value="">All stores in your scope</option>
          {(stores.data ?? []).map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
        </select>
        <input type="search" placeholder="Search item code, description or bin…" value={q} onChange={(e) => setQ(e.target.value)} />
        <span className="spacer" />
        <button className="btn small" onClick={stock.reload}>Refresh</button>
      </div>

      <div className="grid g-kpi">
        <Kpi label="Stock value" value={`${cur} ${money(s.totalValue)}`} sub={`${int(s.items)} labels in stock locations`} />
        <Kpi label="Quantity on hand" value={qty(s.totalQty)} sub={`${s.articles} article(s)`} />
        <Kpi label="Below re-order level" value={int(s.belowReorder.length)} tone={s.belowReorder.length ? "watch" : "good"} sub="replenishment needed" />
        <Kpi label={`Held over ${ageThreshold} days`} value={int(s.rows.filter((r) => r.oldestDays > ageThreshold && r.locationKind === "Stock").length)} tone="watch" sub="lines with ageing stock" />
      </div>

      {s.belowReorder.length > 0 && <Notice kind="watch"><span>
        <b>{s.belowReorder.length} article(s)</b> are at or below their re-order level: {s.belowReorder.slice(0, 5).map((a) => a.itemCode).join(", ")}{s.belowReorder.length > 5 ? " …" : ""}
      </span></Notice>}

      <Card flush title="Stock by location" sub="Store ▸ section ▸ rack ▸ bin. Quarantine and scrap bins are shown separately from issuable stock.">
        <Table rows={s.rows.length} empty="No stock in this view." cols={["Store", "Location", "Item code", { t: "On hand", c: "r" }, { t: "Reserved", c: "r" }, { t: "Available", c: "r" }, { t: "Value", c: "r" }, { t: "Oldest", c: "r" }, "Kind"]}>
          {s.rows.map((r) => (
            <tr key={`${r.locationId}-${r.articleId}`} className="row-link" onClick={() => setLocId(r.locationId)}>
              <td>{r.storeCode}</td>
              <td className="mono tiny">{r.locationCode}</td>
              <td><div className="two-line"><b className="mono">{r.itemCode}</b><small>{r.description}</small></div></td>
              <td className="r num">{qty(r.onHand)} {r.uom}</td>
              <td className="r num">{r.reserved > 0 ? qty(r.reserved) : "—"}</td>
              <td className="r num"><b>{qty(r.available)}</b></td>
              <td className="r num">{cur} {money(r.value)}</td>
              <td className="r num">{r.oldestDays > ageThreshold ? <span className="watch">{r.oldestDays} d</span> : `${r.oldestDays} d`}</td>
              <td>{r.locationKind === "Stock" ? <Chip tone="ok">Stock</Chip> : r.locationKind === "Quarantine" ? <Chip tone="watch">Quarantine</Chip> : <Chip tone="muted">Scrap</Chip>}</td>
            </tr>
          ))}
        </Table>
      </Card>

      <Modal open={!!locId} onClose={() => setLocId(null)} title="Items at this location" sub={s.rows.find((r) => r.locationId === locId)?.locationCode}
        footer={<><span className="tiny">Click any label to see its full history.</span><span className="spacer" /><button className="btn" onClick={() => setLocId(null)}>Close</button></>}>
        <Table rows={locItems.data?.length ?? 0} cols={["Item code", { t: "Serial", c: "r" }, { t: "Qty", c: "r" }, "Status", "PO", "Received", "QR payload"]}>
          {(locItems.data ?? []).map((i) => (
            <tr key={i.id} className="row-link" onClick={() => get<ItemDetailDto>(`/api/items/${i.id}`).then(setItem)}>
              <td className="mono">{i.itemCode}</td>
              <td className="r num">{i.serial}</td>
              <td className="r num">{qty(i.qty)} {i.uom}</td>
              <td><StatusChip s={i.status} /></td>
              <td className="mono tiny">{i.poNumber}</td>
              <td className="tiny">{date(i.receivedAt)}</td>
              <td className="mono tiny">{i.qrCode}</td>
            </tr>
          ))}
        </Table>
      </Modal>

      <ItemModal d={item} onClose={() => setItem(null)} cur={cur} />
    </>
  );
}

export function ItemModal({ d, onClose, cur }: { d: ItemDetailDto | null; onClose: () => void; cur: string }) {
  if (!d) return null;
  const i = d.item;
  return (
    <Modal open onClose={onClose} title={`${i.itemCode} #${i.serial}`} sub={i.description}
      chips={<><StatusChip s={i.status} /><Chip tone="plain">PO {i.poNumber}</Chip><Chip tone="plain">{i.vendorName}</Chip></>}
      footer={<><span className="tiny mono">{i.qrCode}</span><span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="kv">
        <div><span>Quantity</span><b className="num">{qty(i.qty)} {i.uom}</b></div>
        <div><span>Rate</span><b className="num">{cur} {money(i.rate, 2)}</b></div>
        <div><span>Store location</span><b className="mono tiny">{i.locationCode ?? "—"}</b></div>
        <div><span>Site</span><b>{i.siteCode ?? "—"}</b></div>
        <div><span>Received</span><b>{dateTime(i.receivedAt)}</b></div>
        <div><span>Issued</span><b>{dateTime(i.issuedAt)}</b></div>
        <div><span>Installed</span><b>{dateTime(i.installedAt)}</b></div>
        <div><span>Returned</span><b>{dateTime(i.returnedAt)}</b></div>
        <div><span>Warranty</span><b>{i.warrantyMonths} months{i.warrantyExpires ? ` · to ${date(i.warrantyExpires)}` : ""}</b></div>
        <div><span>Label prints</span><b className="num">{i.labelPrints}</b></div>
      </div>

      {d.installation && <Notice kind={d.installation.geoStatus === "Confirmed" ? "good" : "watch"}>
        <span>Installed at <b>{d.installation.siteCode}</b>{d.installation.poleId ? ` pole ${d.installation.poleId}` : ""} — {Math.round(d.installation.distanceM)} m from the site point, {d.installation.geoStatus}.</span>
      </Notice>}
      {d.return && <Notice kind="crit"><span>Return <b className="mono">{d.return.number}</b> — {d.return.reason} ({d.return.classification}).</span></Notice>}

      <Card title="Full movement history" sub="Every scan, decision and status change, in order">
        <Table rows={d.history.length} cols={["When", "Action", "By", "Detail"]}>
          {d.history.map((h, n) => (
            <tr key={n}>
              <td className="tiny nowrap">{dateTime(h.at)}</td>
              <td><b>{h.action}</b></td>
              <td className="tiny">{h.by ?? "—"}{h.role ? ` (${h.role})` : ""}</td>
              <td className="tiny" style={{ maxWidth: 420 }}>
                {h.reason && <div>{h.reason}</div>}
                {h.nextJson && <span className="mono tiny">{h.nextJson}</span>}
              </td>
            </tr>
          ))}
        </Table>
      </Card>
    </Modal>
  );
}
