"use client";
import { useState } from "react";
import { downloadFile, get, qs } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { DocumentTypeDto, OrgUnitDto, ReportInfo, ReportResult, UserRefDto } from "@/lib/types";
import { date, dateTime } from "@/lib/format";
import { Card, ErrorBox, Loading, Table, UserSelect, errText, useAsync, useToast } from "@/components/ui";

const KINDS = ["task", "document", "workflow", "signature", "auth", "access", "config", "escalation", "report"];

export default function ReportsPage() {
  const toast = useToast();
  const { can } = useAuth();
  const catalog = useAsync(() => get<ReportInfo[]>("/api/reports"), []);
  const people = useAsync(() => get<UserRefDto[]>("/api/org/directory?inactive=true"), []);
  const units = useAsync(() => get<OrgUnitDto[]>("/api/org/units"), []);
  const types = useAsync(() => get<DocumentTypeDto[]>("/api/workflow/types"), []);
  const [key, setKey] = useState("task-aging");
  const [f, setF] = useState({ from: "", to: "", userId: null as string | null, unitId: "", status: "", typeId: "", kind: "" });
  const [res, setRes] = useState<ReportResult | null>(null); const [err, setErr] = useState<unknown>(null); const [busy, setBusy] = useState(false);
  const info = catalog.data?.find((c) => c.key === key);
  const params = () => qs({ from: f.from, to: f.to, userId: f.userId, unitId: f.unitId, status: f.status, typeId: f.typeId, kind: f.kind });
  const run = async () => { setBusy(true); setErr(null); try { setRes(await get<ReportResult>(`/api/reports/${key}${params()}`)); } catch (e) { setErr(e); setRes(null); } finally { setBusy(false); } };
  const exp = async (format: string) => { try { await downloadFile(`/api/reports/${key}/export${params()}${params() ? "&" : "?"}format=${format}`, `PERA-${key}.${format}`); } catch (e) { toast(errText(e), true); } };
  const has = (x: string) => info?.filters.includes(x);
  const cell = (v: unknown, kind: string) => v == null ? "" : kind === "date" ? date(String(v)) : kind === "datetime" ? dateTime(String(v)) : String(v);
  return (
    <div className="grid">
      <Card title="Report" sub={info?.description} className="no-print">
        <div className="form">
          <div className="frow">
            <div className="field"><label>Report</label><select value={key} onChange={(e) => { setKey(e.target.value); setRes(null); }}>
              {(catalog.data ?? []).filter((c) => c.key !== "audit" || can("audit")).map((c) => <option key={c.key} value={c.key}>{c.title}</option>)}</select></div>
            {has("from") && <div className="field"><label>From</label><input type="date" value={f.from} onChange={(e) => setF({ ...f, from: e.target.value })} /></div>}
            {has("to") && <div className="field"><label>To</label><input type="date" value={f.to} onChange={(e) => setF({ ...f, to: e.target.value })} /></div>}
            {has("user") && <div className="field"><label>Officer</label><UserSelect users={people.data ?? []} value={f.userId} onChange={(v) => setF({ ...f, userId: v })} placeholder="Any officer" /></div>}
            {has("unit") && <div className="field"><label>Unit</label><select value={f.unitId} onChange={(e) => setF({ ...f, unitId: e.target.value })}><option value="">Any unit</option>{(units.data ?? []).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}</select></div>}
            {has("status") && <div className="field"><label>Status</label><select value={f.status} onChange={(e) => setF({ ...f, status: e.target.value })}><option value="">Any open status</option>{["Assigned", "InProgress", "Blocked", "SubmittedForReview", "Returned"].map((s) => <option key={s}>{s}</option>)}</select></div>}
            {has("type") && <div className="field"><label>Document type</label><select value={f.typeId} onChange={(e) => setF({ ...f, typeId: e.target.value })}><option value="">Any type</option>{(types.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></div>}
            {has("kind") && <div className="field"><label>Kind</label><select value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value })}><option value="">Any</option>{KINDS.map((k) => <option key={k}>{k}</option>)}</select></div>}
          </div>
          <div className="actions">
            <button className="btn primary" disabled={busy} onClick={run}>Run report</button>
            {can("export") && <><button className="btn" onClick={() => exp("csv")}>Export CSV</button><button className="btn" onClick={() => exp("xlsx")}>Export Excel</button></>}
            {res && <button className="btn" onClick={() => window.print()}>Print</button>}
          </div>
        </div>
      </Card>
      {busy && <Loading what="report" />}
      {!!err && <ErrorBox error={err} />}
      {res && <Card flush title={`${res.title} — ${res.rows.length} row(s)`} sub={`Generated ${dateTime(res.generatedAt)} by ${res.generatedBy} · Filters: ${Object.entries(res.filters).map(([k, v]) => `${k}: ${v}`).join("; ")}`}>
        <div className="print-head"><h2>PERA — {res.title}</h2><div>Generated {dateTime(res.generatedAt)} by {res.generatedBy}</div><div>Filters: {Object.entries(res.filters).map(([k, v]) => `${k}: ${v}`).join("; ")}</div></div>
        <Table rows={res.rows.length} empty="No rows for these filters." cols={res.columns.map((c) => ({ t: c.label, c: c.kind === "number" ? "r" : undefined }))}>
          {res.rows.map((r, i) => <tr key={i}>{res.columns.map((c) => <td key={c.key} className={c.kind === "number" ? "r num" : c.key === "hash" ? "hash" : undefined}>{cell(r[c.key], c.kind)}</td>)}</tr>)}
        </Table>
      </Card>}
    </div>
  );
}
