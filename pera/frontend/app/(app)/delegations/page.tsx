"use client";
import { useState } from "react";
import { get, post } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { DelegationDto, DocumentTypeDto, UserRefDto } from "@/lib/types";
import { addDays, date, dateTime, today } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Prompt, Table, UserSelect, errText, useAsync, useToast } from "@/components/ui";

const SCOPES = [{ k: "All", l: "Everything (tasks, documents, approvals, signatures)" }, { k: "Tasks", l: "Tasks only" }, { k: "Documents", l: "Document handling (review, information, dispatch)" }, { k: "Approvals", l: "Approvals and concurrence (with document handling)" }, { k: "Signatures", l: "Signatures (with document handling)" }];

export default function DelegationsPage() {
  const toast = useToast();
  const { can } = useAuth();
  const list = useAsync(() => get<DelegationDto[]>("/api/org/delegations"), []);
  const people = useAsync(() => get<UserRefDto[]>("/api/org/directory"), []);
  const types = useAsync(() => get<DocumentTypeDto[]>("/api/workflow/types"), []);
  const [f, setF] = useState<null | { fromUserId: string | null; toUserId: string | null; scope: string; documentTypeId: number | null; startsOn: string; endsOn: string; reason: string }>(null);
  const [revoke, setRevoke] = useState<DelegationDto | null>(null);
  const save = async () => { try { await post("/api/org/delegations", f); toast("Acting arrangement recorded; both officers are notified"); setF(null); list.reload(); } catch (e) { toast(errText(e), true); } };
  return (
    <>
      <Notice kind="info"><span>An acting officer can do what the absent officer could, within the scope and dates set here. Every action they take records both names (FR-027). Approval power still comes from the absent officer&apos;s designation in the authority matrix.</span></Notice>
      <div className="filters"><span className="spacer" />{can("configure") && <button className="btn primary" onClick={() => setF({ fromUserId: null, toUserId: null, scope: "All", documentTypeId: null, startsOn: today(), endsOn: addDays(7), reason: "" })}>New acting arrangement</button>}</div>
      <Card flush title="Acting arrangements" sub={can("configure") ? "All arrangements" : "Arrangements you are party to"}>
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No acting arrangements." cols={["Absent officer", "Acting officer", "Scope", "Period", "Basis", "State", ""]}>
            {list.data!.map((d) => (
              <tr key={d.id}>
                <td>{d.fromName}</td><td>{d.toName}</td><td>{d.scope}{d.documentType ? <div className="tiny">{d.documentType} only</div> : null}</td>
                <td className="tiny">{date(d.startsOn)} → {date(d.endsOn)}</td><td className="tiny" style={{ maxWidth: 280 }}>{d.reason}<div>recorded {dateTime(d.createdAt)}</div></td>
                <td><Chip tone={d.state === "Active" ? "ok" : d.state === "Scheduled" ? "info" : "muted"}>{d.state}</Chip></td>
                <td>{can("configure") && ["Active", "Scheduled"].includes(d.state) && <button className="btn small" onClick={() => setRevoke(d)}>Revoke</button>}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      {f && <Modal open onClose={() => setF(null)} title="New acting arrangement" footer={<><button className="btn primary" disabled={!f.fromUserId || !f.toUserId || !f.reason.trim()} onClick={save}>Save</button><span className="spacer" /><button className="btn" onClick={() => setF(null)}>Cancel</button></>}>
        <div className="form"><div className="frow">
          <div className="field"><label>Absent officer</label><UserSelect users={people.data ?? []} value={f.fromUserId} onChange={(v) => setF({ ...f, fromUserId: v })} /></div>
          <div className="field"><label>Acting officer</label><UserSelect users={(people.data ?? []).filter((u) => u.id !== f.fromUserId)} value={f.toUserId} onChange={(v) => setF({ ...f, toUserId: v })} /></div>
        </div><div className="frow">
          <div className="field"><label>Scope</label><select value={f.scope} onChange={(e) => setF({ ...f, scope: e.target.value })}>{SCOPES.map((s) => <option key={s.k} value={s.k}>{s.l}</option>)}</select></div>
          <div className="field"><label>Document type</label><select value={f.documentTypeId ?? ""} onChange={(e) => setF({ ...f, documentTypeId: e.target.value ? +e.target.value : null })}><option value="">All types</option>{(types.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></div>
          <div className="field"><label>From</label><input type="date" value={f.startsOn} onChange={(e) => setF({ ...f, startsOn: e.target.value })} /></div>
          <div className="field"><label>To</label><input type="date" value={f.endsOn} onChange={(e) => setF({ ...f, endsOn: e.target.value })} /></div>
        </div>
          <div className="field"><label>Basis (leave, tour, office order number)</label><textarea rows={2} value={f.reason} onChange={(e) => setF({ ...f, reason: e.target.value })} /></div>
        </div>
      </Modal>}
      <Prompt open={!!revoke} title="Revoke acting arrangement" label="Reason" confirm="Revoke" tone="danger" onCancel={() => setRevoke(null)}
        onConfirm={async (r) => { try { await post(`/api/org/delegations/${revoke!.id}/revoke`, { reason: r || null }); toast("Revoked"); setRevoke(null); list.reload(); } catch (e) { toast(errText(e), true); } }} />
    </>
  );
}
