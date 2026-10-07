"use client";
import { useState } from "react";
import { get, post, put } from "@/lib/api";
import type { LocationDto, StoreDto, StoreUpsert, UserDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { int, money } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

export default function StoresPage() {
  const { config, can } = useAuth();
  const [edit, setEdit] = useState<StoreDto | "new" | null>(null);
  const [locFor, setLocFor] = useState<StoreDto | null>(null);
  const list = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const cur = config?.app.currency ?? "Rs.";

  return (
    <>
      <div className="filters">
        <span className="spacer" />
        {can("configure") && <button className="btn primary" onClick={() => setEdit("new")}>New store</button>}
      </div>

      <Notice kind="info"><span>
        Stock always sits at a bin, never in a store as a whole (BR-005). Every store also keeps a <b>Quarantine</b> bin for returned material
        and a <b>Scrap</b> bin for condemned items — those are created automatically.
      </span></Notice>

      {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
        <div className="grid g-2">
          {list.data!.map((s) => (
            <Card key={s.id} flush title={<>{s.code} — {s.name}</>} sub={`${s.type} store · ${s.zone} zone · ${s.inChargeName ?? "no in-charge assigned"}`}
              actions={<>
                <Chip tone={s.active ? "ok" : "muted"}>{s.active ? "Active" : "Inactive"}</Chip>
                {can("configure") && <button className="btn small" onClick={() => setEdit(s)}>Edit</button>}
                {(can("configure") || can("receive")) && <button className="btn small" onClick={() => setLocFor(s)}>Add bin</button>}
              </>}>
              <div style={{ padding: "0 16px 10px" }} className="kv">
                <div><span>Labels held</span><b className="num">{int(s.items)}</b></div>
                <div><span>Stock value</span><b className="num">{cur} {money(s.value)}</b></div>
                <div><span>Bins</span><b className="num">{s.locations.length}</b></div>
              </div>
              <Table rows={s.locations.length} cols={["Location code", "Section", "Rack", "Bin", "Kind", { t: "Items", c: "r" }, "Status"]}>
                {s.locations.map((l) => (
                  <tr key={l.id}>
                    <td className="mono tiny">{l.fullCode}</td>
                    <td>{l.section}</td><td>{l.rack}</td><td>{l.bin}</td>
                    <td>{l.kind === "Stock" ? <Chip tone="ok">Stock</Chip> : l.kind === "Quarantine" ? <Chip tone="watch">Quarantine</Chip> : <Chip tone="muted">Scrap</Chip>}</td>
                    <td className="r num">{int(l.items)}</td>
                    <td>{l.active ? <span className="tiny">Active</span> : <span className="tiny muted">Inactive</span>}</td>
                  </tr>
                ))}
              </Table>
            </Card>
          ))}
        </div>
      )}

      {edit && <StoreModal s={edit === "new" ? null : edit} onClose={() => setEdit(null)} onDone={() => { setEdit(null); list.reload(); }} />}
      {locFor && <LocationModal store={locFor} onClose={() => setLocFor(null)} onDone={() => { setLocFor(null); list.reload(); }} />}
    </>
  );
}

