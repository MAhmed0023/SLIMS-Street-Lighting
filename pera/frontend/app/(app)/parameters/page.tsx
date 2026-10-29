"use client";
import { useState } from "react";
import { put } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { AppConfig } from "@/lib/types";
import { Card, Loading, Notice, errText, useToast } from "@/components/ui";

export default function ParametersPage() {
  const { config } = useAuth();
  if (!config) return <Loading />;
  return <ParametersForm key={config.version} initial={config} />;
}

function ParametersForm({ initial }: { initial: AppConfig }) {
  const { reload } = useAuth();
  const toast = useToast();
  const [c, setC] = useState<AppConfig>(() => structuredClone(initial));
  const n = (v: string) => Math.max(0, Math.round(+v || 0));
  const save = async () => { try { await put("/api/config", c); toast("Parameters saved"); await reload(); } catch (e) { toast(errText(e), true); } };
  return (
    <div className="grid">
      <Card title="Reminders and escalation" sub="Applied by the background worker to open tasks and document stages (FR-024)">
        <div className="frow">
          <div className="field"><label>Remind the holder before the due time (hours)</label><input type="number" value={c.alerts.reminderHours} onChange={(e) => setC({ ...c, alerts: { ...c.alerts, reminderHours: n(e.target.value) } })} /></div>
          <div className="field"><label>Escalate to the reporting officer after (hours overdue)</label><input type="number" value={c.alerts.escalate1Hours} onChange={(e) => setC({ ...c, alerts: { ...c.alerts, escalate1Hours: n(e.target.value) } })} /></div>
          <div className="field"><label>Escalate one more level after (hours overdue)</label><input type="number" value={c.alerts.escalate2Hours} onChange={(e) => setC({ ...c, alerts: { ...c.alerts, escalate2Hours: n(e.target.value) } })} /></div>
          <div className="field"><label>Check every (minutes)</label><input type="number" value={c.alerts.workerMinutes} onChange={(e) => setC({ ...c, alerts: { ...c.alerts, workerMinutes: Math.max(1, n(e.target.value)) } })} /></div>
          <div className="field"><label>“Due soon” window (days)</label><input type="number" value={c.alerts.dueSoonDays} onChange={(e) => setC({ ...c, alerts: { ...c.alerts, dueSoonDays: n(e.target.value) } })} /></div>
        </div>
      </Card>
      <Card title="Files" sub="Document files follow their document type; these apply to task attachments and to every upload">
        <div className="frow">
          <div className="field"><label>Never accepted (extensions)</label><input value={c.files.blockedExtensions} onChange={(e) => setC({ ...c, files: { ...c.files, blockedExtensions: e.target.value } })} /></div>
          <div className="field"><label>Task attachment types</label><input value={c.files.taskAttachmentExtensions} onChange={(e) => setC({ ...c, files: { ...c.files, taskAttachmentExtensions: e.target.value } })} /></div>
          <div className="field"><label>Task attachment limit (MB)</label><input type="number" value={c.files.taskAttachmentMaxMb} onChange={(e) => setC({ ...c, files: { ...c.files, taskAttachmentMaxMb: Math.max(1, n(e.target.value)) } })} /></div>
        </div>
      </Card>
      <Card title="Sign-in and naming">
        <div className="frow">
          <div className="field"><label>Application name</label><input value={c.app.name} onChange={(e) => setC({ ...c, app: { ...c.app, name: e.target.value } })} /></div>
          <div className="field"><label>Short name</label><input value={c.app.short} onChange={(e) => setC({ ...c, app: { ...c.app, short: e.target.value } })} /></div>
          <div className="field"><label>Organisation</label><input value={c.app.client} onChange={(e) => setC({ ...c, app: { ...c.app, client: e.target.value } })} /></div>
          <div className="field"><label>Office</label><input value={c.app.agency} onChange={(e) => setC({ ...c, app: { ...c.app, agency: e.target.value } })} /></div>
        </div>
        <div className="actions" style={{ marginTop: 10 }}>
          <label className="tiny"><input type="checkbox" style={{ width: "auto" }} checked={c.showUserPicker} onChange={(e) => setC({ ...c, showUserPicker: e.target.checked })} /> Show “Choose a user” on the sign-in card</label>
          <label className="tiny"><input type="checkbox" style={{ width: "auto" }} checked={c.loginRequired} onChange={(e) => setC({ ...c, loginRequired: e.target.checked })} /> Passwords required</label>
        </div>
        {!c.loginRequired && <Notice kind="crit"><span>Without passwords anyone can sign in as any account that has none. Use only on an isolated demonstration machine.</span></Notice>}
      </Card>
      <div className="actions"><button className="btn primary" onClick={save}>Save parameters</button><span className="tiny">Version {c.version}. Changes are audit-logged.</span></div>
    </div>
  );
}
