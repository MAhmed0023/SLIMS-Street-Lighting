"use client";
import { Fragment, useState } from "react";
import { get, qs } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { AuditDto } from "@/lib/types";
import { dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Notice, Table, useAsync } from "@/components/ui";

const KINDS = ["", "task", "document", "workflow", "signature", "auth", "access", "config", "escalation", "report"];

export default function AuditPage() {
  const { can } = useAuth();
  const [kind, setKind] = useState(""); const [result, setResult] = useState(""); const [q, setQ] = useState(""); const [from, setFrom] = useState(""); const [to, setTo] = useState("");
  const [open, setOpen] = useState<number | null>(null);
  const list = useAsync(() => get<AuditDto[]>(`/api/audit${qs({ kind, result, q, from, to, limit: 1000 })}`), [kind, result, q, from, to]);
  return (
    <>
      {!can("audit") && <Notice kind="info"><span>You see your own actions. The full trail is available to auditors.</span></Notice>}
      <div className="filters">
        <select value={kind} onChange={(e) => setKind(e.target.value)} aria-label="Kind">{KINDS.map((k) => <option key={k} value={k}>{k || "Any kind"}</option>)}</select>
        <select value={result} onChange={(e) => setResult(e.target.value)} aria-label="Result"><option value="">Any result</option><option value="ok">ok</option><option value="denied">denied</option><option value="failed">failed</option></select>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="From" /><input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="To" />
        <input type="search" placeholder="Search actor, record, reason" value={q} onChange={(e) => setQ(e.target.value)} />
      </div>
      <Card flush title="Audit trail" sub="Append-only: the system only ever adds to this record">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No entries." cols={["When", "Actor", "Action", "Record", "Result", "Reason", "From"]}>
            {list.data!.map((a) => (
              <Fragment key={a.id}>
                <tr className="row-link" onClick={() => setOpen(open === a.id ? null : a.id)}>
                  <td className="tiny nowrap">{dateTime(a.at)}</td>
                  <td><div className="two-line"><b>{a.actorName ?? a.actorId}</b><small>{a.role}{a.actingForId ? ` · for ${a.actingForId}` : ""}</small></div></td>
                  <td><span className="tag">{a.kind}</span> {a.action}</td>
                  <td className="tiny">{a.entityType} {a.entityId}</td>
                  <td><Chip tone={a.result === "ok" ? "ok" : "crit"}>{a.result}</Chip></td>
                  <td className="tiny" style={{ maxWidth: 320 }}>{a.reason}</td>
                  <td className="tiny">{a.ip}</td>
                </tr>
                {open === a.id && <tr><td colSpan={7}><div className="grid g-2">
                  <div><div className="tiny">Before</div><pre className="hash" style={{ whiteSpace: "pre-wrap" }}>{a.prevJson ?? "—"}</pre></div>
                  <div><div className="tiny">After</div><pre className="hash" style={{ whiteSpace: "pre-wrap" }}>{a.nextJson ?? "—"}</pre></div>
                </div><div className="tiny">Correlation {a.correlationId}</div></td></tr>}
              </Fragment>
            ))}
          </Table>
        )}
      </Card>
    </>
  );
}
