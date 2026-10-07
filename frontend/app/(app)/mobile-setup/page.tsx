"use client";
import { useEffect, useState } from "react";
import { get, post } from "@/lib/api";
import type { AppInfoDto, DeviceDto, EnrolmentDto, MyMobileDto, UserDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { ago, coords, dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Lightbox, Loading, Notice, errText, useAsync, useToast } from "@/components/ui";
import { AuthImg } from "@/components/Controls";

const tone = (s: string) => s === "Active" ? "ok" : s === "AwaitingApproval" || s === "Setup" ? "watch" : "muted";
const qr = (payload: string, size = 6) => `/api/items/qr.png?payload=${encodeURIComponent(payload)}&size=${size}`;

/** Mobile setup (mandatory for Lighting Officers — FR-SEC-009, FR-FLD-007):
 * install the SLIMS Field app, scan the one-time setup QR with it, and take the live test photograph. */
export default function MobileSetupPage() {
  const { user, can } = useAuth();
  const toast = useToast();
  const mine = useAsync(() => get<MyMobileDto>("/api/mobile/mine"), []);
  const app = useAsync(() => get<AppInfoDto>("/api/mobile/app-info"), []);
  const users = useAsync(() => can("configure") ? get<UserDto[]>("/api/users") : Promise.resolve([] as UserDto[]), []);
  const [forUser, setForUser] = useState("");
  const [enrol, setEnrol] = useState<EnrolmentDto | null>(null);
  const [left, setLeft] = useState(0);
  const [zoom, setZoom] = useState<string | null>(null);
  // the address phones use: the configured one, else the address this page was opened by — unless that is localhost,
  // which on a phone means the phone itself, so the server's own LAN address is used instead
  const loc = typeof window !== "undefined" ? window.location : null;
  const local = !!loc && /^(localhost|127\.|\[::1\]$)/.test(loc.hostname);
  const lan = app.data?.lanAddresses?.[0];
  const origin = app.data?.phoneServerUrl || (!loc ? "" : !local ? loc.origin : lan ? `${loc.protocol}//${lan}${loc.port ? ":" + loc.port : ""}` : loc.origin);

  // while a setup QR is on screen, watch for the phone to enrol and pass its checks
  useEffect(() => {
    if (!enrol) return;
    const t = setInterval(() => {
      const s = Math.max(0, Math.round((new Date(enrol.expiresAt).getTime() - Date.now()) / 1000)); setLeft(s);
      if (s === 0) setEnrol(null);
    }, 1000);
    const p = setInterval(() => { mine.reload(); window.dispatchEvent(new Event("slims:mobile")); }, 5000);
    return () => { clearInterval(t); clearInterval(p); };
  }, [enrol, mine]);

  const start = async () => {
    try { const e = await post<EnrolmentDto>("/api/mobile/enrolment", { forUserId: forUser || null, serverUrl: origin }); setEnrol(e); }
    catch (e) { toast(errText(e), true); }
  };
  const revoke = async (d: DeviceDto) => {
    if (!confirm(`Remove ${d.manufacturer ?? ""} ${d.model ?? "this phone"}? It will stop working for SLIMS at once.`)) return;
    try { await post(`/api/mobile/devices/${d.id}/revoke`, { reason: "Removed by the owner" }); mine.reload(); window.dispatchEvent(new Event("slims:mobile")); } catch (e) { toast(errText(e), true); }
  };

  if ((mine.loading && !mine.data) || (app.loading && !app.data)) return <Loading what="your phone" />;
  if (mine.error) return <ErrorBox error={mine.error} />;
  const m = mine.data!;
  const active = m.active;
  const waiting = m.devices.find((d) => d.status === "AwaitingApproval");
  const apkUrl = `${origin}/api/mobile/app.apk`;
  const done1 = !!active || m.devices.length > 0, done2 = m.devices.some((d) => d.status !== "Revoked" && d.status !== "Replaced"), done3 = !!active;

  return (
    <>
      {local && !app.data?.phoneServerUrl && (lan
        ? <Notice kind="watch"><span>You opened the portal as <b>{loc!.host}</b>, which a phone cannot reach. The codes below use this PC&apos;s office address <b>{origin}</b> — the phone must be on the same network.</span></Notice>
        : <Notice kind="crit"><span>You opened the portal as <b>{loc!.host}</b> and this PC has no network address a phone can reach. Connect it to the office network, or set <i>Phone server address</i> under Settings ▸ Parameters.</span></Notice>)}
      {m.required && !active && <Notice kind="crit"><span><b>Mobile setup is required for your role.</b> Installation evidence is accepted only from the SLIMS Field app on your own registered phone — live camera, real GPS, no gallery.</span></Notice>}
      {active && <Notice kind="good"><span>Your phone <b>{active.manufacturer} {active.model}</b> is set up and active. Last seen {ago(active.lastSeenAt)}.</span></Notice>}
      {waiting && <Notice kind="watch"><span>A new phone (<b>{waiting.manufacturer} {waiting.model}</b>) finished setup and is waiting for the System Administrator to approve it. Your current phone keeps working until then.</span></Notice>}

      <div className="split wide">
        <Card title="Set up your phone" sub="About two minutes. Do step 3 outdoors, where the phone gets a good GPS fix.">
          <ol className="steps">
            <li className={done1 ? "done" : ""}>
              <b>Install the SLIMS Field app</b> (Android 10 or newer). Scan this code with the phone&apos;s camera, or open <span className="mono tiny">{apkUrl}</span> in its browser, then allow the installation.
              {app.data?.available ? <div className="setup-qr" style={{ marginTop: 8 }}>
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={qr(apkUrl, 5)} alt="Download link for the SLIMS Field app" style={{ width: 150, height: 150 }} />
                <div className="tiny">SLIMS-Field.apk · {(app.data.size / 1024 / 1024).toFixed(1)} MB{app.data.version ? ` · version ${app.data.version}` : ""}<br />Updated {dateTime(app.data.updated)}<br /><a className="linky" href="/api/mobile/app.apk">Download here</a></div>
              </div> : <Notice kind="watch">The app has not been published on this server yet. Ask the administrator.</Notice>}
            </li>
            <li className={done2 ? "done" : ""}>
              <b>Link the phone to your account.</b> Open the app, tap <i>Scan setup code</i> and point it at the code below. The code works once and expires after a few minutes. Your password never goes onto the phone.
              {can("configure") && <div className="field" style={{ marginTop: 8 }}><label>Set up a phone for</label>
                <select value={forUser} onChange={(e) => setForUser(e.target.value)}><option value="">Myself ({user?.name})</option>
                  {(users.data ?? []).filter((u) => u.active && u.id !== user?.id).map((u) => <option key={u.id} value={u.id}>{u.name} — {u.roleLabel}</option>)}</select></div>}
              <div className="actions" style={{ marginTop: 8 }}><button className="btn primary" onClick={start}>{enrol ? "New setup code" : "Show setup code"}</button></div>
              {enrol && <div className="setup-qr" style={{ marginTop: 10 }}>
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src={qr(enrol.qrPayload, 6)} alt="SLIMS setup code" />
                <div>
                  <div className="tiny">Setup code for <b>{enrol.userName}</b></div>
                  <div className="setup-code">{enrol.code}</div>
                  <div className="tiny">Server {enrol.serverUrl}</div>
                  <div className={left < 60 ? "tiny err" : "tiny"}>Expires in {Math.floor(left / 60)}:{String(left % 60).padStart(2, "0")}</div>
                  <div className="tiny" style={{ marginTop: 6 }}>No camera? In the app choose <i>Enter code</i> and type the server and the code.</div>
                </div>
              </div>}
            </li>
            <li className={done3 ? "done" : ""}>
              <b>Take the setup photograph.</b> The app checks the camera, GPS accuracy, that no mock-location app is running and that the phone clock is correct, then takes a live photo with its fix. When the server confirms the checks, the phone is active.
            </li>
          </ol>
        </Card>

        <Card title="My phones" sub="Only one phone per person is active. A replacement needs the administrator's approval (FR-SEC-009).">
          {m.devices.length === 0 ? <div className="tiny">No phone linked yet.</div> : m.devices.map((d) => (
            <div key={d.id} style={{ borderTop: "1px solid var(--line)", padding: "10px 0", display: "grid", gridTemplateColumns: d.setupPhotoUrl ? "84px 1fr" : "1fr", gap: 10 }}>
              {d.setupPhotoUrl && <AuthImg src={d.setupPhotoUrl} alt="Setup photograph" style={{ width: 84, height: 84, objectFit: "cover", borderRadius: 6 }} onOpen={setZoom} />}
              <div>
                <div><b>{d.manufacturer} {d.model}</b> <Chip tone={tone(d.status)}>{d.status === "AwaitingApproval" ? "Awaiting approval" : d.status}</Chip></div>
                <div className="tiny">Android {d.osVersion ?? "?"} · app {d.appVersion ?? "?"} · enrolled {dateTime(d.enrolledAt)}</div>
                {d.setupCompletedAt && <div className="tiny">Setup photo {dateTime(d.setupCompletedAt)} at {coords(d.setupLat, d.setupLng)} (±{Math.round(d.setupAccuracyM ?? 0)} m)</div>}
                {d.lastSeenAt && <div className="tiny">Last seen {ago(d.lastSeenAt)}</div>}
                {d.note && <div className="tiny">{d.note}</div>}
                {(d.status === "Active" || d.status === "Setup" || d.status === "AwaitingApproval") && <button className="btn small" style={{ marginTop: 6 }} onClick={() => revoke(d)}>Remove this phone</button>}
              </div>
            </div>
          ))}
        </Card>
      </div>
      <Lightbox src={zoom} onClose={() => setZoom(null)} />
    </>
  );
}
