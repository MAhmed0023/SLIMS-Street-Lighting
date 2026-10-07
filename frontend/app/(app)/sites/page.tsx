"use client";
import { useState } from "react";
import { get, post, put } from "@/lib/api";
import type { SiteDto, SiteUpsert, UserDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { coords, int } from "@/lib/format";
import { Card, Chip, ErrorBox, Kpi, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

export default function SitesPage() {
  const { config, can } = useAuth();
  const [zone, setZone] = useState("");
  const [q, setQ] = useState("");
  const [edit, setEdit] = useState<SiteDto | "new" | null>(null);
  const [bulk, setBulk] = useState(false);
  const list = useAsync(() => get<SiteDto[]>(`/api/sites?${zone ? `zone=${encodeURIComponent(zone)}&` : ""}${q ? `q=${encodeURIComponent(q)}` : ""}`), [zone, q]);
  const canEdit = can("configure") || can("requisition");

  return (
    <>
      <div className="filters">
        <select value={zone} onChange={(e) => setZone(e.target.value)}>
          <option value="">All zones</option>
          {(config?.zones ?? []).map((z) => <option key={z}>{z}</option>)}
        </select>
        <input type="search" placeholder="Search code, name, division…" value={q} onChange={(e) => setQ(e.target.value)} />
        <span className="spacer" />
        {can("configure") && <button className="btn" onClick={() => setBulk(true)}>Bulk upload from survey</button>}
        {canEdit && <button className="btn primary" onClick={() => setEdit("new")}>New site</button>}
      </div>

      <Notice kind="info"><span>
        A site must exist here, with coordinates, before material can be requisitioned for it (BR-007). The geo-fence radius is what installation
        evidence is judged against — default {config?.geo.defaultGeofenceM ?? 50} m plus {config?.geo.toleranceM ?? 20} m tolerance.
      </span></Notice>

      {list.data && <div className="grid g-kpi">
        <Kpi label="Sites in scope" value={int(list.data.length)} sub={`${new Set(list.data.map((s) => s.division)).size} division(s)`} />
        <Kpi label="Installed fixtures" value={int(list.data.reduce((s, x) => s + x.installed, 0))} tone="good" />
        <Kpi label="Material at site" value={int(list.data.reduce((s, x) => s + x.atSite, 0))} tone="watch" sub="issued, not yet installed" />
        <Kpi label="Pending verification" value={int(list.data.reduce((s, x) => s + x.pending, 0))} tone={list.data.some((x) => x.pending) ? "crit" : "good"} />
      </div>}

      <Card flush title="Site locations" sub="Each site is a point with a geo-fence; installation evidence is measured against it">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No sites in this view." cols={["Code", "Name", "Zone / division", "Type", "Coordinates", { t: "Fence", c: "r" }, "Supervisor", { t: "Installed", c: "r" }, { t: "At site", c: "r" }, "Status", ""]}>
            {list.data!.map((s) => (
              <tr key={s.id} className="row-link" onClick={() => setEdit(s)}>
                <td className="mono tiny">{s.code}</td>
                <td><b>{s.name}</b>{s.ward && <div className="tiny">{s.ward}</div>}</td>
                <td className="tiny">{s.zone}<div className="tiny">{s.division}</div></td>
                <td className="tiny">{s.type}</td>
                <td className="mono tiny">{coords(s.lat, s.lng)}</td>
                <td className="r num">{s.geofenceM} m</td>
                <td className="tiny">{s.supervisorName ?? "—"}</td>
                <td className="r num">{int(s.installed)}</td>
                <td className="r num">{s.atSite > 0 ? <span className="watch">{int(s.atSite)}</span> : "—"}{s.pending > 0 && <div className="tiny err">{s.pending} pending</div>}</td>
                <td><Chip tone={s.status === "Active" ? "ok" : s.status === "Completed" ? "info" : s.status === "On hold" ? "crit" : "muted"}>{s.status}</Chip></td>
                <td><span className="linky">Edit</span></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>

      {edit && <SiteModal s={edit === "new" ? null : edit} onClose={() => setEdit(null)} onDone={() => { setEdit(null); list.reload(); }} />}
      {bulk && <BulkModal onClose={() => setBulk(false)} onDone={() => { setBulk(false); list.reload(); }} />}
    </>
  );
}

function SiteModal({ s, onClose, onDone }: { s: SiteDto | null; onClose: () => void; onDone: () => void }) {
  const { config, can } = useAuth();
  const toast = useToast();
  const users = useAsync(() => can("configure") ? get<UserDto[]>("/api/users") : Promise.resolve([]), []);
  const [f, setF] = useState<SiteUpsert>({
    code: s?.code ?? null, name: s?.name ?? "", zone: s?.zone ?? config?.zones[0] ?? "", division: s?.division ?? "", ward: s?.ward ?? "",
    type: s?.type ?? "Road stretch", lat: s?.lat ?? 0, lng: s?.lng ?? 0, geofenceM: s?.geofenceM ?? config?.geo.defaultGeofenceM ?? 50,
    supervisorUserId: s?.supervisorUserId ?? null, officerUserId: s?.officerUserId ?? null, status: s?.status ?? "Active", remarks: s?.remarks ?? "",
  });
  const [busy, setBusy] = useState(false);
  const usingHere = () => navigator.geolocation?.getCurrentPosition(
    (p) => { setF({ ...f, lat: +p.coords.latitude.toFixed(6), lng: +p.coords.longitude.toFixed(6) }); toast(`Position captured ±${Math.round(p.coords.accuracy)} m`); },
    () => toast("Could not read the device location.", true), { enableHighAccuracy: true });

  return (
    <Modal open onClose={onClose} title={s ? s.name : "New site location"} sub={s ? s.code : "Surveyed point where material is installed"}
      footer={<><button className="btn primary" disabled={busy || !f.name || !f.division || (!f.lat && !f.lng)} onClick={async () => {
        setBusy(true);
        try { if (s) await put(`/api/sites/${s.id}`, f); else await post("/api/sites", f); toast("Saved"); onDone(); }
        catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Save</button><span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>Name</label><input value={f.name} onChange={(e) => setF({ ...f, name: e.target.value })} placeholder="Mall Road — GPO to Regal Chowk" /></div>
          <div className="field"><label>Type</label><select value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })}>{(config?.siteTypes ?? []).map((t) => <option key={t}>{t}</option>)}</select></div>
          <div className="field"><label>Status</label><select value={f.status} onChange={(e) => setF({ ...f, status: e.target.value })}>{["Planned", "Active", "Completed", "On hold"].map((t) => <option key={t}>{t}</option>)}</select></div>
        </div>
        <div className="frow">
          <div className="field"><label>Zone</label><select value={f.zone} onChange={(e) => setF({ ...f, zone: e.target.value })}>{(config?.zones ?? []).map((z) => <option key={z}>{z}</option>)}</select></div>
          <div className="field"><label>Division</label><input value={f.division} onChange={(e) => setF({ ...f, division: e.target.value })} placeholder="Lahore City" /></div>
          <div className="field"><label>Ward / UC</label><input value={f.ward ?? ""} onChange={(e) => setF({ ...f, ward: e.target.value })} /></div>
        </div>
        <div className="fsec">
          <h4>Position and geo-fence</h4>
          <div className="frow">
            <div className="field"><label>Latitude</label><input type="number" step="0.000001" value={f.lat} onChange={(e) => setF({ ...f, lat: +e.target.value })} /></div>
            <div className="field"><label>Longitude</label><input type="number" step="0.000001" value={f.lng} onChange={(e) => setF({ ...f, lng: +e.target.value })} /></div>
            <div className="field"><label>Geo-fence radius (m)</label><input type="number" min="5" value={f.geofenceM ?? 50} onChange={(e) => setF({ ...f, geofenceM: +e.target.value })} />
              <span className="hint">Larger for a road stretch, smaller for one pole</span></div>
          </div>
          <div className="actions"><button className="btn small" onClick={usingHere}>Use my current position</button>
            <span className="tiny">Six decimal places. Changing coordinates after material has been issued needs a configuring role (BR-025).</span></div>
        </div>
        {can("configure") && <div className="frow">
          <div className="field"><label>Assigned supervisor</label>
            <select value={f.supervisorUserId ?? ""} onChange={(e) => setF({ ...f, supervisorUserId: e.target.value || null })}>
              <option value="">— none —</option>
              {(users.data ?? []).filter((u) => u.roleKey === "supervisor").map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
            </select>
            <span className="hint">Only this supervisor may requisition for the site (BR-008)</span></div>
          <div className="field"><label>Assigned officer</label>
            <select value={f.officerUserId ?? ""} onChange={(e) => setF({ ...f, officerUserId: e.target.value || null })}>
              <option value="">— none —</option>
              {(users.data ?? []).filter((u) => u.roleKey === "officer").map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
            </select></div>
        </div>}
        <div className="field"><label>Remarks</label><textarea value={f.remarks ?? ""} onChange={(e) => setF({ ...f, remarks: e.target.value })} /></div>
      </div>
    </Modal>
  );
}

function BulkModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<{ created: number; updated: number; errors: string[] } | null>(null);
  const sample = `[
  { "name": "Ferozepur Road — Kalma Chowk", "zone": "South", "division": "Gulberg", "type": "Junction", "lat": 31.504012, "lng": 74.326440, "geofenceM": 120, "status": "Active" }
]`;
  return (
    <Modal open onClose={onClose} title="Bulk upload site locations" sub="From a survey export — every row is validated and errors are reported by row (FR-MD-04)"
      footer={<><button className="btn primary" disabled={busy || !text.trim()} onClick={async () => {
        setBusy(true);
        try {
          const rows = JSON.parse(text);
          const r = await post<{ created: number; updated: number; errors: string[] }>("/api/sites/bulk", rows);
          setResult(r); toast(`${r.created} created, ${r.updated} updated, ${r.errors.length} rejected`);
          if (r.errors.length === 0) onDone();
        } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Upload</button><span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="form">
        <div className="field"><label>Survey rows (JSON array)</label>
          <textarea style={{ minHeight: 220, fontFamily: "var(--font-mono)", fontSize: 12 }} value={text} onChange={(e) => setText(e.target.value)} placeholder={sample} /></div>
        {result && <>
          <Notice kind={result.errors.length ? "watch" : "good"}><span><b>{result.created}</b> created, <b>{result.updated}</b> updated, <b>{result.errors.length}</b> rejected.</span></Notice>
          {result.errors.length > 0 && <Card title="Rejected rows"><ul className="tiny">{result.errors.map((e, i) => <li key={i}>{e}</li>)}</ul></Card>}
        </>}
      </div>
    </Modal>
  );
}
