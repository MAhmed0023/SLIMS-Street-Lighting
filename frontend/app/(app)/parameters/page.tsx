"use client";
import { useEffect, useState } from "react";
import { get, post, put } from "@/lib/api";
import type { AppConfig, NotifySettings, RoleDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { Card, Notice, errText, useAsync, useToast } from "@/components/ui";

const lines = (v: string) => v.split("\n").map((s) => s.trim()).filter(Boolean);

export default function ParametersPage() {
  const { config, can, reload } = useAuth();
  const toast = useToast();
  const [f, setF] = useState<AppConfig | null>(config);
  const [busy, setBusy] = useState(false);
  useEffect(() => { setF(config); }, [config]);
  if (!can("configure")) return <Notice kind="crit">Only a configuring role may change application parameters.</Notice>;
  if (!f) return <Notice kind="info">Loading parameters…</Notice>;

  const G = (k: keyof AppConfig["geo"]) => ({
    type: "number", value: String(f.geo[k]),
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, geo: { ...f.geo, [k]: +e.target.value } }),
  });
  const C = (k: keyof AppConfig["controls"]) => ({
    type: "number", value: String(f.controls[k]),
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, controls: { ...f.controls, [k]: +e.target.value } }),
  });
  const A = (k: keyof AppConfig["alerts"]) => ({
    type: "number", value: String(f.alerts?.[k] ?? ""),
    onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, alerts: { ...f.alerts, [k]: +e.target.value } }),
  });
  const levels = [...(f.approvalLevels ?? [])].sort((a, b) => a.level - b.level);

  return (
    <>
      <Notice kind="info"><span>
        These values are enforced by the server, not the browser. Changing them affects new transactions only — records already posted keep the
        limits that applied when they were created.
      </span></Notice>

      <div className="grid g-2">
        <Card title="Geo-tagging and evidence" sub="Section 13 of the BRD — what makes an installation acceptable">
          <div className="form">
            <div className="frow">
              <div className="field"><label>Default site geo-fence radius (m)</label><input {...G("defaultGeofenceM")} />
                <span className="hint">Applied to new sites; each site can override it</span></div>
              <div className="field"><label>Geo-fence tolerance (m)</label><input {...G("toleranceM")} />
                <span className="hint">Allowance for GPS drift in built-up areas</span></div>
            </div>
            <div className="frow">
              <div className="field"><label>Minimum GPS accuracy (m)</label><input {...G("minAccuracyM")} />
                <span className="hint">The field app warns below this; captures worse than 3× are refused</span></div>
              <div className="field"><label>Maximum clock drift (minutes)</label><input {...G("maxClockDriftMinutes")} />
                <span className="hint">Beyond this an online submission is rejected and logged</span></div>
            </div>
          </div>
        </Card>

        <Card title="Operational controls" sub="Timing and tolerance thresholds used across the process">
          <div className="form">
            <div className="frow">
              <div className="field"><label>Return period after removal (days)</label><input {...C("returnDays")} />
                <span className="hint">Beyond this the item is escalated as not returned (BR-029)</span></div>
              <div className="field"><label>Reservation validity (days)</label><input {...C("reservationDays")} /></div>
            </div>
            <div className="frow">
              <div className="field"><label>Acknowledgement overdue (hours)</label><input {...C("ackOverdueHours")} />
                <span className="hint">Issued but unacknowledged material is flagged after this</span></div>
              <div className="field"><label>Receipt quantity tolerance (%)</label><input {...C("receiptTolerancePct")} />
                <span className="hint">0 means a receipt may never exceed the PO line (BR-014)</span></div>
            </div>
            <div className="frow">
              <div className="field"><label>Stock ageing threshold (days)</label><input {...C("ageingDays")} /></div>
              <div className="field"><label>Evidence photograph limit (MB)</label><input {...C("evidenceMaxMB")} /></div>
            </div>
          </div>
        </Card>
      </div>

      <div className="grid g-2">
        <Card title="Sign-in" sub="How users authenticate">
          <div className="form">
            <div className="field"><label>Password required</label>
              <select value={f.loginRequired ? "1" : "0"} onChange={(e) => setF({ ...f, loginRequired: e.target.value === "1" })}>
                <option value="1">Yes — every account needs a password</option>
                <option value="0">No — accounts without a password may sign in with the ID alone</option>
              </select>
              <span className="hint">Switching this off is for a closed LAN pilot only; it weakens attribution.</span></div>
            <div className="field"><label>Show the user picker on the sign-in card</label>
              <select value={f.showUserPicker ? "1" : "0"} onChange={(e) => setF({ ...f, showUserPicker: e.target.value === "1" })}>
                <option value="1">Yes</option><option value="0">No</option>
              </select></div>
          </div>
        </Card>

        <Card title="Vocabulary" sub="Zones and failure reasons offered in the field app">
          <div className="form">
            <div className="field"><label>Zones (one per line)</label>
              <textarea value={f.zones.join("\n")} onChange={(e) => setF({ ...f, zones: e.target.value.split("\n").map((s) => s.trim()).filter(Boolean) })} /></div>
            <div className="field"><label>Failure reasons (one per line)</label>
              <textarea value={f.failureReasons.join("\n")} onChange={(e) => setF({ ...f, failureReasons: e.target.value.split("\n").map((s) => s.trim()).filter(Boolean) })} /></div>
          </div>
        </Card>
      </div>

      <div className="grid g-2">
        <Card title="Approval matrix (FR-REQ-010)" sub="A requisition goes to the first level whose limit covers its value. Below that level an approval is a recommendation that escalates.">
          <div className="form">
            {levels.map((l, i) => (
              <div className="frow" key={i}>
                <div className="field" style={{ maxWidth: 70 }}><label>Level</label><input type="number" value={l.level} onChange={(e) => { const n = [...levels]; n[i] = { ...l, level: +e.target.value }; setF({ ...f, approvalLevels: n }); }} /></div>
                <div className="field"><label>Role</label><RoleSelect value={l.roleKey} onChange={(v) => { const n = [...levels]; n[i] = { ...l, roleKey: v }; setF({ ...f, approvalLevels: n }); }} /></div>
                <div className="field"><label>Label</label><input value={l.label} onChange={(e) => { const n = [...levels]; n[i] = { ...l, label: e.target.value }; setF({ ...f, approvalLevels: n }); }} /></div>
                <div className="field"><label>Limit (Rs.; blank = none)</label><input type="number" value={l.maxValue ?? ""} onChange={(e) => { const n = [...levels]; n[i] = { ...l, maxValue: e.target.value === "" ? null : +e.target.value }; setF({ ...f, approvalLevels: n }); }} /></div>
                <button className="btn small" style={{ alignSelf: "end" }} onClick={() => setF({ ...f, approvalLevels: levels.filter((_, j) => j !== i) })}>×</button>
              </div>
            ))}
            <div className="actions"><button className="btn small" onClick={() => setF({ ...f, approvalLevels: [...levels, { level: (levels.at(-1)?.level ?? 0) + 1, roleKey: "pdoffice", label: "", maxValue: null }] })}>Add level</button></div>
          </div>
        </Card>

        <Card title="Alerts and escalation (FR-NTF-003/004)" sub="When work not actioned escalates to the next level, and the thresholds of the configurable alerts">
          <div className="form">
            <div className="frow"><div className="field"><label>Approval escalates after (h)</label><input {...A("approvalEscalateHours")} /></div><div className="field"><label>Requisition turnaround alert (h)</label><input {...A("turnaroundHours")} /></div></div>
            <div className="frow"><div className="field"><label>GPS exception escalates after (days)</label><input {...A("exceptionEscalateDays")} /></div><div className="field"><label>Re-capture escalates after (days)</label><input {...A("recaptureEscalateDays")} /></div></div>
            <div className="frow"><div className="field"><label>Installation pending alert (days)</label><input {...A("installPendingDays")} /></div><div className="field"><label>Unresolved site alert (days)</label><input {...A("unresolvedSiteDays")} /></div></div>
            <div className="frow"><div className="field"><label>Repeated GPS mismatch — count</label><input {...A("gpsMismatchCount")} /></div><div className="field"><label>… within (days)</label><input {...A("gpsMismatchDays")} /></div></div>
            <div className="frow"><div className="field"><label>Unusual return rate (%)</label><input {...A("returnRatePct")} /></div><div className="field"><label>Checks run every (minutes)</label><input {...A("workerMinutes")} /></div></div>
            <div className="field"><label>Escalation chain (role → next role, one per line)</label>
              <textarea value={Object.entries(f.escalateTo ?? {}).map(([k, v]) => `${k} → ${v}`).join("\n")} onChange={(e) => setF({ ...f, escalateTo: Object.fromEntries(lines(e.target.value).map((x) => x.split(/→|->/).map((s) => s.trim())).filter((p) => p.length === 2 && p[0] && p[1])) })} /></div>
          </div>
        </Card>
      </div>

      <div className="grid g-2">
        <Card title="Mobile field app (FR-FLD-007, FR-SEC-009)" sub="Phone binding and field capture rules">
          <div className="form">
            <div className="field"><label>Phone server address</label>
              <input placeholder="Blank = this PC's office address, e.g. http://172.16.10.184:3001" value={f.field?.phoneServerUrl ?? ""} onChange={(e) => setF({ ...f, field: { ...f.field, phoneServerUrl: e.target.value } })} />
              <span className="tiny muted">The address phones open for the app download and setup code. Set it when SLIMS is reached by a domain name.</span></div>
            <div className="field"><label>Mobile setup mandatory for Lighting Officers</label>
              <select value={f.field?.mobileSetupRequired ? "1" : "0"} onChange={(e) => setF({ ...f, field: { ...f.field, mobileSetupRequired: e.target.value === "1" } })}><option value="1">Yes — officers must bind a phone first</option><option value="0">No</option></select></div>
            <div className="field"><label>Accept installation photographs uploaded from a browser</label>
              <select value={f.field?.allowWebCapture ? "1" : "0"} onChange={(e) => setF({ ...f, field: { ...f.field, allowWebCapture: e.target.value === "1" } })}><option value="0">No — live capture in the app only (FRD)</option><option value="1">Yes — also allow browser upload (not live-only)</option></select></div>
            <div className="frow">
              <div className="field"><label>Setup code valid (minutes)</label><input type="number" value={f.field?.enrolmentMinutes ?? 15} onChange={(e) => setF({ ...f, field: { ...f.field, enrolmentMinutes: +e.target.value } })} /></div>
              <div className="field"><label>Setup photo needs GPS within (m)</label><input type="number" value={f.field?.setupMaxAccuracyM ?? 60} onChange={(e) => setF({ ...f, field: { ...f.field, setupMaxAccuracyM: +e.target.value } })} /></div>
              <div className="field"><label>Offline queue kept (days)</label><input type="number" value={f.field?.offlineRetentionDays ?? 7} onChange={(e) => setF({ ...f, field: { ...f.field, offlineRetentionDays: +e.target.value } })} /></div>
            </div>
          </div>
        </Card>
        <Card title="Reason lists" sub="Offered at receipt, rejection and stock adjustment">
          <div className="form">
            <div className="field"><label>Consignment conditions (one per line)</label><textarea value={(f.receiptConditions ?? []).join("\n")} onChange={(e) => setF({ ...f, receiptConditions: lines(e.target.value) })} /></div>
            <div className="field"><label>Rejection reason codes</label><textarea value={(f.rejectReasons ?? []).join("\n")} onChange={(e) => setF({ ...f, rejectReasons: lines(e.target.value) })} /></div>
            <div className="field"><label>Adjustment reason codes</label><textarea value={(f.adjustmentReasons ?? []).join("\n")} onChange={(e) => setF({ ...f, adjustmentReasons: lines(e.target.value) })} /></div>
          </div>
        </Card>
      </div>

      <Card title="Article categories" sub="Drives the item code and the reporting hierarchy">
        <div className="form">
          {f.categories.map((c, i) => (
            <div className="frow" key={i}>
              <div className="field"><label>Code</label><input value={c.code} onChange={(e) => {
                const n = [...f.categories]; n[i] = { ...c, code: e.target.value.toUpperCase() }; setF({ ...f, categories: n });
              }} /></div>
              <div className="field"><label>Label</label><input value={c.label} onChange={(e) => {
                const n = [...f.categories]; n[i] = { ...c, label: e.target.value }; setF({ ...f, categories: n });
              }} /></div>
              <div className="field"><label>Sub-categories (comma separated)</label><input value={c.subs.join(", ")} onChange={(e) => {
                const n = [...f.categories]; n[i] = { ...c, subs: e.target.value.split(",").map((s) => s.trim().toUpperCase()).filter(Boolean) }; setF({ ...f, categories: n });
              }} /></div>
            </div>
          ))}
          <div className="actions">
            <button className="btn small" onClick={() => setF({ ...f, categories: [...f.categories, { code: "", label: "", subs: [] }] })}>Add category</button>
          </div>
        </div>
      </Card>

      <div className="actions">
        <button className="btn primary" disabled={busy} onClick={async () => {
          setBusy(true);
          try { await put("/api/config", f); await reload(); toast("Parameters saved"); }
          catch (e) { toast(errText(e), true); } finally { setBusy(false); }
        }}>{busy ? "Saving…" : "Save parameters"}</button>
        <button className="btn" onClick={() => setF(config)}>Discard changes</button>
        <span className="tiny">Version {f.version} — every change is written to the audit trail.</span>
      </div>

      <NotifyChannels />
    </>
  );
}

function RoleSelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const roles = useAsync(() => get<RoleDto[]>("/api/users/roles"), []);
  return <select value={value} onChange={(e) => onChange(e.target.value)}>{(roles.data ?? []).map((r) => <option key={r.key} value={r.key}>{r.label}</option>)}</select>;
}

/** FR-NTF-002: the department's own mail server and SMS gateway. Nothing paid is built in. */
function NotifyChannels() {
  const toast = useToast();
  const s = useAsync(() => get<NotifySettings>("/api/notify-settings"), []);
  const [f, setF] = useState<NotifySettings | null>(null);
  const [to, setTo] = useState("");
  useEffect(() => { if (s.data) setF(s.data); }, [s.data]);
  if (!f) return null;
  const T = (k: keyof NotifySettings) => ({ value: String(f[k] ?? ""), onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value }) });
  const test = async (channel: string) => {
    try { const r = await post<{ ok: boolean; message: string }>(`/api/notify-settings/test?channel=${channel}&to=${encodeURIComponent(to)}`); toast(r.message, !r.ok); } catch (e) { toast(errText(e), true); }
  };
  return (
    <Card title="Notification channels (FR-NTF-002)" sub="In-app notices always work. E-mail and SMS copies go out once the department's mail server and SMS gateway are entered here.">
      <div className="grid g-2">
        <div className="form">
          <div className="field"><label>E-mail</label><select value={f.emailEnabled ? "1" : "0"} onChange={(e) => setF({ ...f, emailEnabled: e.target.value === "1" })}><option value="0">Off</option><option value="1">On</option></select></div>
          <div className="frow"><div className="field"><label>SMTP server</label><input {...T("smtpHost")} placeholder="mail.punjab.gov.pk" /></div><div className="field" style={{ maxWidth: 110 }}><label>Port</label><input type="number" value={f.smtpPort} onChange={(e) => setF({ ...f, smtpPort: +e.target.value })} /></div></div>
          <div className="frow"><div className="field"><label>User</label><input {...T("smtpUser")} /></div><div className="field"><label>Password</label><input type="password" {...T("smtpPassword")} placeholder="unchanged" /></div></div>
          <div className="frow"><div className="field"><label>From address</label><input {...T("fromAddress")} placeholder="slims@pera.gop.pk" /></div><div className="field"><label>TLS</label><select value={f.smtpSsl ? "1" : "0"} onChange={(e) => setF({ ...f, smtpSsl: e.target.value === "1" })}><option value="1">Yes</option><option value="0">No</option></select></div></div>
          <div className="field"><label>Portal address used in e-mail links</label><input {...T("portalUrl")} placeholder="http://172.16.10.184:3001" /></div>
        </div>
        <div className="form">
          <div className="field"><label>SMS</label><select value={f.smsEnabled ? "1" : "0"} onChange={(e) => setF({ ...f, smsEnabled: e.target.value === "1" })}><option value="0">Off</option><option value="1">On</option></select></div>
          <div className="field"><label>Gateway URL — use {"{phone}"}, {"{text}"} and {"{key}"}</label><input {...T("smsUrl")} placeholder="https://sms.example.pk/send?to={phone}&msg={text}&key={key}" /></div>
          <div className="frow"><div className="field"><label>Method</label><select value={f.smsMethod} onChange={(e) => setF({ ...f, smsMethod: e.target.value })}><option>GET</option><option>POST</option></select></div><div className="field"><label>API key</label><input type="password" {...T("smsApiKey")} placeholder="unchanged" /></div></div>
          <div className="field"><label>Send a test to</label><input value={to} onChange={(e) => setTo(e.target.value)} placeholder="address@… or 03xx…" /></div>
          <div className="actions"><button className="btn small" disabled={!to} onClick={() => test("email")}>Test e-mail</button><button className="btn small" disabled={!to} onClick={() => test("sms")}>Test SMS</button></div>
        </div>
      </div>
      <div className="actions" style={{ marginTop: 10 }}>
        <button className="btn primary" onClick={async () => { try { setF(await put<NotifySettings>("/api/notify-settings", f)); toast("Channels saved"); } catch (e) { toast(errText(e), true); } }}>Save channels</button>
      </div>
    </Card>
  );
}
