"use client";
import { useState } from "react";
import { get, post, put, qs } from "@/lib/api";
import type { ReportDto, ReportMeta, ScheduleDto, SiteDto, StoreDto, UserPublicDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { date, dateTime, int, money } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

const DAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

const isNum = (v: unknown) => typeof v === "number";
const looksLikeDate = (v: unknown) => typeof v === "string" && /^\d{4}-\d{2}-\d{2}T/.test(v);

export default function ReportsPage() {
  const [tab, setTab] = useState<"run" | "schedules">("run");
  return (
    <>
      <div className="seg" style={{ marginBottom: 10 }}>
        <button className={tab === "run" ? "active" : ""} onClick={() => setTab("run")}>Run a report</button>
        <button className={tab === "schedules" ? "active" : ""} onClick={() => setTab("schedules")}>Scheduled delivery</button>
      </div>
      {tab === "run" ? <RunReport /> : <Schedules />}
    </>
  );
}

/** FR-RPT-010: reports delivered automatically to nominated recipients — in-app, and by e-mail where configured. */
function Schedules() {
  const { can, config } = useAuth();
  const toast = useToast();
  const list = useAsync(() => get<ScheduleDto[]>("/api/report-schedules"), []);
  const catalogue = useAsync(() => get<ReportMeta[]>("/api/reports"), []);
  const [edit, setEdit] = useState<ScheduleDto | "new" | null>(null);
  const title = (k: string) => catalogue.data?.find((c) => c.key === k)?.title ?? k;
  const when = (s: ScheduleDto) => `${s.frequency === "Daily" ? "Every day" : s.frequency === "Weekly" ? `Every ${DAYS[s.dayOfWeek]}` : `Monthly on day ${s.dayOfMonth}`} at ${String(s.hour).padStart(2, "0")}:00`;
  return (
    <>
      <div className="filters"><span className="spacer" />{(can("export") || can("configure")) && <button className="btn primary" onClick={() => setEdit("new")}>New schedule</button>}</div>
      <Notice kind="info">Each run is produced in the scope of the person who set it up, saved as CSV, announced in the recipients&apos; inbox, and e-mailed as an attachment when e-mail is configured under Parameters.</Notice>
      <Card flush title="Schedules">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No scheduled reports yet." cols={["Report", "When", "Period", "Recipients", "Next run", "Recent runs", ""]}>
            {list.data!.map((s) => (
              <tr key={s.id}>
                <td><div className="two-line"><b>{s.title}</b><small>{title(s.reportKey)}</small></div></td>
                <td className="tiny">{when(s)} PKT</td>
                <td className="tiny">{s.period === "previous" ? "the period just ended" : "period to date"}{s.zone ? ` · ${s.zone}` : ""}</td>
                <td className="tiny">{[...s.recipientUserIds, ...s.recipientEmails.split(",").filter(Boolean)].join(", ")}</td>
                <td className="tiny">{s.active ? dateTime(s.nextRunAt) : <Chip tone="muted">paused</Chip>}</td>
                <td className="tiny">{s.runs.slice(0, 3).map((r) => <div key={r.id}>{r.status === "Done" ? <a className="linky" href={r.downloadUrl}>{dateTime(r.ranAt)} · {r.rows} rows</a> : <span className="err">{dateTime(r.ranAt)} failed: {r.error}</span>}</div>)}</td>
                <td><div className="actions">
                  <button className="btn small" onClick={async () => { try { const r = await post<{ rows: number; status: string }>(`/api/report-schedules/${s.id}/run`); toast(`Run ${r.status} — ${r.rows} rows`); list.reload(); } catch (e) { toast(errText(e), true); } }}>Run now</button>
                  <button className="btn small" onClick={() => setEdit(s)}>Edit</button></div></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      {edit && <ScheduleModal s={edit === "new" ? null : edit} catalogue={catalogue.data ?? []} zones={config?.zones ?? []} onClose={() => setEdit(null)} onDone={() => { setEdit(null); list.reload(); }} />}
    </>
  );
}

function ScheduleModal({ s, catalogue, zones, onClose, onDone }: { s: ScheduleDto | null; catalogue: ReportMeta[]; zones: string[]; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const people = useAsync(() => get<UserPublicDto[]>("/api/auth/picker"), []);
  const [f, setF] = useState({ reportKey: s?.reportKey ?? "installations", title: s?.title ?? "", frequency: s?.frequency ?? "Weekly", dayOfWeek: s?.dayOfWeek ?? 1, dayOfMonth: s?.dayOfMonth ?? 1, hour: s?.hour ?? 8,
    period: s?.period ?? "previous", zone: s?.zone ?? "", storeId: s?.storeId ?? null, recipientUserIds: s?.recipientUserIds ?? [], recipientEmails: s?.recipientEmails ?? "", active: s?.active ?? true });
  const save = async () => {
    try { const body = { ...f, zone: f.zone || null }; if (s) await put(`/api/report-schedules/${s.id}`, body); else await post("/api/report-schedules", body); toast("Schedule saved"); onDone(); }
    catch (e) { toast(errText(e), true); }
  };
  return (
    <Modal open onClose={onClose} title={s ? "Edit schedule" : "New scheduled report"} footer={<><button className="btn primary" onClick={save}>Save</button><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="form">
        <div className="field"><label>Report</label><select value={f.reportKey} onChange={(e) => setF({ ...f, reportKey: e.target.value })}>{catalogue.map((c) => <option key={c.key} value={c.key}>{c.title}</option>)}</select></div>
        <div className="field"><label>Title</label><input value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} placeholder="Weekly installation report — PD Office" /></div>
        <div className="frow">
          <div className="field"><label>Frequency</label><select value={f.frequency} onChange={(e) => setF({ ...f, frequency: e.target.value })}><option>Daily</option><option>Weekly</option><option>Monthly</option></select></div>
          {f.frequency === "Weekly" && <div className="field"><label>Day</label><select value={f.dayOfWeek} onChange={(e) => setF({ ...f, dayOfWeek: +e.target.value })}>{DAYS.map((d, i) => <option key={d} value={i}>{d}</option>)}</select></div>}
          {f.frequency === "Monthly" && <div className="field"><label>Day of month</label><input type="number" min={1} max={28} value={f.dayOfMonth} onChange={(e) => setF({ ...f, dayOfMonth: +e.target.value })} /></div>}
          <div className="field"><label>Hour (PKT)</label><input type="number" min={0} max={23} value={f.hour} onChange={(e) => setF({ ...f, hour: +e.target.value })} /></div>
        </div>
        <div className="frow">
          <div className="field"><label>Covers</label><select value={f.period} onChange={(e) => setF({ ...f, period: e.target.value })}><option value="previous">The period just ended</option><option value="todate">Period to date</option></select></div>
          <div className="field"><label>Zone</label><select value={f.zone} onChange={(e) => setF({ ...f, zone: e.target.value })}><option value="">All in scope</option>{zones.map((z) => <option key={z}>{z}</option>)}</select></div>
        </div>
        <div className="field"><label>Recipients with an account</label>
          <select multiple size={6} value={f.recipientUserIds} onChange={(e) => setF({ ...f, recipientUserIds: Array.from(e.target.selectedOptions).map((o) => o.value) })}>
            {(people.data ?? []).map((u) => <option key={u.id} value={u.id}>{u.name} — {u.roleLabel}</option>)}
          </select><span className="hint">Ctrl-click to choose several</span></div>
        <div className="field"><label>Other e-mail addresses (comma separated)</label><input value={f.recipientEmails} onChange={(e) => setF({ ...f, recipientEmails: e.target.value })} placeholder="director@pera.gop.pk" /></div>
        <div className="field"><label>Status</label><select value={f.active ? "1" : "0"} onChange={(e) => setF({ ...f, active: e.target.value === "1" })}><option value="1">Active</option><option value="0">Paused</option></select></div>
      </div>
    </Modal>
  );
}

function RunReport() {
  const { config } = useAuth();
  const toast = useToast();
  const [key, setKey] = useState("installations");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [zone, setZone] = useState("");
  const [storeId, setStoreId] = useState("");
  const [siteId, setSiteId] = useState("");
  const catalogue = useAsync(() => get<ReportMeta[]>("/api/reports"), []);
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const sites = useAsync(() => get<SiteDto[]>("/api/sites"), []);
  const url = `/api/reports/${key}${qs({ from, to, zone, storeId, siteId })}`;
  const report = useAsync(() => get<ReportDto>(url), [url]);

  const csv = () => {
    const r = report.data; if (!r) return;
    const esc = (v: unknown) => v === null || v === undefined ? "" : looksLikeDate(v) ? dateTime(v as string) : `"${String(v).replace(/"/g, '""')}"`;
    const body = [r.columns.map((c) => `"${c}"`).join(","), ...r.rows.map((row) => row.map(esc).join(","))].join("\r\n");
    const blob = new Blob(["﻿" + body], { type: "text/csv;charset=utf-8" });
    const a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = `${r.key}-${new Date().toISOString().slice(0, 10)}.csv`;
    a.click(); URL.revokeObjectURL(a.href);
    toast("Exported");
  };

  const meta = catalogue.data?.find((c) => c.key === key);
  return (
    <>
      <div className="filters">
        <select value={key} onChange={(e) => setKey(e.target.value)} style={{ minWidth: 300 }}>
          {(catalogue.data ?? []).map((c) => <option key={c.key} value={c.key}>{c.title}</option>)}
        </select>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="From" />
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="To" />
        <select value={zone} onChange={(e) => setZone(e.target.value)}><option value="">All zones</option>{(config?.zones ?? []).map((z) => <option key={z}>{z}</option>)}</select>
        <select value={storeId} onChange={(e) => setStoreId(e.target.value)}><option value="">All stores</option>{(stores.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}</select>
        <select value={siteId} onChange={(e) => setSiteId(e.target.value)}><option value="">All sites</option>{(sites.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}</select>
        <span className="spacer" />
        <button className="btn" onClick={report.reload}>Run</button>
        <button className="btn secondary" onClick={csv} disabled={!report.data?.rows.length}>Export CSV</button>
        <button className="btn" onClick={() => window.print()}>Print</button>
      </div>

      {meta && <Notice kind="info"><span><b>{meta.title}</b> — primary audience: {meta.audience}. Built from the transaction record, with no manual compilation (FR-RP-01).</span></Notice>}

      <Card flush title={report.data?.title ?? "Report"} sub={report.data ? `${int(report.data.rows.length)} row(s)${Object.keys(report.data.filters).length ? ` · ${Object.entries(report.data.filters).map(([k, v]) => `${k}: ${v}`).join(" · ")}` : ""}` : undefined}>
        {report.loading ? <Loading what="report" /> : report.error ? <ErrorBox error={report.error} /> : (
          <Table rows={report.data!.rows.length} empty="No rows match these filters."
            cols={report.data!.columns.map((c) => ({ t: c, c: /qty|value|rate|%|days|m\)|count|labels|ordered|received|issued|installed|balance|failed|shortfall|approved|requested/i.test(c) ? "r" : undefined }))}>
            {report.data!.rows.map((row, i) => (
              <tr key={i}>
                {row.map((v, j) => (
                  <td key={j} className={isNum(v) ? "r num" : ""}>
                    {v === null || v === undefined || v === "" ? <span className="muted">—</span>
                      : looksLikeDate(v) ? <span className="tiny">{/T00:00:00/.test(String(v)) ? date(String(v)) : dateTime(String(v))}</span>
                      : typeof v === "boolean" ? (v ? "Yes" : "No")
                      : isNum(v) ? money(v as number, Number.isInteger(v) ? 0 : 2)
                      : String(v)}
                  </td>
                ))}
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </>
  );
}
