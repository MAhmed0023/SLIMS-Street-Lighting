"use client";
import { useState } from "react";
import { get, qs } from "@/lib/api";
import type { AuditDto } from "@/lib/types";
import { dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Notice, Table, useAsync } from "@/components/ui";

const KINDS = ["", "stock", "master", "auth", "access", "config"];
const tone = (a: string) => /rejected|failed|exception|reversed|condemned|disposed|out-of-order/.test(a) ? "crit"
  : /approved|confirmed|installed|received|posted|created/.test(a) ? "ok"
  : /pending|indent|recapture|released/.test(a) ? "watch" : "";

export default function AuditPage() {
  const [kind, setKind] = useState("");
  const [q, setQ] = useState("");
  const [limit, setLimit] = useState(300);
  const list = useAsync(() => get<AuditDto[]>(`/api/audit${qs({ kind, q, limit })}`), [kind, q, limit]);

  return (
    <>
      <div className="filters">
        <div className="seg">{KINDS.map((k) => <button key={k || "all"} className={kind === k ? "active" : ""} onClick={() => setKind(k)}>{k || "All"}</button>)}</div>
        <input type="search" placeholder="Search actor, reason, entity or detail…" value={q} onChange={(e) => setQ(e.target.value)} />
        <select value={limit} onChange={(e) => setLimit(+e.target.value)}>{[100, 300, 1000, 2000].map((n) => <option key={n} value={n}>{n} rows</option>)}</select>
        <span className="spacer" />
        <button className="btn small" onClick={list.reload}>Refresh</button>
      </div>

      <Notice kind="info"><span>
        The audit trail is append-only. No posted transaction can be deleted — corrections are made by reversal and both entries stay visible (BR-022).
        Every row carries the named user, their role, the time and the address the request came from (BR-023).
      </span></Notice>

      <Card flush title="Audit trail" sub="Newest first">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="Nothing logged in this view." cols={["When", "Kind", "Action", "Entity", "By", "Reason / detail", "From"]}>
            {list.data!.map((a) => (
              <tr key={a.id}>
                <td className="tiny nowrap">{dateTime(a.at)}</td>
                <td><Chip tone="plain">{a.kind}</Chip></td>
                <td><Chip tone={tone(a.action)}>{a.action}</Chip></td>
                <td className="tiny">{a.entityType ? <><b>{a.entityType}</b><div className="mono tiny">{a.entityId}</div></> : "—"}</td>
                <td className="tiny">{a.actorName ?? a.actorId ?? "—"}{a.role && <div className="tiny">{a.role}</div>}</td>
                <td className="tiny" style={{ maxWidth: 440 }}>
                  {a.reason && <div>{a.reason}</div>}
                  {a.nextJson && <span className="mono tiny">{a.nextJson}</span>}
                  {a.prevJson && <div className="mono tiny muted">was {a.prevJson}</div>}
                </td>
                <td className="mono tiny">{a.ip ?? "—"}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </>
  );
}
