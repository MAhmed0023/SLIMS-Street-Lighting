"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import type { ItemDto, ScanLookupDto, StoreDto, TransferDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { dateTime, int, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { ScanBox } from "@/components/Controls";

/** FR-INV-019: a despatch leg and a receipt leg, both by scan; between them the stock shows as in transit. */
export default function TransfersPage() {
  const { can } = useAuth();
  const toast = useToast();
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const list = useAsync(() => get<TransferDto[]>("/api/transfers"), []);
  const [from, setFrom] = useState<number | null>(null);
  const [to, setTo] = useState<number | null>(null);
  const [vehicle, setVehicle] = useState(""); const [remarks, setRemarks] = useState("");
  const [basket, setBasket] = useState<ItemDto[]>([]);
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState<TransferDto | null>(null);

  const scan = async (qr: string) => {
    try {
      const r = await get<ScanLookupDto>(`/api/stock-control/lookup?qr=${encodeURIComponent(qr)}`);
      if (r.kind !== "item" || !r.item) { setMsg({ ok: false, text: r.message }); return; }
      const i = r.item;
      if (i.status !== "Received" || i.storeId !== from) { setMsg({ ok: false, text: `${i.itemCode} #${i.serial} is ${i.status}${i.storeCode ? " at " + i.storeCode : ""}; only unreserved stock of the despatching store can go.` }); return; }
      if (basket.some((b) => b.id === i.id)) { setMsg({ ok: false, text: "Already in the list." }); return; }
      setBasket((b) => [i, ...b]); setMsg({ ok: true, text: r.message });
    } catch (e) { setMsg({ ok: false, text: errText(e) }); }
  };
  const despatch = async () => {
    setBusy(true);
    try {
      const t = await post<TransferDto>("/api/transfers", { fromStoreId: from, toStoreId: to, qrCodes: basket.map((b) => b.qrCode), vehicle: vehicle || null, remarks: remarks || null });
      toast(`${t.number} despatched — ${t.itemCount} item(s) in transit`); setBasket([]); setVehicle(""); setRemarks(""); list.reload();
    } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };

  if (stores.loading) return <Loading what="stores" />;
  if (stores.error) return <ErrorBox error={stores.error} />;
  const active = stores.data!.filter((s) => s.active);
  return (
    <>
      {can("issue") && <div className="split wide">
        <Card title="Despatch leg" sub="Scan the stock leaving this store. It shows as in transit until the other store scans it in.">
          <div className="form">
            <div className="frow">
              <div className="field"><label>From store</label><select value={from ?? ""} onChange={(e) => { setFrom(e.target.value ? +e.target.value : null); setBasket([]); }}><option value="">—</option>{active.map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}</select></div>
              <div className="field"><label>To store</label><select value={to ?? ""} onChange={(e) => setTo(e.target.value ? +e.target.value : null)}><option value="">—</option>{active.filter((s) => s.id !== from).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}</select></div>
            </div>
            <div className="frow">
              <div className="field"><label>Vehicle</label><input value={vehicle} onChange={(e) => setVehicle(e.target.value)} placeholder="LES-1234" /></div>
              <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} /></div>
            </div>
          </div>
          <ScanBox onScan={scan} disabled={!from || !to} placeholder={!from || !to ? "Choose both stores first" : "Scan each item being despatched"} />
          {msg && <Notice kind={msg.ok ? "good" : "crit"}>{msg.text}</Notice>}
          <Table rows={basket.length} empty="Nothing scanned yet." cols={["Item", "Bin", { t: "Qty", c: "r" }, ""]}>
            {basket.map((i) => <tr key={i.id}><td className="mono">{i.itemCode} #{i.serial}</td><td className="mono tiny">{i.locationCode}</td><td className="r num">{qty(i.qty)}</td><td><button className="btn small" onClick={() => setBasket((b) => b.filter((x) => x.id !== i.id))}>×</button></td></tr>)}
          </Table>
          <div className="actions" style={{ marginTop: 10 }}><button className="btn primary" disabled={busy || basket.length === 0} onClick={despatch}>Despatch {basket.length} item(s)</button></div>
        </Card>
        <Card title="How it works">
          <ol className="steps">
            <li>The despatching store scans each item. Stock leaves its books and shows as <b>in transit</b>.</li>
            <li>The receiving store opens the transfer below and scans each item as it arrives.</li>
            <li>Received items land in the receiving store&apos;s Receiving area, ready to be put away.</li>
          </ol>
        </Card>
      </div>}

      <Card flush title="Transfers" sub="Newest first" actions={<button className="btn small" onClick={list.reload}>Refresh</button>}>
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No transfers yet." cols={["Transfer", "Despatched", "From → to", { t: "Labels", c: "r" }, { t: "In transit", c: "r" }, "Status", ""]}>
            {list.data!.map((t) => (
              <tr key={t.id} className="row-link" onClick={async () => setOpen(await get<TransferDto>(`/api/transfers/${t.id}`))}>
                <td className="mono">{t.number}</td>
                <td className="tiny">{dateTime(t.despatchedAt)}<div className="tiny">{t.despatchedBy}</div></td>
                <td>{t.fromStoreCode} → {t.toStoreCode}</td>
                <td className="r num">{int(t.itemCount)}</td>
                <td className="r num">{t.inTransit > 0 ? <b className="watch">{t.inTransit}</b> : 0}</td>
                <td><Chip tone={t.status === "Received" ? "ok" : "watch"}>{t.status === "InTransit" ? "In transit" : t.status === "PartReceived" ? "Part received" : t.status}</Chip></td>
                <td><span className="linky">Open</span></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      {open && <ReceiveModal t={open} onClose={() => setOpen(null)} onDone={(t) => { setOpen(t); list.reload(); }} canReceive={can("receive")} />}
    </>
  );
}

function ReceiveModal({ t, onClose, onDone, canReceive }: { t: TransferDto; onClose: () => void; onDone: (t: TransferDto) => void; canReceive: boolean }) {
  const toast = useToast();
  const [scanned, setScanned] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const pending = t.items.filter((i) => i.status === "InTransit");
  const scan = (qr: string) => {
    const hit = pending.find((i) => i.qrCode === qr);
    if (!hit) { toast("That label is not in transit on this transfer.", true); return; }
    setScanned((s) => s.includes(qr) ? s : [...s, qr]);
  };
  return (
    <Modal open onClose={onClose} size="wide" title={t.number} sub={`${t.fromStoreCode} → ${t.toStoreCode} · despatched ${dateTime(t.despatchedAt)} by ${t.despatchedBy}`}
      footer={<>{canReceive && pending.length > 0 && <button className="btn primary" disabled={busy || scanned.length === 0} onClick={async () => {
        setBusy(true);
        try { const r = await post<TransferDto>(`/api/transfers/${t.id}/receive`, { qrCodes: scanned }); toast(`${scanned.length} item(s) received`); setScanned([]); onDone(r); }
        catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Receive {scanned.length} scanned item(s)</button>}<span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      {canReceive && pending.length > 0 && <ScanBox onScan={scan} placeholder="Receipt leg — scan each arriving item" />}
      <Table rows={t.items.length} cols={["Item", "Status", "Now at", ""]}>
        {t.items.map((i) => <tr key={i.id}>
          <td className="mono">{i.itemCode} #{i.serial}</td>
          <td><Chip tone={i.status === "InTransit" ? "watch" : "ok"}>{i.status === "InTransit" ? "In transit" : i.status}</Chip></td>
          <td className="mono tiny">{i.locationCode ?? "—"}</td>
          <td>{scanned.includes(i.qrCode) && <Chip tone="ok">scanned</Chip>}</td>
        </tr>)}
      </Table>
    </Modal>
  );
}
