"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { get, post, upload } from "@/lib/api";
import type { InstallationDto, ItemDto, SiteDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { coords, dateTime, geoTone } from "@/lib/format";
import { Card, Chip, ErrorBox, Lightbox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { AmendButton, AuthImg, EvidencePhoto } from "@/components/Controls";
import Link from "next/link";

interface Shot { blob: Blob; url: string; lat: number | null; lng: number | null; acc: number | null; at: Date }

/** Stamps coordinates, time and identity onto the visible face of the photograph (FR-IN-04). */
async function stampImage(file: File, lines: string[]): Promise<Blob> {
  const bitmap = await createImageBitmap(file);
  const maxW = 1600;
  const scale = Math.min(1, maxW / bitmap.width);
  const w = Math.round(bitmap.width * scale), h = Math.round(bitmap.height * scale);
  const canvas = document.createElement("canvas"); canvas.width = w; canvas.height = h;
  const ctx = canvas.getContext("2d")!;
  ctx.drawImage(bitmap, 0, 0, w, h);
  const fs = Math.max(13, Math.round(w / 52));
  const pad = Math.round(fs * 0.55);
  const bandH = lines.length * (fs + pad * 0.5) + pad * 1.4;
  ctx.fillStyle = "rgba(0,0,0,.66)";
  ctx.fillRect(0, h - bandH, w, bandH);
  ctx.fillStyle = "#fff";
  ctx.font = `600 ${fs}px ui-monospace, "IBM Plex Mono", Consolas, monospace`;
  ctx.textBaseline = "top";
  lines.forEach((t, i) => ctx.fillText(t, pad, h - bandH + pad * 0.8 + i * (fs + pad * 0.5)));
  return await new Promise<Blob>((res) => canvas.toBlob((b) => res(b!), "image/jpeg", 0.82));
}

function useGeo() {
  const [pos, setPos] = useState<GeolocationPosition | null>(null);
  const [err, setErr] = useState<string | null>(null);
  useEffect(() => {
    if (!navigator.geolocation) { setErr("This device has no location service."); return; }
    const id = navigator.geolocation.watchPosition(setPos, (e) => setErr(e.message), { enableHighAccuracy: true, maximumAge: 5000, timeout: 15000 });
    return () => navigator.geolocation.clearWatch(id);
  }, []);
  return { pos, err };
}

export default function FieldPage() {
  const { config, can, user } = useAuth();
  const toast = useToast();
  const [tab, setTab] = useState<"capture" | "exceptions" | "register">("capture");
  const [zoom, setZoom] = useState<string | null>(null);
  const [open, setOpen] = useState<InstallationDto | null>(null);

  const field = useAsync(() => get<ItemDto[]>("/api/issues/field"), [tab === "capture"]);
  const exceptions = useAsync(() => get<InstallationDto[]>("/api/installations?geoStatus=PendingVerification"), [tab === "exceptions"]);
  const register = useAsync(() => get<InstallationDto[]>("/api/installations"), [tab === "register"]);
  const sites = useAsync(() => get<SiteDto[]>("/api/sites"), []);
  // links from notifications: /field?inst=12
  useEffect(() => {
    const id = new URLSearchParams(window.location.search).get("inst");
    if (id) get<InstallationDto>(`/api/installations/${id}`).then(setOpen).catch(() => {});
  }, []);
  const webCapture = !!config?.field?.allowWebCapture;

  return (
    <>
      <div className="seg">
        <button className={tab === "capture" ? "active" : ""} onClick={() => setTab("capture")}>{webCapture ? "Capture installation" : "Field work list"}</button>
        <button className={tab === "exceptions" ? "active" : ""} onClick={() => setTab("exceptions")}>Geo-fence exceptions{exceptions.data?.length ? ` (${exceptions.data.length})` : ""}</button>
        <button className={tab === "register" ? "active" : ""} onClick={() => setTab("register")}>Installation register</button>
      </div>

      {tab === "capture" && (webCapture && can("install")
        ? <CapturePanel items={field.data ?? []} loading={field.loading} sites={sites.data ?? []} onDone={() => { field.reload(); register.reload(); }} />
        : <>
          <Notice kind="info"><span>Installation evidence is captured in the <b>SLIMS Field</b> app on the officer&apos;s registered phone: live camera only, real GPS with mock-location detection, works offline and synchronises by itself (FR-FLD-007, 012). {can("install") && <Link className="linky" href="/mobile-setup">Set up your phone</Link>}</span></Notice>
          <Card flush title="Material in the field" sub="Issued to sites and not yet installed">
            {field.loading ? <Loading /> : field.error ? <ErrorBox error={field.error} /> : (
              <Table rows={field.data!.length} empty="Nothing waiting in the field." cols={["Site", "Item", "Status", "Issued"]}>
                {field.data!.map((i) => <tr key={i.id}><td><div className="two-line"><b>{i.siteCode}</b><small>{i.siteName}</small></div></td><td className="mono">{i.itemCode} #{i.serial}</td><td><Chip tone="watch">{i.status}</Chip></td><td className="tiny">{dateTime(i.issuedAt)}</td></tr>)}
              </Table>
            )}
          </Card>
        </>)}

      {tab === "exceptions" && (
        <Card flush title="Installations held for verification" sub="Coordinates fell outside the site geo-fence — the Supervisor decides (BR-017)">
          {exceptions.loading ? <Loading /> : exceptions.error ? <ErrorBox error={exceptions.error} /> : (
            <Table rows={exceptions.data!.length} empty="No exceptions. Every installation passed geo-validation on the first attempt." cols={["Installation", "Site", "Item", "Officer", { t: "Deviation", c: "r" }, "Captured", "Age", ""]}>
              {exceptions.data!.map((e) => (
                <tr key={e.id} className="row-link" onClick={() => setOpen(e)}>
                  <td className="mono">{e.number}</td>
                  <td><div className="two-line"><b>{e.siteCode}</b><small>{e.siteName}</small></div></td>
                  <td><div className="two-line"><b className="mono">{e.itemCode}</b><small>{e.description}</small></div></td>
                  <td>{e.officer}</td>
                  <td className="r"><b className="err num">{Math.round(e.distanceM)} m</b><div className="tiny">allowed {e.geofenceM} + {config?.geo.toleranceM ?? 20} m</div></td>
                  <td className="tiny">{dateTime(e.capturedAt)}</td>
                  <td>{e.ageDays} d {e.escalationLevel > 0 && <Chip tone="crit">escalated L{e.escalationLevel}</Chip>} {e.fvRequestedAt && <Chip tone={e.fvFindingsAt ? "ok" : "watch"}>{e.fvFindingsAt ? "findings in" : "field check"}</Chip>}</td>
                  <td><span className="linky">Review</span></td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      )}

      {tab === "register" && (
        <Card flush title="Installation register" sub="Every submission with its evidence and geo-validation result" actions={<button className="btn small" onClick={register.reload}>Refresh</button>}>
          {register.loading ? <Loading /> : register.error ? <ErrorBox error={register.error} /> : (
            <Table rows={register.data!.length} empty="Nothing installed yet." cols={["Installation", "Site", "Pole", "Item", "Officer", { t: "Distance", c: "r" }, "Result", "Captured", ""]}>
              {register.data!.map((e) => (
                <tr key={e.id} className="row-link" onClick={() => setOpen(e)}>
                  <td className="mono">{e.number}</td>
                  <td><div className="two-line"><b>{e.siteCode}</b><small>{e.siteName}</small></div></td>
                  <td className="tiny">{e.poleId ?? "—"}</td>
                  <td className="mono tiny">{e.itemCode}</td>
                  <td className="tiny">{e.officer}</td>
                  <td className="r num">{Math.round(e.distanceM)} m</td>
                  <td><Chip tone={geoTone(e.geoStatus)}>{e.geoStatus === "ApprovedException" ? "Exception approved" : e.geoStatus === "PendingVerification" ? "Pending" : e.geoStatus}</Chip></td>
                  <td className="tiny">{dateTime(e.capturedAt)}{e.offline && <> <Chip tone="muted">offline</Chip></>}</td>
                  <td><span className="linky">Open</span></td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      )}

      {open && <DetailModal x={open} onClose={() => setOpen(null)} onZoom={setZoom} canVerify={can("verify") && open.officerId !== user?.id} myId={user?.id ?? ""}
        onChanged={(x) => { setOpen(x); exceptions.reload(); register.reload(); }}
        onDone={() => { setOpen(null); exceptions.reload(); register.reload(); toast("Decision recorded"); }} />}
      <Lightbox src={zoom} onClose={() => setZoom(null)} />
    </>
  );
}

function CapturePanel({ items, loading, sites, onDone }: { items: ItemDto[]; loading: boolean; sites: SiteDto[]; onDone: () => void }) {
  const { config, user } = useAuth();
  const toast = useToast();
  const { pos, err } = useGeo();
  const [qr, setQr] = useState("");
  const [pole, setPole] = useState("");
  const [remarks, setRemarks] = useState("");
  const [before, setBefore] = useState<Shot | null>(null);
  const [after, setAfter] = useState<Shot | null>(null);
  const [busy, setBusy] = useState(false);
  const beforeRef = useRef<HTMLInputElement>(null);
  const afterRef = useRef<HTMLInputElement>(null);

  const item = items.find((i) => i.qrCode === qr.trim()) ?? null;
  const site = item?.siteId ? sites.find((s) => s.id === item.siteId) ?? null : null;
  const acc = pos?.coords.accuracy ?? null;
  const minAcc = config?.geo.minAccuracyM ?? 25;
  const distance = pos && site ? haversine(pos.coords.latitude, pos.coords.longitude, site.lat, site.lng) : null;
  const allowed = site ? site.geofenceM + (config?.geo.toleranceM ?? 20) : null;

  const take = useCallback(async (file: File, kind: "before" | "after") => {
    if (!pos) { toast("Waiting for a GPS fix — try again in a moment.", true); return; }
    const now = new Date();
    const stamped = await stampImage(file, [
      `${kind.toUpperCase()}  ${now.toLocaleString("en-GB")}`,
      `LAT ${pos.coords.latitude.toFixed(6)}  LNG ${pos.coords.longitude.toFixed(6)}  ±${Math.round(pos.coords.accuracy)}m`,
      `${site?.code ?? "SITE"}  ${item?.itemCode ?? ""}  ${user?.name ?? ""}`,
    ]);
    const shot: Shot = { blob: stamped, url: URL.createObjectURL(stamped), lat: pos.coords.latitude, lng: pos.coords.longitude, acc: pos.coords.accuracy, at: now };
    if (kind === "before") setBefore(shot); else setAfter(shot);
  }, [pos, site, item, user, toast]);

  const submit = async () => {
    if (!item || !before || !after || !pos) return;
    setBusy(true);
    try {
      const f = new FormData();
      f.set("Qr", item.qrCode); f.set("PoleId", pole); f.set("Lat", String(pos.coords.latitude)); f.set("Lng", String(pos.coords.longitude));
      f.set("AccuracyM", String(Math.round(pos.coords.accuracy))); f.set("CapturedAt", after.at.toISOString()); f.set("Remarks", remarks); f.set("Offline", "false");
      f.set("Before", new File([before.blob], "before.jpg", { type: "image/jpeg" }));
      f.set("BeforeLat", String(before.lat)); f.set("BeforeLng", String(before.lng)); f.set("BeforeAccuracyM", String(Math.round(before.acc ?? 0))); f.set("BeforeAt", before.at.toISOString());
      f.set("After", new File([after.blob], "after.jpg", { type: "image/jpeg" }));
      f.set("AfterLat", String(after.lat)); f.set("AfterLng", String(after.lng)); f.set("AfterAccuracyM", String(Math.round(after.acc ?? 0))); f.set("AfterAt", after.at.toISOString());
      const r = await upload<InstallationDto>("/api/installations", f);
      if (r.geoStatus === "Confirmed") toast(`${r.number} confirmed — ${Math.round(r.distanceM)} m from the site point`);
      else toast(`Held for verification — ${Math.round(r.distanceM)} m from the site, outside the geo-fence`, true);
      setQr(""); setPole(""); setRemarks(""); setBefore(null); setAfter(null); onDone();
    } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };

  if (loading) return <Loading what="your work list" />;
  return (
    <div className="split wide">
      <Card title="1 · The item you are installing" sub="Only material issued to your sites appears here">
        <div className="form">
          <div className="field">
            <label>Scan or choose the item</label>
            <select value={qr} onChange={(e) => setQr(e.target.value)}>
              <option value="">— {items.length} item(s) in the field —</option>
              {items.map((i) => <option key={i.id} value={i.qrCode}>{i.siteCode} · {i.itemCode} #{i.serial} ({i.status})</option>)}
            </select>
          </div>
          <div className="field"><label>Or scan the QR at the pole</label>
            <input className="mono" value={qr} onChange={(e) => setQr(e.target.value)} placeholder="SLIMS|LUM-LED-090W-PO2611-001|000012|A1B2C3" /></div>
          <div className="field"><label>Pole / point identifier</label><input value={pole} onChange={(e) => setPole(e.target.value)} placeholder="P-014" /></div>
          <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} placeholder="Optional note about the installation" /></div>
        </div>

        {item && site && <div className="kv" style={{ marginTop: 12 }}>
          <div><span>Site</span><b>{site.code}</b></div>
          <div><span>Name</span><b>{site.name}</b></div>
          <div><span>Site point</span><b className="mono tiny">{coords(site.lat, site.lng)}</b></div>
          <div><span>Geo-fence</span><b>{site.geofenceM} m + {config?.geo.toleranceM ?? 20} m tolerance</b></div>
        </div>}

        <div style={{ marginTop: 12 }}>
          {err && <Notice kind="crit">{err}</Notice>}
          {!pos && !err && <Notice kind="info">Waiting for a GPS fix…</Notice>}
          {pos && <Notice kind={acc != null && acc > minAcc ? "watch" : "good"}>
            <span>
              Your position <b className="mono">{coords(pos.coords.latitude, pos.coords.longitude)}</b> · accuracy ±{Math.round(acc ?? 0)} m
              {acc != null && acc > minAcc && <> — poorer than the ±{minAcc} m standard; wait for a better fix.</>}
              {distance != null && allowed != null && <> · <b>{Math.round(distance)} m</b> from the site point (limit {allowed} m)</>}
            </span>
          </Notice>}
        </div>
      </Card>

      <div className="grid">
        <Card title="2 · Before and after evidence" sub="Captured live through the app; coordinates and time are burned onto the image (BR-015, BR-016)">
          <div className="evidence-pair">
            {([["before", before, beforeRef, setBefore], ["after", after, afterRef, setAfter]] as const).map(([kind, shot, ref, set]) => (
              <div className="ev-card" key={kind}>
                <h5>{kind} installation</h5>
                {shot
                  // eslint-disable-next-line @next/next/no-img-element
                  ? <img src={shot.url} alt={`${kind} evidence`} onClick={() => window.open(shot.url, "_blank")} />
                  : <div style={{ height: 230, display: "grid", placeItems: "center", color: "var(--ink-3)", fontSize: 12.5, textAlign: "center", padding: 16 }}>
                      No {kind} photograph yet.<br />Gallery uploads are not accepted.
                    </div>}
                <div className="ev-meta">
                  {shot ? <>
                    <span className="mono">{coords(shot.lat, shot.lng)}</span>
                    <span>±{Math.round(shot.acc ?? 0)} m · {shot.at.toLocaleString("en-GB")}</span>
                  </> : <span>Waiting for capture</span>}
                  <div className="actions" style={{ marginTop: 5 }}>
                    <input ref={ref} type="file" accept="image/*" capture="environment" hidden
                      onChange={(e) => { const f = e.target.files?.[0]; if (f) take(f, kind); e.target.value = ""; }} />
                    <button className="btn small" disabled={!pos || !item} onClick={() => ref.current?.click()}>{shot ? "Retake" : `Capture ${kind}`}</button>
                    {shot && <button className="btn small" onClick={() => set(null)}>Discard</button>}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </Card>

        <Card title="3 · Submit" sub="The server measures the distance to the site and decides — you cannot override it (BR-016)">
          <div className="actions">
            <button className="btn primary" disabled={busy || !item || !before || !after || !pos} onClick={submit}>
              {busy ? "Uploading…" : "Submit installation with evidence"}
            </button>
          </div>
          {!item && <div className="tiny" style={{ marginTop: 6 }}>Choose the item first.</div>}
          {item && (!before || !after) && <div className="tiny" style={{ marginTop: 6 }}>Both photographs are required before you can submit.</div>}
          {distance != null && allowed != null && before && after &&
            <div className={`geo-verdict ${distance <= allowed ? "ok" : "bad"}`} style={{ marginTop: 10 }}>
              <span className="big">{Math.round(distance)} m</span>
              <span>{distance <= allowed ? "Inside the geo-fence — this will be confirmed automatically." : `Outside the ${allowed} m limit — this will be held for the Supervisor to verify.`}</span>
            </div>}
        </Card>
      </div>
    </div>
  );
}

function DetailModal({ x, onClose, onZoom, canVerify, myId, onDone, onChanged }: { x: InstallationDto; onClose: () => void; onZoom: (u: string) => void; canVerify: boolean; myId: string; onDone: () => void; onChanged: (x: InstallationDto) => void }) {
  const toast = useToast();
  const [just, setJust] = useState("");
  const [busy, setBusy] = useState(false);
  const [fvNote, setFvNote] = useState(""); const [findings, setFindings] = useState("");
  const fv = async (path: string, body: unknown, ok: string) => {
    setBusy(true);
    try { onChanged(await post<InstallationDto>(`/api/installations/${x.id}/${path}`, body)); toast(ok); } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const decide = async (approve: boolean) => {
    setBusy(true);
    try { await post(`/api/installations/${x.id}/verify`, { approve, justification: just || null }); onDone(); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const inside = x.geoStatus === "Confirmed";
  return (
    <Modal open onClose={onClose} size="wide" title={`${x.number} — ${x.itemCode}`} sub={`${x.siteCode} · ${x.siteName} · ${x.zone} zone`}
      chips={<><Chip tone={geoTone(x.geoStatus)}>{x.geoStatus}</Chip>{x.poleId && <Chip tone="plain">Pole {x.poleId}</Chip>}{x.offline && <Chip tone="muted">captured offline</Chip>}
        {x.geoStatus === "PendingVerification" && <Chip tone={x.escalationLevel ? "crit" : "watch"}>{x.ageDays} d open{x.escalationLevel ? ` · escalated L${x.escalationLevel}` : ""}</Chip>}
        {x.deviceId && <Chip tone="plain">from bound phone</Chip>}</>}
      footer={x.geoStatus === "PendingVerification" && canVerify
        ? <><button className="btn primary" disabled={busy || just.trim().length < 10} onClick={() => decide(true)}>Approve deviation</button>
            <button className="btn danger" disabled={busy} onClick={() => decide(false)}>Require re-capture</button>
            <span className="tiny">A written justification of at least 10 characters is required to approve (BR-017).</span>
            <span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>
        : <><span className="tiny">{x.verifiedBy ? `Verified by ${x.verifiedBy} on ${dateTime(x.verifiedAt)}` : x.geoStatus === "MissingEvidence" ? "Photographs failed the integrity check — re-capture required" : "Confirmed automatically by geo-validation"}</span><span className="spacer" />
          {(x.geoStatus === "Confirmed" || x.geoStatus === "ApprovedException") && <AmendButton entityType="installation" entityRef={x.number} current={{ PoleId: x.poleId, Remarks: x.remarks }} />}
          <button className="btn" onClick={onClose}>Close</button></>}>
      <div className={`geo-verdict ${inside || x.geoStatus === "ApprovedException" ? "ok" : "bad"}`}>
        <span className="big">{Math.round(x.distanceM)} m</span>
        <span>
          from the recorded site point ({x.geofenceM} m geo-fence). Evidence at <b className="mono">{coords(x.lat, x.lng)}</b>, site at <b className="mono">{coords(x.siteLat, x.siteLng)}</b>, GPS accuracy ±{Math.round(x.accuracyM)} m.
        </span>
      </div>
      {x.justification && <Notice kind={x.geoStatus === "ApprovedException" ? "watch" : "crit"}><span><b>Justification:</b> {x.justification}</span></Notice>}

      <div className="evidence-pair">
        <EvidencePhoto e={x.before} onZoom={onZoom} />
        <EvidencePhoto e={x.after} onZoom={onZoom} />
      </div>

      {(x.geoStatus === "PendingVerification" || x.fvRequestedAt) && <Card title="Field verification (FR-EXC-007)" sub="The reviewer can ask the Lighting Supervisor to check on the ground">
        {x.fvRequestedAt && <div className="tiny">Requested by <b>{x.fvRequestedBy}</b> on {dateTime(x.fvRequestedAt)}{x.fvRequestNote ? ` — “${x.fvRequestNote}”` : ""}</div>}
        {x.fvFindingsAt && <Notice kind="good"><span><b>Findings by {x.fvFindingsBy}</b> ({dateTime(x.fvFindingsAt)}): {x.fvFindings}</span></Notice>}
        {x.fvPhotoUrls.length > 0 && <div className="actions">{x.fvPhotoUrls.map((u) => <AuthImg key={u} src={u} alt="Verification" style={{ width: 120, height: 90, objectFit: "cover", borderRadius: 6 }} onOpen={onZoom} />)}</div>}
        {x.geoStatus === "PendingVerification" && canVerify && (!x.fvRequestedAt || x.fvFindingsAt) && <div className="actions" style={{ marginTop: 8 }}>
          <input value={fvNote} onChange={(e) => setFvNote(e.target.value)} placeholder="What should the supervisor check?" style={{ flex: 1 }} />
          <button className="btn" disabled={busy} onClick={() => fv("field-verification", { note: fvNote || null }, "Field verification requested — the supervisor is notified")}>Request field verification</button>
        </div>}
        {x.fvRequestedAt && !x.fvFindingsAt && x.fvAssignedToId === myId && <div className="form" style={{ marginTop: 8 }}>
          <div className="field"><label>Your findings (photographs are added from the SLIMS Field app)</label><textarea value={findings} onChange={(e) => setFindings(e.target.value)} placeholder="Pole found installed at the far end of the junction; light working." /></div>
          <div className="actions"><button className="btn primary" disabled={busy || findings.trim().length < 10} onClick={() => fv("findings", { findings, photos: null, deviceTime: new Date().toISOString() }, "Findings recorded")}>Record findings</button></div>
        </div>}
      </Card>}

      <div className="kv">
        <div><span>Officer</span><b>{x.officer}</b></div>
        <div><span>Captured</span><b>{dateTime(x.capturedAt)}</b></div>
        <div><span>Synchronised</span><b>{dateTime(x.submittedAt)}</b></div>
        {x.queuedAt && <div><span>Queued offline</span><b>{dateTime(x.queuedAt)}</b></div>}
        {x.clientTxnId && <div><span>Transaction ID</span><b className="mono tiny">{x.clientTxnId}</b></div>}
        <div><span>QR payload</span><b className="mono tiny">{x.qrCode}</b></div>
      </div>
      {x.remarks && <Notice kind="info">{x.remarks}</Notice>}
      {x.geoStatus === "PendingVerification" && canVerify &&
        <div className="field"><label>Justification for accepting this deviation</label>
          <textarea value={just} onChange={(e) => setJust(e.target.value)} placeholder="Verified in the field on 17-09-2026; the pole sits at the far end of a 400 m road stretch surveyed to a single point." /></div>}
    </Modal>
  );
}

function haversine(lat1: number, lng1: number, lat2: number, lng2: number) {
  const R = 6371000, d = (x: number) => (x * Math.PI) / 180;
  const dLat = d(lat2 - lat1), dLng = d(lng2 - lng1);
  const a = Math.sin(dLat / 2) ** 2 + Math.cos(d(lat1)) * Math.cos(d(lat2)) * Math.sin(dLng / 2) ** 2;
  return 2 * R * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}
