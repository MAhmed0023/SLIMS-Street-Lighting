import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { App as CapApp } from "@capacitor/app";
import { Network } from "@capacitor/network";
import { ApiError, APP_VERSION, api, enrol, forgetPairing, lastSession, loadPairing, serverUrl, session,
  type Bootstrap, type Installation, type Issue, type Item, type Notice, type Session, type SetupCheck, type Site } from "./api";
import { FieldGuard, type DeviceInfo, type Fix } from "./native";
import { LiveCamera } from "./camera";
import { cacheGet, cachePut, discard, enqueue, history, queued, sha256Hex, syncNow, toPhoto, type Queued, type Shot, type Synced } from "./queue";

type Screen = "boot" | "setup" | "checks" | "waiting" | "home" | "install" | "ack" | "report" | "verify" | "queue" | "notices" | "about";
const errText = (e: unknown) => e instanceof Error ? e.message : String(e);
const when = (iso: string) => { const d = new Date(iso); return `${String(d.getDate()).padStart(2, "0")}-${String(d.getMonth() + 1).padStart(2, "0")} ${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`; };
function metres(a: { lat: number; lng: number }, b: { lat: number; lng: number }) {
  const R = 6371000, r = (x: number) => (x * Math.PI) / 180;
  const h = Math.sin(r(b.lat - a.lat) / 2) ** 2 + Math.cos(r(a.lat)) * Math.cos(r(b.lat)) * Math.sin(r(b.lng - a.lng) / 2) ** 2;
  return 2 * R * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h));
}

/** Live GPS from the native plugin. A fix older than 30 s is treated as no fix. */
function useFix(active: boolean) {
  const [fix, setFix] = useState<Fix | null>(null);
  const [err, setErr] = useState<string | null>(null);
  useEffect(() => {
    if (!active) return;
    let handle: { remove: () => Promise<void> } | null = null;
    (async () => {
      try {
        handle = await FieldGuard.addListener("fix", (f) => setFix((cur) => !cur || f.accuracy <= cur.accuracy + 5 || Date.now() - cur.time > 5000 ? f : cur));
        const r = await FieldGuard.startWatch();
        if (!r.gps) setErr("Switch on Location (GPS) in the phone settings.");
      } catch (e) { setErr(errText(e)); }
    })();
    return () => { handle?.remove(); FieldGuard.stopWatch().catch(() => {}); };
  }, [active]);
  const [now, setNow] = useState(Date.now());
  useEffect(() => { const t = setInterval(() => setNow(Date.now()), 2000); return () => clearInterval(t); }, []);
  const fresh = fix && now - fix.time < 30_000 ? fix : null;
  return { fix: fresh, err };
}

async function shotFrom(blob: Blob, f: Fix): Promise<Shot> {
  const bytes = await blob.arrayBuffer();
  return { bytes, sha256: await sha256Hex(bytes), lat: f.lat, lng: f.lng, accuracyM: Math.round(f.accuracy * 10) / 10, capturedAt: new Date().toISOString(), isMock: f.isMock };
}

