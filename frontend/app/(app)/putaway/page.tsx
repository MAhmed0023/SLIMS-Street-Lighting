"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import type { BinLabel, ItemDto, MoveResultDto, ScanLookupDto, StoreDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { dateTime, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { ScanBox } from "@/components/Controls";

/** FR-INV-010/012: goods received wait in the Receiving area until they are put away; any move between bins is made by
 * scanning the items and then the destination bin label. */
export default function PutawayPage() {
  const { can } = useAuth();
  const toast = useToast();
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const [storeId, setStoreId] = useState<number | null>(null);
  const receiving = useAsync(() => get<ItemDto[]>(`/api/stock-control/receiving${storeId ? `?storeId=${storeId}` : ""}`), [storeId]);
  const labels = useAsync(() => storeId ? get<BinLabel[]>(`/api/stores/${storeId}/labels`) : Promise.resolve([] as BinLabel[]), [storeId]);
  const [basket, setBasket] = useState<ItemDto[]>([]);
  const [bin, setBin] = useState<string>("");
  const [busy, setBusy] = useState(false);
  const [last, setLast] = useState<{ ok: boolean; text: string } | null>(null);
  const [print, setPrint] = useState(false);
  const allowed = can("receive") || can("issue");

  const move = async (toLocationQr?: string, toLocationId?: number) => {
    if (basket.length === 0) { toast("Scan the items first, then the bin.", true); return; }
    setBusy(true);
    try {
      const r = await post<MoveResultDto>("/api/stock-control/move", { qrCodes: basket.map((i) => i.qrCode), toLocationQr: toLocationQr ?? null, toLocationId: toLocationId ?? null });
      toast(`${r.moved} item(s) now in ${r.toLocation}`); setLast({ ok: true, text: `${r.moved} item(s) moved to ${r.toLocation}` });
      setBasket([]); setBin(""); receiving.reload(); stores.reload();
    } catch (e) { setLast({ ok: false, text: errText(e) }); toast(errText(e), true); } finally { setBusy(false); }
  };

  const scan = async (qr: string) => {
    try {
      const r = await get<ScanLookupDto>(`/api/stock-control/lookup?qr=${encodeURIComponent(qr)}`);
      if (r.kind === "location" && r.location) { await move(qr); return; }
      if (r.kind === "item" && r.item) {
        if (basket.some((b) => b.id === r.item!.id)) { setLast({ ok: false, text: "Already in the list." }); return; }
        if (!r.item.locationCode) { setLast({ ok: false, text: `${r.item.itemCode} #${r.item.serial} is ${r.item.status} and not in a store.` }); return; }
        setBasket((b) => [r.item!, ...b]); setLast({ ok: true, text: r.message }); return;
      }
      setLast({ ok: false, text: r.message });
    } catch (e) { setLast({ ok: false, text: errText(e) }); }
  };

  if (stores.loading) return <Loading what="stores" />;
  if (stores.error) return <ErrorBox error={stores.error} />;
  const store = stores.data!.find((s) => s.id === storeId) ?? null;
  const bins = (store?.locations ?? []).filter((l) => l.active && l.kind !== "Receiving");

  return (
    <>
      <div className="filters">
        <select value={storeId ?? ""} onChange={(e) => { setStoreId(e.target.value ? +e.target.value : null); setBasket([]); }}>
          <option value="">All my stores</option>
          {stores.data!.map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
        </select>
        <span className="spacer" />
        {storeId && <button className="btn" onClick={() => setPrint(true)}>Print bin labels</button>}
      </div>

      <div className="split wide">
        <Card title="Scan items, then the bin" sub="Scanning a bin label moves every item in the list into that bin. Both locations go to the stock ledger.">
          {!allowed ? <Notice kind="crit">Your role cannot move stock.</Notice> : <>
            <ScanBox onScan={scan} busy={busy} placeholder="Scan an item label, or a bin label to put the list away" />
            {last && <Notice kind={last.ok ? "good" : "crit"}>{last.text}</Notice>}
            <Table rows={basket.length} empty="Nothing scanned yet." cols={["Item", "From", "Status", ""]}>
              {basket.map((i) => (
                <tr key={i.id}>
                  <td><div className="two-line"><b className="mono">{i.itemCode} #{i.serial}</b><small>{i.description}</small></div></td>
                  <td className="mono tiny">{i.locationCode}</td>
                  <td><Chip tone="info">{i.status}</Chip></td>
                  <td><button className="btn small" onClick={() => setBasket((b) => b.filter((x) => x.id !== i.id))}>×</button></td>
                </tr>
              ))}
            </Table>
            {basket.length > 0 && store && <div className="actions" style={{ marginTop: 10 }}>
              <select value={bin} onChange={(e) => setBin(e.target.value)} aria-label="Destination bin">
                <option value="">— or choose the bin —</option>
                {bins.map((l) => <option key={l.id} value={l.id}>{l.fullCode} ({l.kind})</option>)}
              </select>
              <button className="btn primary" disabled={busy || !bin} onClick={() => move(undefined, +bin)}>Move {basket.length} item(s)</button>
            </div>}
          </>}
        </Card>

        <Card flush title="Waiting in the Receiving area" sub="Received on a GRN, not yet available for requisition or issue (FR-INV-010)" actions={<button className="btn small" onClick={receiving.reload}>Refresh</button>}>
          {receiving.loading ? <Loading /> : receiving.error ? <ErrorBox error={receiving.error} /> : (
            <Table rows={receiving.data!.length} empty="Nothing waiting — every receipt has been put away." cols={["Item", "Store", { t: "Qty", c: "r" }, "Received", ""]}>
              {receiving.data!.map((i) => (
                <tr key={i.id}>
                  <td><div className="two-line"><b className="mono">{i.itemCode} #{i.serial}</b><small>{i.poNumber} · {i.description}</small></div></td>
                  <td className="mono tiny">{i.locationCode}</td>
                  <td className="r num">{qty(i.qty)} {i.uom}</td>
                  <td className="tiny">{dateTime(i.receivedAt)}</td>
                  <td>{allowed && !basket.some((b) => b.id === i.id) && <button className="btn small" onClick={() => setBasket((b) => [i, ...b])}>Add</button>}</td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      </div>

      {print && <div className="back" onClick={() => setPrint(false)} />}
      {print && <div className="modal-wrap"><div className="modal wide">
        <div className="modal-head"><div><h3>Bin labels — {store?.code}</h3></div><button className="close" onClick={() => setPrint(false)}>×</button></div>
        <div className="modal-body">
          <div className="bin-labels">
            {(labels.data ?? []).map((l) => <div className="bin-label" key={l.id}>
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img src={`/api/items/qr.png?payload=${encodeURIComponent(l.payload)}&size=5`} alt="" />
              <b>{l.fullCode}</b><span className="tiny">{l.kind}</span>
            </div>)}
          </div>
        </div>
        <div className="modal-foot"><button className="btn primary" onClick={() => window.print()}>Print</button><span className="spacer" /><button className="btn" onClick={() => setPrint(false)}>Close</button></div>
      </div></div>}
    </>
  );
}