function StoreModal({ s, onClose, onDone }: { s: StoreDto | null; onClose: () => void; onDone: () => void }) {
  const { config } = useAuth();
  const toast = useToast();
  const users = useAsync(() => get<UserDto[]>("/api/users").catch(() => []), []);
  const [f, setF] = useState<StoreUpsert>({
    code: s?.code ?? "", name: s?.name ?? "", type: s?.type ?? "Central", zone: s?.zone ?? config?.zones[0] ?? "",
    address: s?.address ?? "", lat: s?.lat ?? null, lng: s?.lng ?? null, inChargeUserId: s?.inChargeUserId ?? null, active: s?.active ?? true,
  });
  const [busy, setBusy] = useState(false);
  return (
    <Modal open onClose={onClose} size="narrow" title={s ? `${s.code} — ${s.name}` : "New store"}
      footer={<><button className="btn primary" disabled={busy || !f.code || !f.name} onClick={async () => {
        setBusy(true);
        try { if (s) await put(`/api/stores/${s.id}`, f); else await post("/api/stores", f); toast("Saved"); onDone(); }
        catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Save</button><span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>Store code</label><input value={f.code} onChange={(e) => setF({ ...f, code: e.target.value })} placeholder="CEN01" disabled={!!s} /></div>
          <div className="field"><label>Type</label><select value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })}>{["Central", "Zonal", "Sub-store"].map((t) => <option key={t}>{t}</option>)}</select></div>
          <div className="field"><label>Zone</label><select value={f.zone} onChange={(e) => setF({ ...f, zone: e.target.value })}>{(config?.zones ?? []).map((z) => <option key={z}>{z}</option>)}</select></div>
        </div>
        <div className="field"><label>Name</label><input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder="Central Store, Lahore" /></div>
        <div className="field"><label>Address</label><input value={f.address ?? ""} onChange={(e) => setF({ ...f, address: e.target.value })} /></div>
        <div className="frow">
          <div className="field"><label>Latitude</label><input type="number" step="0.000001" value={f.lat ?? ""} onChange={(e) => setF({ ...f, lat: e.target.value ? +e.target.value : null })} /></div>
          <div className="field"><label>Longitude</label><input type="number" step="0.000001" value={f.lng ?? ""} onChange={(e) => setF({ ...f, lng: e.target.value ? +e.target.value : null })} /></div>
        </div>
        <div className="frow">
          <div className="field"><label>Store In-Charge</label>
            <select value={f.inChargeUserId ?? ""} onChange={(e) => setF({ ...f, inChargeUserId: e.target.value || null })}>
              <option value="">— none —</option>
              {(users.data ?? []).filter((u) => ["storeincharge", "storekeeper"].includes(u.roleKey)).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
            </select></div>
          <div className="field"><label>Status</label>
            <select value={f.active ? "1" : "0"} onChange={(e) => setF({ ...f, active: e.target.value === "1" })}>
              <option value="1">Active</option><option value="0">Inactive</option>
            </select>
            <span className="hint">A store holding stock cannot be deactivated (BR-024)</span></div>
        </div>
      </div>
    </Modal>
  );
}

function LocationModal({ store, onClose, onDone }: { store: StoreDto; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const [f, setF] = useState({ section: "", rack: "", bin: "", kind: "Stock", active: true });
  const [busy, setBusy] = useState(false);
  const preview = [store.code, f.section, f.rack, f.bin].filter(Boolean).join("-").toUpperCase();
  return (
    <Modal open onClose={onClose} size="narrow" title={`New bin in ${store.code}`} sub="Store ▸ section ▸ rack ▸ bin"
      footer={<><button className="btn primary" disabled={busy || !f.section} onClick={async () => {
        setBusy(true);
        try { await post(`/api/stores/${store.id}/locations`, f); toast(`${preview} created`); onDone(); }
        catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Create bin</button><span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>Section</label><input value={f.section} onChange={(e) => setF({ ...f, section: e.target.value })} placeholder="LUM" /></div>
          <div className="field"><label>Rack / row</label><input value={f.rack} onChange={(e) => setF({ ...f, rack: e.target.value })} placeholder="R01" /></div>
          <div className="field"><label>Bin / shelf</label><input value={f.bin} onChange={(e) => setF({ ...f, bin: e.target.value })} placeholder="B03" /></div>
        </div>
        <div className="field"><label>Kind</label>
          <select value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}>
            <option value="Stock">Stock — issuable material</option>
            <option value="Quarantine">Quarantine — returned items awaiting classification</option>
            <option value="Scrap">Scrap — condemned items awaiting disposal</option>
          </select></div>
        {preview && <Notice kind="info"><span>Location code will be <b className="mono">{preview}</b></span></Notice>}
      </div>
    </Modal>
  );
}