export default function App() {
  const [screen, setScreen] = useState<Screen>("boot");
  const [boot, setBoot] = useState<Bootstrap | null>(null);
  const [online, setOnline] = useState(true);
  const [q, setQ] = useState<Queued[]>([]);
  const [toast, setToast] = useState<{ text: string; bad?: boolean } | null>(null);
  const [installFor, setInstallFor] = useState<{ item?: Item; replaces?: Installation } | null>(null);
  const [task, setTask] = useState<Installation | null>(null);
  const say = useCallback((text: string, bad?: boolean) => { setToast({ text, bad }); setTimeout(() => setToast(null), bad ? 6000 : 3500); }, []);

  const refreshQueue = useCallback(async () => setQ(await queued()), []);
  const loadBoot = useCallback(async () => {
    try { const b = await api<Bootstrap>("GET", "/api/mobile/bootstrap"); setBoot(b); await cachePut("bootstrap", b); setOnline(true); return b; }
    catch (e) {
      if (e instanceof ApiError && e.status === 0) { setOnline(false); const c = await cacheGet<Bootstrap>("bootstrap"); if (c) setBoot(c); return c ?? null; }
      throw e;
    }
  }, []);
  const sync = useCallback(async (quiet = true) => {
    const r = await syncNow(); await refreshQueue();
    if (r.offline) setOnline(false); else setOnline(true);
    if (!quiet || r.sent || r.failed) say(r.offline ? "Offline — captures stay on the phone and go out automatically." : `${r.sent} sent${r.failed ? `, ${r.failed} refused — see Queue` : ""}`, r.failed > 0);
    if (r.sent) loadBoot().catch(() => {});
  }, [refreshQueue, say, loadBoot]);

  // start-up: paired? active? — then keep the queue flowing on resume, reconnect and every minute
  useEffect(() => {
    (async () => {
      await refreshQueue();
      const p = await loadPairing();
      if (!p.paired) { setScreen("setup"); return; }
      try {
        const s = await session(true);
        if (s.device.status === "Active") { await loadBoot(); setScreen("home"); sync(); }
        else setScreen(s.device.status === "AwaitingApproval" && s.device.setupCompletedAt ? "waiting" : "checks");
      } catch (e) {
        if (e instanceof ApiError && e.status === 0) { setOnline(false); const c = await cacheGet<Bootstrap>("bootstrap"); if (c) { setBoot(c); setScreen("home"); return; } }
        if (e instanceof ApiError && e.status === 401) { await forgetPairing(); setScreen("setup"); say(e.message, true); return; }
        say(errText(e), true); setScreen("setup");
      }
    })();
    const subs: Promise<{ remove: () => Promise<void> }>[] = [
      Network.addListener("networkStatusChange", (s) => { setOnline(s.connected); if (s.connected) sync(); }),
      CapApp.addListener("resume", () => { sync(); }),
    ];
    const t = setInterval(() => sync(), 60_000);
    return () => { clearInterval(t); subs.forEach((p) => p.then((h) => h.remove())); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const go = (s: Screen) => setScreen(s);
  const body = (() => {
    switch (screen) {
      case "boot": return <div className="center"><div className="spinner" /><p>Starting SLIMS Field…</p></div>;
      case "setup": return <SetupScreen say={say} onPaired={() => setScreen("checks")} />;
      case "checks": return <ChecksScreen say={say} onDone={async (status) => { if (status === "Active") { await loadBoot(); setScreen("home"); } else setScreen("waiting"); }} />;
      case "waiting": return <WaitingScreen say={say} onActive={async () => { await loadBoot(); setScreen("home"); }} />;
      case "home": return <HomeScreen boot={boot} online={online} q={q} go={go} reload={() => loadBoot().then(() => say("Updated")).catch((e) => say(errText(e), true))} sync={() => sync(false)}
        onInstall={(item) => { setInstallFor({ item }); setScreen("install"); }} onRecapture={(i) => { setInstallFor({ item: boot?.workList.find((w) => w.id === i.itemId) ?? { id: i.itemId, qrCode: i.qrCode, itemCode: i.itemCode, description: "", serial: 0, status: "AtSite", siteId: i.siteId, siteCode: i.siteCode, siteName: i.siteName, qty: 1, uom: "Nos" } }); setScreen("install"); }}
        onTask={(t) => { setTask(t); setScreen("verify"); }} />;
      case "install": return <InstallScreen boot={boot} start={installFor?.item} online={online} say={say} onClose={() => setScreen("home")}
        onSaved={async () => { await refreshQueue(); setScreen("home"); await sync(false); }} />;
      case "ack": return <AckScreen boot={boot} say={say} onClose={() => { loadBoot().catch(() => {}); setScreen("home"); }} />;
      case "report": return <ReportScreen boot={boot} say={say} onClose={() => setScreen("home")} />;
      case "verify": return task ? <VerifyScreen task={task} say={say} onClose={() => { loadBoot().catch(() => {}); setScreen("home"); }} /> : null;
      case "queue": return <QueueScreen q={q} refresh={refreshQueue} sync={() => sync(false)} onClose={() => setScreen("home")} />;
      case "notices": return <NoticesScreen onClose={() => setScreen("home")} />;
      case "about": return <AboutScreen boot={boot} onClose={() => setScreen("home")} onUnlink={async () => { await forgetPairing(); setBoot(null); setScreen("setup"); }} />;
    }
  })();
  return <div className="app">{body}{toast && <div className={`toast ${toast.bad ? "bad" : ""}`}>{toast.text}</div>}</div>;
}

// ---- setup ------------------------------------------------------------------------------------------------------
function SetupScreen({ say, onPaired }: { say: (t: string, bad?: boolean) => void; onPaired: () => void }) {
  const [scan, setScan] = useState(false);
  const [manual, setManual] = useState(false);
  const [server, setServer] = useState("http://"); const [code, setCode] = useState("");
  const [busy, setBusy] = useState(false);
  const pair = async (s: string, c: string) => {
    setBusy(true);
    try {
      const info = await FieldGuard.info();
      const r = await enrol(s, c.trim().toUpperCase(), info);
      say(`Linked to ${r.userName}`); onPaired();
    } catch (e) { say(errText(e), true); } finally { setBusy(false); }
  };
  const onCode = useCallback((text: string) => {
    setScan(false);
    try { const p = JSON.parse(text) as { t?: string; s?: string; c?: string }; if (p.t !== "slims-setup" || !p.s || !p.c) throw new Error(); pair(p.s, p.c); }
    catch { say("That is not a SLIMS setup code. Open Mobile Setup on the portal and scan the code shown there.", true); }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  if (scan) return <LiveCamera mode="scan" title="Scan the setup code" hint="Point the camera at the code on the portal's Mobile Setup page" onCode={onCode} onClose={() => setScan(false)} />;
  return (
    <div className="page">
      <header className="hero"><img src="./icon.png" alt="" /><h1>SLIMS Field</h1><p>Street Lighting Store &amp; Installation — field evidence</p></header>
      <div className="card">
        <h2>Set up this phone</h2>
        <ol className="steps">
          <li>On a computer, sign in to the SLIMS portal and open <b>Settings ▸ Mobile Setup</b>.</li>
          <li>Tap <b>Show setup code</b> there, then scan it here.</li>
          <li>Allow camera and location, and take the setup photograph outdoors.</li>
        </ol>
        <button className="btn primary big" disabled={busy} onClick={() => setScan(true)}>{busy ? "Linking…" : "Scan setup code"}</button>
        <button className="btn link" onClick={() => setManual((m) => !m)}>Enter the code instead</button>
        {manual && <div className="form">
          <label>Server address<input value={server} onChange={(e) => setServer(e.target.value)} inputMode="url" autoCapitalize="none" placeholder="http://172.16.10.184:3001" /></label>
          <label>Setup code<input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} autoCapitalize="characters" placeholder="8 letters and digits" /></label>
          <button className="btn primary" disabled={busy || code.length < 6 || server.length < 10} onClick={() => pair(server, code)}>Link this phone</button>
        </div>}
        <p className="small">The phone never stores your password. It gets its own key, which the administrator can revoke at any time.</p>
      </div>
    </div>
  );
}

function ChecksScreen({ say, onDone }: { say: (t: string, bad?: boolean) => void; onDone: (status: string) => void }) {
  const [info, setInfo] = useState<DeviceInfo | null>(null);
  const [perm, setPerm] = useState<{ location: string; camera: string } | null>(null);
  const [s, setS] = useState<Session | null>(lastSession());
  const [drift, setDrift] = useState<number | null>(null);
  const [cam, setCam] = useState(false);
  const [result, setResult] = useState<SetupCheck | null>(null);
  const [busy, setBusy] = useState(false);
  const { fix, err } = useFix(perm?.location === "granted");
  useEffect(() => {
    (async () => {
      setInfo(await FieldGuard.info());
      setPerm(await FieldGuard.checkPermissions());
      const t0 = Date.now(); const ses = await session(true); setS(ses);
      setDrift(Math.round((Date.now() - (new Date(ses.serverTime).getTime() + (Date.now() - t0) / 2)) / 1000));
    })().catch((e) => say(errText(e), true));
  }, [say]);
  const maxAcc = s?.field.setupMaxAccuracyM ?? 60;
  const rows = [
    { k: "Camera and location allowed", ok: perm?.camera === "granted" && perm?.location === "granted", d: perm ? `camera ${perm.camera}, location ${perm.location}` : "checking…" },
    { k: "GPS fix", ok: !!fix && fix.accuracy <= maxAcc, d: err ?? (fix ? `±${Math.round(fix.accuracy)} m (${fix.provider}) — needs ±${maxAcc} m` : "waiting for satellites… step outside") },
    { k: "Real location (no mock app)", ok: !!fix && !fix.isMock, d: fix?.isMock ? "a mock-location app is active — remove it" : fix ? "genuine" : "—" },
    { k: "Automatic date & time", ok: !!info?.autoTime, d: info?.autoTime ? "on" : "switch on in Settings ▸ Date & time" },
    { k: "Phone clock", ok: drift !== null && Math.abs(drift) <= (s?.geo.maxClockDriftMinutes ?? 5) * 60, d: drift === null ? "checking…" : `${Math.abs(drift)} s ${drift > 0 ? "ahead of" : "behind"} the server` },
  ];
  const ready = rows.every((r) => r.ok);
  const photo = async (blob: Blob) => {
    setCam(false); if (!fix) return;
    setBusy(true);
    try {
      const shot = await shotFrom(blob, fix);
      const r = await api<SetupCheck>("POST", "/api/mobile/setup-check", { photo: toPhoto(shot), deviceTime: new Date().toISOString(), autoTimeEnabled: !!info?.autoTime });
      setResult(r);
      if (r.ok) say(r.device.status === "Active" ? "Setup complete — this phone is active" : "Setup complete — waiting for the administrator to approve the new phone");
    } catch (e) { say(errText(e), true); } finally { setBusy(false); }
  };
  if (cam) return <LiveCamera mode="photo" title="Setup photograph" hint="Take any photograph outdoors — it proves the camera and GPS work" onPhoto={photo} onClose={() => setCam(false)}
    overlay={fix && <div className="fixbadge">±{Math.round(fix.accuracy)} m {fix.isMock && "· MOCK"}</div>} />;
  return (
    <div className="page">
      <div className="bar"><b>Phone checks</b><span>{s?.user.name}</span></div>
      <div className="card">
        <h2>Almost done</h2>
        <p className="small">Linked to <b>{s?.user.name}</b> ({s?.user.roleLabel}). These checks run again on the server.</p>
        {perm && (perm.camera !== "granted" || perm.location !== "granted") &&
          <button className="btn primary big" onClick={async () => { try { setPerm(await FieldGuard.requestPermissions()); } catch (e) { say(errText(e), true); } }}>Allow camera and location</button>}
        <ul className="checks">{rows.map((r) => <li key={r.k} className={r.ok ? "ok" : "bad"}><span>{r.ok ? "✓" : "•"}</span><div><b>{r.k}</b><small>{r.d}</small></div></li>)}</ul>
        <button className="btn primary big" disabled={!ready || busy} onClick={() => setCam(true)}>{busy ? "Checking on the server…" : "Take setup photograph"}</button>
        {result && <>
          <ul className="checks">{result.checks.map((c) => <li key={c.check} className={c.ok ? "ok" : "bad"}><span>{c.ok ? "✓" : "✕"}</span><div><b>{c.check}</b><small>{c.detail}</small></div></li>)}</ul>
          {result.ok && <button className="btn primary big" onClick={() => onDone(result.device.status)}>Continue</button>}
        </>}
      </div>
    </div>
  );
}

function WaitingScreen({ say, onActive }: { say: (t: string, bad?: boolean) => void; onActive: () => void }) {
  const [busy, setBusy] = useState(false);
  return (
    <div className="page"><div className="card center">
      <h2>Waiting for approval</h2>
      <p>This is a new phone for your account. The System Administrator has to approve it before it can record evidence. Your previous phone keeps working until then.</p>
      <button className="btn primary big" disabled={busy} onClick={async () => {
        setBusy(true);
        try { const s = await session(true); if (s.device.status === "Active") onActive(); else say("Not approved yet."); } catch (e) { say(errText(e), true); } finally { setBusy(false); }
      }}>Check again</button>
    </div></div>
  );
}

// ---- home -------------------------------------------------------------------------------------------------------
function HomeScreen({ boot, online, q, go, reload, sync, onInstall, onRecapture, onTask }: {
  boot: Bootstrap | null; online: boolean; q: Queued[]; go: (s: Screen) => void; reload: () => void; sync: () => void;
  onInstall: (i?: Item) => void; onRecapture: (i: Installation) => void; onTask: (t: Installation) => void;
}) {
  const bySite = useMemo(() => {
    const m = new Map<string, Item[]>();
    for (const i of boot?.workList ?? []) { const k = i.siteCode ?? "?"; m.set(k, [...(m.get(k) ?? []), i]); }
    return [...m.entries()];
  }, [boot]);
  const failed = q.filter((x) => x.status === "failed").length;
  const canInstall = !!boot?.user.permissions.install;
  return (
    <div className="page">
      <div className="bar">
        <div><b>SLIMS Field</b><small>{boot?.user.name} · {boot?.user.roleLabel}</small></div>
        <div className="pills">
          <span className={`pill ${online ? "on" : "off"}`}>{online ? "Online" : "Offline"}</span>
          <button className={`pill ${failed ? "off" : q.length ? "warn" : ""}`} onClick={() => go("queue")}>{q.length} queued</button>
          <button className="pill" onClick={() => go("notices")}>🔔 {boot?.unread ?? 0}</button>
          <button className="pill" onClick={() => go("about")}>⋯</button>
        </div>
      </div>
      <div className="tiles">
        {canInstall && <button className="tile primary" onClick={() => onInstall()}><b>Record installation</b><small>Scan the item, before and after photographs</small></button>}
        {(boot?.toAcknowledge.length ?? 0) > 0 && <button className="tile" onClick={() => go("ack")}><b>Confirm material at site</b><small>{boot!.toAcknowledge.length} issue(s) waiting</small></button>}
        {(boot?.recapture.length ?? 0) > 0 && <div className="tile warn"><b>Re-capture required</b>{boot!.recapture.map((r) => <button key={r.id} className="row" onClick={() => onRecapture(r)}>{r.itemCode} at {r.siteCode}<small>{r.justification ?? "photographs failed the integrity check"}</small></button>)}</div>}
        {(boot?.verificationTasks.length ?? 0) > 0 && <div className="tile warn"><b>Field verification</b>{boot!.verificationTasks.map((t) => <button key={t.id} className="row" onClick={() => onTask(t)}>{t.number} · {t.siteCode}<small>{t.fvRequestNote ?? `${Math.round(t.distanceM)} m from the site point`}</small></button>)}</div>}
        {canInstall && <button className="tile" onClick={() => go("report")}><b>Report out-of-order item</b><small>Removed or failed at site</small></button>}
      </div>
      <div className="section"><h3>My work list</h3><span className="spacer" /><button className="btn small" onClick={reload}>Refresh</button><button className="btn small" onClick={sync}>Sync now</button></div>
      {bySite.length === 0 && <p className="small pad">Nothing issued to your sites is waiting to be installed.</p>}
      {bySite.map(([site, items]) => (
        <div className="card list" key={site}>
          <div className="list-head"><b>{site}</b><small>{items[0].siteName}</small></div>
          {items.map((i) => <button key={i.id} className="row" disabled={!canInstall} onClick={() => onInstall(i)}><span className="mono">{i.itemCode} #{i.serial}</span><small>{i.description} · {i.status}</small></button>)}
        </div>
      ))}
      {!online && <p className="small pad">Offline — showing the work list from the last sync. Captures are kept on the phone and sent when the connection returns.</p>}
    </div>
  );
}

// ---- installation capture --------------------------------------------------------------------------------------
function InstallScreen({ boot, start, online, say, onClose, onSaved }: { boot: Bootstrap | null; start?: Item; online: boolean; say: (t: string, bad?: boolean) => void; onClose: () => void; onSaved: () => void }) {
  const { fix, err } = useFix(true);
  const [item, setItem] = useState<Item | null>(start ?? null);
  const [cam, setCam] = useState<null | "scan" | "before" | "after">(start ? null : "scan");
  const [before, setBefore] = useState<Shot | null>(null);
  const [after, setAfter] = useState<Shot | null>(null);
  const [pole, setPole] = useState(""); const [remarks, setRemarks] = useState("");
  const [busy, setBusy] = useState(false);
  const site: Site | undefined = boot?.sites.find((s) => s.id === item?.siteId);
  const geo = boot?.geo;
  const dist = fix && site ? metres(fix, site) : null;
  const allowed = site && geo ? site.geofenceM + geo.toleranceM : null;
  const maxAcc = (geo?.minAccuracyM ?? 25) * 3;
  const fixOk = !!fix && !fix.isMock && fix.accuracy <= maxAcc;
  const onCode = useCallback((text: string) => {
    setCam(null);
    const hit = boot?.workList.find((w) => w.qrCode === text.trim());
    if (hit) setItem(hit); else say(text.startsWith("SLIMS|") ? "This item is not issued to your sites (or is already installed)." : "Not a SLIMS item label.", true);
  }, [boot, say]);
  const take = async (blob: Blob) => {
    const kind = cam; setCam(null);
    if (!fix) { say("No GPS fix — try again.", true); return; }
    const s = await shotFrom(blob, fix);
    if (kind === "before") setBefore(s); else setAfter(s);
  };
  const save = async () => {
    if (!item || !before || !after) return;
    setBusy(true);
    try {
      const rec: Queued = { clientTxnId: crypto.randomUUID(), qr: item.qrCode, itemCode: item.itemCode, serial: item.serial, siteCode: item.siteCode ?? "", poleId: pole.trim(), remarks: remarks.trim(), replacesQr: null,
        before, after, queuedAt: new Date().toISOString(), queuedOffline: !online, status: "queued", attempts: 0 };
      await enqueue(rec);
      say(online ? "Saved — sending…" : "Saved on the phone — it will be sent when you are online");
      onSaved();
    } catch (e) { say(errText(e), true); } finally { setBusy(false); }
  };
  const overlay = <div className="fixbadge">{fix ? <>±{Math.round(fix.accuracy)} m{dist != null && allowed != null && <> · {Math.round(dist)} m {dist <= allowed ? "✓ inside" : "✕ outside"}</>}{fix.isMock && " · MOCK"}</> : "No GPS fix"}</div>;
  if (cam === "scan") return <LiveCamera mode="scan" title="Scan the item's QR label" hint="At the pole, point the camera at the label on the luminaire or pole" onCode={onCode} onClose={() => { setCam(null); if (!item) onClose(); }} />;
  if (cam) return <LiveCamera mode="photo" title={cam === "before" ? "BEFORE installation" : "AFTER installation"} hint={fixOk ? "Frame the pole and fitting, then press the button" : "Waiting for a good GPS fix…"} overlay={overlay} onPhoto={take} onClose={() => setCam(null)} />;
  return (
    <div className="page">
      <div className="bar"><button className="ghost" onClick={onClose}>‹ Back</button><b>Record installation</b><span /></div>
      <div className="card">
        {item ? <div className="item"><b className="mono">{item.itemCode} #{item.serial}</b><small>{item.description}</small><small>Site {item.siteCode} — {item.siteName}</small><button className="btn small" onClick={() => { setItem(null); setBefore(null); setAfter(null); setCam("scan"); }}>Change</button></div>
          : <button className="btn primary big" onClick={() => setCam("scan")}>Scan the item&apos;s QR</button>}
        <div className={`gps ${fixOk ? (dist != null && allowed != null && dist > allowed ? "warn" : "ok") : "bad"}`}>
          {err ? err : !fix ? "Waiting for GPS…" : fix.isMock ? "Mock location detected — evidence will be refused. Remove the location-faking app." :
            <>GPS ±{Math.round(fix.accuracy)} m{fix.accuracy > maxAcc && ` — too poor (limit ±${maxAcc} m)`}{dist != null && allowed != null && <> · {Math.round(dist)} m from the site point ({dist <= allowed ? "inside" : "outside"} the {allowed} m limit)</>}</>}
        </div>
        <div className="form">
          <label>Pole / point number<input value={pole} onChange={(e) => setPole(e.target.value)} placeholder="P-014" /></label>
          <label>Remarks<input value={remarks} onChange={(e) => setRemarks(e.target.value)} placeholder="Optional" /></label>
        </div>
        <div className="shots">
          {(["before", "after"] as const).map((k) => { const s = k === "before" ? before : after; return (
            <button key={k} className={`shot ${s ? "done" : ""}`} disabled={!item || !fixOk || (k === "after" && !before)} onClick={() => setCam(k)}>
              {s ? <img src={URL.createObjectURL(new Blob([s.bytes], { type: "image/jpeg" }))} alt={k} /> : <span>📷</span>}
              <b>{k === "before" ? "Before" : "After"}</b><small>{s ? `±${Math.round(s.accuracyM)} m · ${when(s.capturedAt)}` : k === "after" && !before ? "take BEFORE first" : "tap to capture"}</small>
            </button>); })}
        </div>
        <p className="small">Live camera only. Each photograph records its own GPS fix and time, and its fingerprint (SHA-256) is checked by the server.</p>
        <button className="btn primary big" disabled={!item || !before || !after || busy} onClick={save}>{busy ? "Saving…" : "Save installation"}</button>
      </div>
    </div>
  );
}

// ---- acknowledgement, returns, verification ------------------------------------------------------------------------
function AckScreen({ boot, say, onClose }: { boot: Bootstrap | null; say: (t: string, bad?: boolean) => void; onClose: () => void }) {
  const { fix } = useFix(true);
  const [done, setDone] = useState<number[]>([]);
  const ack = async (i: Issue) => {
    try { await api("POST", "/api/mobile/acknowledge", { issueId: i.id, lat: fix?.lat ?? null, lng: fix?.lng ?? null, accuracyM: fix?.accuracy ?? null }); setDone((d) => [...d, i.id]); say(`${i.number} acknowledged`); }
    catch (e) { say(errText(e), true); }
  };
  return (
    <div className="page">
      <div className="bar"><button className="ghost" onClick={onClose}>‹ Back</button><b>Material received at site</b><span /></div>
      <p className="small pad">Confirm when the material on the gate pass has reached the site. Your position is recorded with the confirmation.</p>
      {(boot?.toAcknowledge ?? []).map((i) => <div key={i.id} className="card list">
        <div className="list-head"><b>{i.number}</b><small>Gate pass {i.gatePassNo} · {i.storeCode} → {i.siteCode} · {i.itemCount} label(s) · {when(i.issuedAt)}</small></div>
        <button className="btn primary" disabled={done.includes(i.id)} onClick={() => ack(i)}>{done.includes(i.id) ? "Acknowledged ✓" : `Confirm receipt${fix ? ` (±${Math.round(fix.accuracy)} m)` : ""}`}</button>
      </div>)}
    </div>
  );
}

function ReportScreen({ boot, say, onClose }: { boot: Bootstrap | null; say: (t: string, bad?: boolean) => void; onClose: () => void }) {
  const [scan, setScan] = useState(true);
  const [qr, setQr] = useState(""); const [reason, setReason] = useState(""); const [kind, setKind] = useState("OutOfOrder"); const [remarks, setRemarks] = useState("");
  const [busy, setBusy] = useState(false);
  const onCode = useCallback((t: string) => { setScan(false); if (!t.startsWith("SLIMS|")) { say("Not a SLIMS item label.", true); return; } setQr(t); }, [say]);
  if (scan) return <LiveCamera mode="scan" title="Scan the failed item" onCode={onCode} onClose={() => { setScan(false); if (!qr) onClose(); }} />;
  return (
    <div className="page">
      <div className="bar"><button className="ghost" onClick={onClose}>‹ Back</button><b>Report out-of-order item</b><span /></div>
      <div className="card">
        <div className="item"><b className="mono">{qr.split("|")[1] ?? "—"} #{Number(qr.split("|")[2] ?? 0)}</b><button className="btn small" onClick={() => setScan(true)}>Scan again</button></div>
        <div className="form">
          <label>What happened<select value={kind} onChange={(e) => setKind(e.target.value)}><option value="OutOfOrder">Failed / out of order</option><option value="Dismantled">Dismantled</option></select></label>
          <label>Reason<select value={reason} onChange={(e) => setReason(e.target.value)}><option value="">— choose —</option>{(boot?.failureReasons ?? []).map((r) => <option key={r}>{r}</option>)}</select></label>
          <label>Remarks<input value={remarks} onChange={(e) => setRemarks(e.target.value)} /></label>
        </div>
        <button className="btn primary big" disabled={!qr || !reason || busy} onClick={async () => {
          setBusy(true);
          try { const r = await api<{ number: string }>("POST", "/api/mobile/returns", { qr, reason, kind, remarks: remarks || null }); say(`${r.number} recorded — bring the item back to the store`); onClose(); }
          catch (e) { say(errText(e), true); } finally { setBusy(false); }
        }}>Report</button>
      </div>
    </div>
  );
}

function VerifyScreen({ task, say, onClose }: { task: Installation; say: (t: string, bad?: boolean) => void; onClose: () => void }) {
  const { fix } = useFix(true);
  const [cam, setCam] = useState(false);
  const [shots, setShots] = useState<Shot[]>([]);
  const [text, setText] = useState(""); const [busy, setBusy] = useState(false);
  const dist = fix ? metres(fix, { lat: task.siteLat, lng: task.siteLng }) : null;
  if (cam) return <LiveCamera mode="photo" title="Verification photograph" onPhoto={async (b) => { setCam(false); if (!fix) { say("No GPS fix — try again.", true); return; } const s = await shotFrom(b, fix); setShots((x) => [...x, s].slice(0, 3)); }} onClose={() => setCam(false)}
    overlay={fix && <div className="fixbadge">±{Math.round(fix.accuracy)} m · {dist != null ? `${Math.round(dist)} m from site` : ""}</div>} />;
  return (
    <div className="page">
      <div className="bar"><button className="ghost" onClick={onClose}>‹ Back</button><b>Field verification</b><span /></div>
      <div className="card">
        <div className="item"><b>{task.number}</b><small>{task.itemCode} at {task.siteCode} — {task.siteName}</small><small>Captured {Math.round(task.distanceM)} m from the site point by {task.officer}</small>{task.fvRequestNote && <small>Asked by {task.fvRequestedBy}: “{task.fvRequestNote}”</small>}</div>
        <div className={`gps ${fix ? "ok" : "bad"}`}>{fix ? `You are ${dist != null ? Math.round(dist) : "?"} m from the site point (±${Math.round(fix.accuracy)} m)` : "Waiting for GPS…"}</div>
        <div className="form"><label>Findings<textarea value={text} onChange={(e) => setText(e.target.value)} placeholder="What you found on the ground" /></label></div>
        <div className="shots">{shots.map((s, i) => <div key={i} className="shot done"><img src={URL.createObjectURL(new Blob([s.bytes], { type: "image/jpeg" }))} alt="" /><small>±{Math.round(s.accuracyM)} m</small></div>)}
          {shots.length < 3 && <button className="shot" disabled={!fix || fix.isMock} onClick={() => setCam(true)}><span>📷</span><small>add photograph</small></button>}</div>
        <button className="btn primary big" disabled={busy || text.trim().length < 10} onClick={async () => {
          setBusy(true);
          try { await api("POST", `/api/mobile/findings/${task.id}`, { findings: text.trim(), photos: shots.map(toPhoto), deviceTime: new Date().toISOString() }); say("Findings recorded"); onClose(); }
          catch (e) { say(errText(e), true); } finally { setBusy(false); }
        }}>Submit findings</button>
      </div>
    </div>
  );
}

// ---- queue, notices, about ---------------------------------------------------------------------------------------------
function QueueScreen({ q, refresh, sync, onClose }: { q: Queued[]; refresh: () => void; sync: () => void; onClose: () => void }) {
  const [done, setDone] = useState<Synced[]>([]);
  useEffect(() => { history().then(setDone); }, [q]);
  return (
    <div className="page">
      <div className="bar"><button className="ghost" onClick={onClose}>‹ Back</button><b>Sync queue</b><button className="ghost" onClick={sync}>Sync</button></div>
      <div className="section"><h3>Waiting on the phone</h3></div>
      {q.length === 0 && <p className="small pad">Nothing waiting. Everything has reached the server.</p>}
      {q.map((x) => <div key={x.clientTxnId} className={`card list ${x.status === "failed" ? "failed" : ""}`}>
        <div className="list-head"><b className="mono">{x.itemCode} #{x.serial}</b><small>{x.siteCode} · captured {when(x.after.capturedAt)}{x.queuedOffline ? " · offline" : ""}</small></div>
        {x.error && <small className={x.status === "failed" ? "err" : ""}>{x.error}</small>}
        {x.status === "failed" && <div className="row2">
          <button className="btn small" onClick={async () => { await enqueue({ ...x, status: "queued", error: undefined }); refresh(); sync(); }}>Try again</button>
          <button className="btn small" onClick={async () => { if (confirm("Delete this capture from the phone?")) { await discard(x.clientTxnId); refresh(); } }}>Delete</button></div>}
      </div>)}
      <div className="section"><h3>Sent</h3></div>
      {done.map((d) => <div key={d.clientTxnId} className="card list"><div className="list-head"><b className="mono">{d.number}</b><small>{d.itemCode} #{d.serial} · {d.siteCode} · {d.geoStatus} ({Math.round(d.distanceM)} m) · sent {when(d.syncedAt)}</small></div></div>)}
    </div>
  );
}

function NoticesScreen({ onClose }: { onClose: () => void }) {
  const [list, setList] = useState<Notice[] | null>(null);
  const [err, setErr] = useState<string | null>(null);
  useEffect(() => { api<Notice[]>("GET", "/api/notifications?take=50").then(setList).catch((e) => setErr(errText(e))); api("POST", "/api/notifications/read", { id: null }).catch(() => {}); }, []);
  return (
    <div className="page">
      <div className="bar"><button className="ghost" onClick={onClose}>‹ Back</button><b>Notifications</b><span /></div>
      {err && <p className="small pad err">{err}</p>}
      {(list ?? []).map((n) => <div key={n.id} className={`card list ${n.readAt ? "" : "unread"}`}><div className="list-head"><b>{n.title}</b><small>{when(n.createdAt)} · {n.code}</small></div><small className="pre">{n.body}</small></div>)}
      {list?.length === 0 && <p className="small pad">No notifications.</p>}
    </div>
  );
}

function AboutScreen({ boot, onClose, onUnlink }: { boot: Bootstrap | null; onClose: () => void; onUnlink: () => void }) {
  const tap = useRef(0);
  return (
    <div className="page">
      <div className="bar"><button className="ghost" onClick={onClose}>‹ Back</button><b>This phone</b><span /></div>
      <div className="card">
        <dl className="kv">
          <dt>User</dt><dd>{boot?.user.name} ({boot?.user.id})</dd>
          <dt>Role</dt><dd>{boot?.user.roleLabel}</dd>
          <dt>Phone</dt><dd>{boot?.device.manufacturer} {boot?.device.model}</dd>
          <dt>Status</dt><dd>{boot?.device.status}</dd>
          <dt>Server</dt><dd className="mono">{serverUrl()}</dd>
          <dt>App version</dt><dd onClick={() => { tap.current++; }}>{APP_VERSION}</dd>
        </dl>
        <button className="btn danger" onClick={() => { if (confirm("Unlink this phone from SLIMS? You will need a new setup code from the portal.")) onUnlink(); }}>Unlink this phone</button>
      </div>
    </div>
  );
}
