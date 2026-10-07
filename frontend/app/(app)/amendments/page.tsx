"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import type { AmendmentDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

/** FR-EXC-011: corrections to completed records, each with reason, approval by another user and the before/after values. */
export default function AmendmentsPage() {
  const { user } = useAuth();
  const toast = useToast();
  const [status, setStatus] = useState("Pending");
  const list = useAsync(() => get<AmendmentDto[]>(`/api/amendments${status ? `?status=${status}` : ""}`), [status]);
  const [note, setNote] = useState<Record<number, string>>({});
  const decide = async (a: AmendmentDto, approve: boolean) => {
    try { await post(`/api/amendments/${a.id}/decide`, { approve, note: note[a.id] || null }); toast(approve ? "Amendment approved and applied" : "Amendment rejected"); list.reload(); }
    catch (e) { toast(errText(e), true); }
  };
  return (
    <>
      <div className="filters"><div className="seg">{["Pending", "Approved", "Rejected", ""].map((s) => <button key={s} className={status === s ? "active" : ""} onClick={() => setStatus(s)}>{s || "All"}</button>)}</div></div>
      <Notice kind="info">Amendments are requested from the record itself — an installation, GRN, issue or return — with <b>Request amendment</b>. Photographs, coordinates and stock quantities are never amended; quantities go through stock adjustments.</Notice>
      <Card flush title="Amendment requests">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="Nothing in this view." cols={["Request", "Record", "Field", "Before → after", "Reason", "Status", ""]}>
            {list.data!.map((a) => (
              <tr key={a.id}>
                <td className="mono">{a.number}<div className="tiny">{a.raisedBy} · {dateTime(a.raisedAt)}</div></td>
                <td><b>{a.entityType}</b><div className="mono tiny">{a.entityRef}</div></td>
                <td>{a.field}</td>
                <td>“{a.oldValue ?? "—"}” → <b>“{a.newValue ?? "—"}”</b></td>
                <td style={{ maxWidth: 260 }}>{a.reason}</td>
                <td><Chip tone={a.status === "Approved" ? "ok" : a.status === "Rejected" ? "muted" : "watch"}>{a.status}</Chip>{a.decidedBy && <div className="tiny">{a.decidedBy} · {dateTime(a.decidedAt)}{a.decisionNote ? ` — ${a.decisionNote}` : ""}</div>}</td>
                <td>{a.status === "Pending" && (a.raisedById === user?.id ? <span className="tiny">awaiting another approver</span> : <div className="actions">
                  <input value={note[a.id] ?? ""} onChange={(e) => setNote({ ...note, [a.id]: e.target.value })} placeholder="Note" style={{ width: 120 }} />
                  <button className="btn small primary" onClick={() => decide(a, true)}>Approve</button>
                  <button className="btn small danger" disabled={!note[a.id]?.trim()} onClick={() => decide(a, false)}>Reject</button></div>)}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </>
  );
}
