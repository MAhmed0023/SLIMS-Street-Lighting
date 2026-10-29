"use client";
import { useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { ApiError, downloadFile, fetchBlob, get, post, put, qs, upload } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { AmendmentOp, DocumentDetailDto, DocumentDto, DocumentTypeDto, OrgUnitDto, RouteIssue, RouteStageDto, SignatureFieldDto, StageActionsDto, TemplateDto, UserRefDto, VerificationDto } from "@/lib/types";
import { ACTION_LABEL, STAGE_TYPES, ago, bytes, dateTime, docLabel, docTone, hours, stageTone } from "@/lib/format";
import { RouteTracker, VerifyTable } from "@/components/DocParts";
import { Card, Chip, Empty, ErrorBox, Issues, Loading, Modal, Notice, Prompt, Table, Tabs, UserSelect, errIssues, errText, useAsync, useQueryId, useToast } from "@/components/ui";

const BOXES = [{ k: "inbox", l: "Awaiting me" }, { k: "outbox", l: "Initiated by me" }, { k: "returned", l: "Returned to me" }, { k: "approval", l: "Awaiting approval" }, { k: "signature", l: "Awaiting signature" }, { k: "all", l: "All I can see" }];
const STATUSES = ["", "Draft", "InRoute", "Returned", "Completed", "Rejected", "Withdrawn"];
const CLASSES = ["Public", "Internal", "Confidential", "Restricted"];

export default function DocumentsPage() {
  const { can } = useAuth();
  // dashboard links arrive pre-filtered (?box=&status=&holder=); a task opens registration with ?new=1&task=
  const params = useSearchParams();
  const [init] = useState(() => new URLSearchParams(params.toString()));
  const [box, setBox] = useState(init.get("box") ?? (init.get("holder") || init.get("status") ? "all" : "inbox"));
  const [status, setStatus] = useState(init.get("status") ?? "");
  const [typeId, setTypeId] = useState("");
  const [holder, setHolder] = useState(init.get("holder") ?? "");
  const [q, setQ] = useState("");
  const [id, setId] = useQueryId();
  const [creating, setCreating] = useState<{ taskId?: number } | null>(init.get("new") ? { taskId: init.get("task") ? +init.get("task")! : undefined } : null);
  const types = useAsync(() => get<DocumentTypeDto[]>("/api/workflow/types"), []);
  const list = useAsync(() => get<DocumentDto[]>(`/api/documents${qs({ box, status, typeId, holderId: holder, q })}`), [box, status, typeId, holder, q]);

  return (
    <>
      <div className="filters">
        <div className="seg">{BOXES.map((b) => <button key={b.k} className={box === b.k ? "active" : ""} onClick={() => setBox(b.k)}>{b.l}</button>)}</div>
        <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status">{STATUSES.map((s) => <option key={s} value={s}>{s ? docLabel(s) : "Any status"}</option>)}</select>
        <select value={typeId} onChange={(e) => setTypeId(e.target.value)} aria-label="Type"><option value="">Any type</option>{(types.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select>
        <input type="search" placeholder="Search number, subject, reference" value={q} onChange={(e) => setQ(e.target.value)} />
        {holder && <button className="btn small" onClick={() => setHolder("")}>Holder: {holder} ×</button>}
        <span className="spacer" />
        {can("document") && <button className="btn primary" onClick={() => setCreating({})}>Register document</button>}
      </div>

      <Card flush title="Documents" sub="Current holder, action requested, time held and the full movement history for every case">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No documents in this view." cols={["Document", "Type", "Initiator", "With / stage", "Held", "Approval · signature", "Status"]}>
            {list.data!.map((d) => (
              <tr key={d.id} className="row-link" onClick={() => setId(d.id)}>
                <td><div className="two-line"><b>{d.subject}</b><small><span className="mono">{d.number}</span>{d.taskNumber ? ` · task ${d.taskNumber}` : ""}{d.activeVersionNo ? ` · v${d.activeVersionNo}` : ""}</small></div>
                  {d.classification !== "Internal" && <Chip tone={d.classification === "Restricted" ? "crit" : d.classification === "Confidential" ? "watch" : "muted"}>{d.classification}</Chip>}</td>
                <td className="tiny">{d.documentType}</td>
                <td className="tiny">{d.initiatorName}</td>
                <td>{d.currentHolders ? <div className="two-line"><b>{d.currentHolders}</b><small>{d.currentStage}</small></div> : "—"}{d.awaitingMe && <div><Chip tone="info">Awaiting you</Chip></div>}</td>
                <td className="nowrap tiny">{d.heldSince ? ago(d.heldSince).replace(" ago", "") : "—"}{d.overdue && <div><Chip tone="crit">Overdue</Chip></div>}</td>
                <td className="tiny">{d.approvalState} · {d.signatureState}</td>
                <td><Chip tone={docTone(d.status)}>{docLabel(d.status)}</Chip>{d.returnCount > 0 && <div className="tiny">returned ×{d.returnCount}</div>}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>

      {id && <DocumentModal key={id} id={id} onClose={() => setId(null)} onChanged={list.reload} />}
      {creating && <RegisterForm taskId={creating.taskId} types={types.data ?? []} onClose={() => setCreating(null)} onSaved={(d) => { setCreating(null); list.reload(); setId(d.document.id); }} />}
    </>
  );
}

// ---- registration and metadata -------------------------------------------------------------------------
function RegisterForm({ taskId, types, existing, onClose, onSaved }: { taskId?: number; types: DocumentTypeDto[]; existing?: DocumentDetailDto; onClose: () => void; onSaved: (d: DocumentDetailDto) => void }) {
  const toast = useToast();
  const units = useAsync(() => get<OrgUnitDto[]>("/api/org/units"), []);
  const templates = useAsync(() => get<TemplateDto[]>("/api/workflow/templates"), []);
  const e = existing;
  const [f, setF] = useState({
    subject: e?.document.subject ?? "", documentTypeId: e?.document.documentTypeId ?? (types.find((t) => t.active)?.id ?? 0), originOrgUnitId: e?.originOrgUnitId ?? null as number | null,
    classification: e?.document.classification ?? "Internal", taskId: e?.document.taskId ?? taskId ?? null as number | null, externalRef: e?.document.externalRef ?? "", notes: e?.notes ?? "", templateId: null as number | null,
  });
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof f, v: unknown) => setF({ ...f, [k]: v });
  // the types may arrive after the form opens: fall back to the first active one
  const typeId = f.documentTypeId || (types.find((t) => t.active)?.id ?? 0);
  const type = types.find((t) => t.id === typeId);
  const forType = (templates.data ?? []).filter((t) => t.documentTypeId === typeId && t.status === "Active");
  const save = async () => {
    setBusy(true);
    try {
      const body = { ...f, documentTypeId: typeId, externalRef: f.externalRef || null, notes: f.notes || null };
      onSaved(e ? await put<DocumentDetailDto>(`/api/documents/${e.document.id}`, body) : await post<DocumentDetailDto>("/api/documents", body));
      toast(e ? "Saved" : "Registered — upload the working file and check the route");
    } catch (err) { toast(errText(err), true); } finally { setBusy(false); }
  };
  return (
    <Modal open onClose={onClose} title={e ? `Edit ${e.document.number}` : "Register a document"} sub="A unique case number is issued on registration"
      footer={<><button className="btn primary" disabled={busy || !f.subject.trim() || !typeId} onClick={save}>{e ? "Save" : "Register"}</button><span className="spacer" /><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="form">
        <div className="field"><label>Subject</label><input value={f.subject} onChange={(x) => set("subject", x.target.value)} placeholder="Approval of the Q3 enforcement drive plan" /></div>
        <div className="frow">
          <div className="field"><label>Document type</label>
            <select value={typeId} disabled={!!e && e.document.status !== "Draft"} onChange={(x) => { const t = types.find((y) => y.id === +x.target.value); setF({ ...f, documentTypeId: +x.target.value, classification: t?.defaultClassification ?? f.classification, templateId: null }); }}>
              {types.filter((t) => t.active).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select></div>
          {!e && <div className="field"><label>Route template</label>
            <select value={f.templateId ?? ""} onChange={(x) => set("templateId", x.target.value ? +x.target.value : null)}>
              <option value="">Default for this type</option>{forType.map((t) => <option key={t.id} value={t.id}>{t.name} (v{t.version})</option>)}
            </select></div>}
          <div className="field"><label>Classification</label><select value={f.classification} onChange={(x) => set("classification", x.target.value)}>{CLASSES.map((c) => <option key={c}>{c}</option>)}</select>
            <span className="hint">Restricted: participants only.</span></div>
          <div className="field"><label>Originating office</label>
            <select value={f.originOrgUnitId ?? ""} onChange={(x) => set("originOrgUnitId", x.target.value ? +x.target.value : null)}>
              <option value="">My unit</option>{(units.data ?? []).filter((u) => u.active).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
            </select></div>
        </div>
        <div className="frow">
          <div className="field"><label>Related task id</label><input type="number" value={f.taskId ?? ""} onChange={(x) => set("taskId", x.target.value ? +x.target.value : null)} placeholder="optional" /></div>
          <div className="field"><label>Reference / diary number</label><input value={f.externalRef} onChange={(x) => set("externalRef", x.target.value)} /></div>
        </div>
        <div className="field"><label>Notes / metadata</label><textarea rows={3} value={f.notes} onChange={(x) => set("notes", x.target.value)} /></div>
        {type && <Notice kind="info"><span>{type.name}: accepts {type.allowedExtensions} up to {type.maxSizeMb} MB{type.enforceAuthority ? "; approval and signature need authority in the delegation of powers" : ""}{type.requireApprovalStage ? "; a competent authority must approve" : ""}.</span></Notice>}
      </div>
    </Modal>
  );
}

// ---- detail -----------------------------------------------------------------------------------------------
type DTab = "overview" | "file" | "route" | "signatures" | "movement" | "amendments";

function DocumentModal({ id, onClose, onChanged }: { id: number; onClose: () => void; onChanged: () => void }) {
  const toast = useToast();
  const router = useRouter();
  const d = useAsync(() => get<DocumentDetailDto>(`/api/documents/${id}`), [id]);
  const types = useAsync(() => get<DocumentTypeDto[]>("/api/workflow/types"), []);
  const [tab, setTab] = useState<DTab>("overview");
  const [editing, setEditing] = useState(false);
  const [warnings, setWarnings] = useState<RouteIssue[] | null>(null);
  const [withdraw, setWithdraw] = useState(false);
  const [inform, setInform] = useState(false);
  const [verify, setVerify] = useState<VerificationDto | null>(null);
  const [busy, setBusy] = useState(false);
  // bumped on every change so the editable panels start again from the saved state
  const [rev, setRev] = useState(0);

  if (d.loading && !d.data) return <Modal open onClose={onClose} title="Document"><Loading /></Modal>;
  if (d.error) return <Modal open onClose={onClose} title="Document"><ErrorBox error={d.error} /></Modal>;
  const x = d.data!; const doc = x.document;
  const changed = (n: DocumentDetailDto, msg: string) => { d.setData(n); setRev((r) => r + 1); onChanged(); toast(msg); };
  const submit = async (accept: boolean) => {
    setBusy(true);
    try { changed(await post<DocumentDetailDto>(`/api/documents/${id}/submit`, { acceptWarnings: accept }), "Submitted — the first stage has been notified"); setWarnings(null); }
    catch (e) {
      if (e instanceof ApiError && e.step === "route-warnings") setWarnings(errIssues(e));
      else { toast(errText(e), true); if (errIssues(e).length) setTab("route"); }
    } finally { setBusy(false); }
  };
  const runVerify = async () => { try { setVerify(await get<VerificationDto>(`/api/documents/${id}/verify`)); } catch (e) { toast(errText(e), true); } };
  const mine = x.can.stages;

  return (
    <>
      <Modal open onClose={onClose} size="wide" title={doc.subject} sub={<><span className="mono">{doc.number}</span> · {doc.documentType}{x.templateName ? ` · ${x.templateName} v${x.templateVersion}` : ""}</>}
        chips={<><Chip tone={docTone(doc.status)}>{docLabel(doc.status)}</Chip><Chip tone={doc.classification === "Restricted" ? "crit" : doc.classification === "Confidential" ? "watch" : "muted"}>{doc.classification}</Chip>
          <Chip tone={doc.approvalState === "Approved" ? "ok" : "muted"}>Approval: {doc.approvalState}</Chip>
          {doc.signatureState !== "NotRequired" && <Chip tone={doc.signatureState === "Signed" ? "ok" : doc.signatureState === "Invalidated" ? "crit" : "muted"}>Signature: {doc.signatureState}</Chip>}
          {doc.overdue && <Chip tone="crit">Overdue at stage</Chip>}{mine.length > 0 && <Chip tone="info">Awaiting your action</Chip>}</>}
        tabs={<Tabs<DTab> value={tab} onChange={setTab} tabs={[{ key: "overview", label: "Overview" }, { key: "file", label: `File & versions (${x.versions.length})` }, { key: "route", label: `Route (${x.route.length})` },
          { key: "signatures", label: `Signatures (${x.signatureFields.length})` }, { key: "movement", label: `Movement (${x.movements.length})` }, { key: "amendments", label: `Amendments (${x.amendments.length})` }]} />}
        footer={<>
          {x.can.submit && <button className="btn primary" disabled={busy} onClick={() => submit(false)}>{doc.status === "Returned" ? "Resubmit" : "Submit on route"}</button>}
          {x.can.edit && <button className="btn" onClick={() => setEditing(true)}>Edit details</button>}
          {x.can.inform && <button className="btn" onClick={() => setInform(true)}>Mark for information</button>}
          {x.can.verify && <button className="btn" onClick={runVerify}>Verify signatures</button>}
          {x.can.withdraw && <button className="btn danger" onClick={() => setWithdraw(true)}>Withdraw</button>}
          {doc.taskId && <button className="linky" onClick={() => router.push(`/tasks?id=${doc.taskId}`)}>Task {doc.taskNumber}</button>}
          <span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>

        {tab === "overview" && <Overview x={x} onChanged={changed} onTab={setTab} />}
        {tab === "file" && <FilePanel x={x} onChanged={changed} />}
        {tab === "route" && <RoutePanel key={rev} x={x} onChanged={changed} />}
        {tab === "signatures" && <SignaturePanel key={rev} x={x} onChanged={changed} />}
        {tab === "movement" && <MovementPanel x={x} />}
        {tab === "amendments" && <AmendmentPanel x={x} onChanged={changed} />}
      </Modal>

      {editing && <RegisterForm existing={x} types={types.data ?? []} onClose={() => setEditing(false)} onSaved={(n) => { setEditing(false); changed(n, "Saved"); }} />}
      {warnings && <Modal open onClose={() => setWarnings(null)} size="narrow" title="Submit with warnings?"
        footer={<><button className="btn primary" disabled={busy} onClick={() => submit(true)}>Submit anyway</button><span className="spacer" /><button className="btn" onClick={() => { setWarnings(null); setTab("route"); }}>Review the route</button></>}>
        <Issues issues={warnings} /><div className="tiny">The warnings are recorded with the submission.</div>
      </Modal>}
      <Prompt open={withdraw} title={`Withdraw ${doc.number}`} label="Reason for withdrawal" required confirm="Withdraw" tone="danger" onCancel={() => setWithdraw(false)}
        onConfirm={async (r) => { try { changed(await post<DocumentDetailDto>(`/api/documents/${id}/withdraw`, { reason: r }), "Withdrawn"); setWithdraw(false); } catch (e) { toast(errText(e), true); } }} />
      {inform && <InformDialog x={x} onClose={() => setInform(false)} onDone={(n) => { setInform(false); changed(n, "Marked for information"); }} />}
      {verify && <VerifyModal v={verify} onClose={() => setVerify(null)} />}
    </>
  );
}

function Overview({ x, onChanged, onTab }: { x: DocumentDetailDto; onChanged: (n: DocumentDetailDto, m: string) => void; onTab: (t: DTab) => void }) {
  const doc = x.document;
  const returned = [...x.route].filter((r) => r.status === "Returned").sort((a, b) => (b.actedAt ?? "").localeCompare(a.actedAt ?? ""))[0];
  return (
    <>
      {doc.status === "Draft" && <Notice kind="info"><span>Draft. Upload the working file, check the route{x.signatureFields.length ? "" : " and place the signature field"}, then submit. {x.routeIssues.filter((i) => i.isError).length > 0 && <b>The route has {x.routeIssues.filter((i) => i.isError).length} problem(s) — see Route.</b>}</span></Notice>}
      {doc.status === "Returned" && returned && <Notice kind="watch"><span><b>Returned by {returned.actedByName} ({returned.name}):</b> {returned.comment}. Upload a revised version and resubmit.</span></Notice>}
      {doc.status === "Rejected" && <Notice kind="crit"><span><b>Rejected.</b> {x.route.find((r) => r.status === "Rejected")?.comment}</span></Notice>}
      {doc.status === "Completed" && <Notice kind="good"><span><b>Completed — {doc.outcome}</b> on {dateTime(doc.completedAt)}.</span></Notice>}
      {x.can.stages.map((s) => <ActionPanel key={s.stageId} x={x} s={s} onChanged={onChanged} />)}
      <div className="kv">
        <div><span>Initiator</span><b>{doc.initiatorName}</b></div>
        <div><span>Originating office</span><b>{doc.originOrgUnit ?? "—"}</b></div>
        <div><span>Reference</span><b>{doc.externalRef ?? "—"}</b></div>
        <div><span>Now with</span><b>{doc.currentHolders ?? "—"}</b></div>
        <div><span>Action requested</span><b>{doc.currentStage ?? "—"}</b></div>
        <div><span>Held since</span><b>{dateTime(doc.heldSince)}</b></div>
        <div><span>Stage due</span><b>{dateTime(doc.dueAt)}</b></div>
        <div><span>Registered</span><b>{dateTime(doc.createdAt)}</b></div>
        <div><span>Submitted</span><b>{dateTime(doc.submittedAt)}</b></div>
        <div><span>Active version</span><b>{doc.activeVersionNo ? `v${doc.activeVersionNo}` : "—"}</b></div>
        <div><span>Times returned</span><b>{doc.returnCount}</b></div>
        <div><span>Linked task</span><b>{doc.taskNumber ?? "—"}</b></div>
      </div>
      {x.notes && <div className="fsec"><h4>Notes</h4><div style={{ whiteSpace: "pre-wrap" }}>{x.notes}</div></div>}
      <div className="fsec"><h4>Route <button className="linky" onClick={() => onTab("route")}>details</button></h4><RouteTracker route={x.route} /></div>
    </>
  );
}

function ActionPanel({ x, s, onChanged }: { x: DocumentDetailDto; s: StageActionsDto; onChanged: (n: DocumentDetailDto, m: string) => void }) {
  const toast = useToast();
  const [comment, setComment] = useState(""); const [busy, setBusy] = useState(false); const [signing, setSigning] = useState(false);
  const active = x.versions.find((v) => v.isActive);
  const act = async (action: string) => {
    setBusy(true);
    try { onChanged(await post<DocumentDetailDto>(`/api/documents/${x.document.id}/stages/${s.stageId}/act`, { action, comment: comment || null, versionId: active?.id ?? 0 }), `${ACTION_LABEL[action] ?? action} recorded`); setComment(""); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const primary = ["Approve", "Concur", "Recommend", "Acknowledge", "Dispatch", "Sign"];
  return (
    <div className="card" style={{ borderColor: "var(--info)" }}>
      <div className="card-head"><div><h3>Your action: {s.stageName}</h3><div className="sub">{s.stageType} stage · on version v{active?.no ?? "—"}{s.actingFor ? ` · acting for ${s.actingFor}` : ""}</div></div></div>
      {s.stageType === "Information" && <Notice kind="info"><span>Marked for your information. Acknowledging is not an approval or concurrence.</span></Notice>}
      <div className="field"><label>Comment / reasons {s.actions.some((a) => ["Return", "Reject", "NotConcur"].includes(a)) ? "(required to return, reject or not concur)" : ""}</label>
        <textarea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} placeholder="Observations on this version" /></div>
      <div className="actions">
        {s.actions.filter((a) => a !== "Comment").map((a) => a === "Sign"
          ? <button key={a} className="btn primary" disabled={busy} onClick={() => setSigning(true)}>Sign…</button>
          : <button key={a} className={`btn ${primary.includes(a) ? "primary" : ["Reject", "NotConcur"].includes(a) ? "danger" : ""}`} disabled={busy || (["Return", "Reject", "NotConcur"].includes(a) && !comment.trim())} onClick={() => act(a)}>{ACTION_LABEL[a] ?? a}</button>)}
        <button className="btn" disabled={busy || !comment.trim()} onClick={() => act("Comment")}>Add comment only</button>
      </div>
      {signing && <SignDialog x={x} stageId={s.stageId} actingFor={s.actingFor} onClose={() => setSigning(false)} onDone={(n) => { setSigning(false); onChanged(n, "Signed — the signed output is recorded"); }} />}
    </div>
  );
}

function SignDialog({ x, stageId, actingFor, onClose, onDone }: { x: DocumentDetailDto; stageId: number; actingFor: string | null; onClose: () => void; onDone: (n: DocumentDetailDto) => void }) {
  const toast = useToast();
  const { user } = useAuth();
  const [pw, setPw] = useState(""); const [consent, setConsent] = useState(false); const [busy, setBusy] = useState(false);
  const v = x.versions.find((y) => y.isActive)!;
  const fields = x.signatureFields.filter((f) => f.routeStageId === stageId && f.status === "Pending");
  const sign = async () => {
    setBusy(true);
    try { onDone(await post<DocumentDetailDto>(`/api/documents/${x.document.id}/stages/${stageId}/sign`, { password: pw, versionId: v.id, consent })); }
    catch (e) { toast(errText(e), true); setPw(""); } finally { setBusy(false); }
  };
  return (
    <Modal open onClose={onClose} size="narrow" title="Sign the approved version" sub={`${x.document.number} · v${v.no} · ${v.fileName}`}
      footer={<><button className="btn primary" disabled={busy || !consent || !pw} onClick={sign}>{busy ? "Signing…" : "Sign"}</button><span className="spacer" /><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="kv" style={{ gridTemplateColumns: "1fr" }}>
        <div><span>Signer</span><b>{user?.name}{user?.designation ? `, ${user.designation}` : ""}</b></div>
        {actingFor && <div><span>Acting for</span><b>{actingFor}</b></div>}
        <div><span>Version</span><b>v{v.no} ({v.state})</b></div>
        <div><span>Placement</span><b>{fields.length ? fields.map((f) => `page ${f.page}`).join(", ") : "default (last page)"}</b></div>
      </div>
      <div className="hash">SHA-256 of the file being signed: {v.sha256}</div>
      <label className="tiny" style={{ display: "flex", gap: 8, alignItems: "flex-start" }}><input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} style={{ width: "auto", marginTop: 2 }} />
        I am signing exactly this version{actingFor ? `, for ${actingFor} under the recorded delegation` : " under my own authority"}. A later change to the file invalidates this signature.</label>
      <div className="field"><label>Confirm your password</label><input type="password" autoComplete="current-password" value={pw} onChange={(e) => setPw(e.target.value)} /></div>
      <Notice kind="info"><span>Controlled electronic signature: password re-confirmation, SHA-256 binding to the approved version, server seal and visible stamp. It is not a certificate-based digital signature.</span></Notice>
    </Modal>
  );
}

// ---- file and versions -----------------------------------------------------------------------------------
function FilePanel({ x, onChanged }: { x: DocumentDetailDto; onChanged: (n: DocumentDetailDto, m: string) => void }) {
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null); const [note, setNote] = useState(""); const [busy, setBusy] = useState(false);
  const [preview, setPreview] = useState<{ url: string; name: string } | null>(null);
  const [revise, setRevise] = useState(false);
  useEffect(() => () => { if (preview) URL.revokeObjectURL(preview.url); }, [preview]);
  const id = x.document.id;
  const send = async () => {
    if (!file) return;
    setBusy(true);
    const fd = new FormData(); fd.append("file", file); if (note) fd.append("note", note);
    try { onChanged(await upload<DocumentDetailDto>(`/api/documents/${id}/versions`, fd), "New version uploaded"); setFile(null); setNote(""); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const show = async (vid: number, name: string, type: string) => {
    try {
      const b = await fetchBlob(`/api/documents/${id}/versions/${vid}/file?inline=true`);
      if (type === "application/pdf" || type.startsWith("image/")) setPreview({ url: URL.createObjectURL(b), name });
      else { toast("This format has no in-browser preview; downloading instead."); await downloadFile(`/api/documents/${id}/versions/${vid}/file`, name); }
    } catch (e) { toast(errText(e), true); }
  };
  return (
    <>
      {x.can.edit && <div className="fsec"><h4>Upload a {x.versions.length ? "new" : "working"} version</h4>
        <div className="frow">
          <div className="field"><label>File ({x.type.allowedExtensions}, up to {x.type.maxSizeMb} MB)</label><input type="file" accept={x.type.allowedExtensions.split(",").map((e) => "." + e).join(",")} onChange={(e) => setFile(e.target.files?.[0] ?? null)} /></div>
          <div className="field"><label>What changed</label><input value={note} onChange={(e) => setNote(e.target.value)} /></div>
        </div>
        <div className="actions"><button className="btn primary" disabled={busy || !file} onClick={send}>Upload</button><span className="tiny">Earlier versions are kept; only the newest is active.</span></div>
      </div>}
      {x.can.revise && <Notice kind="watch"><span>This document is approved{x.document.signatureState === "Signed" ? " and signed" : ""}. A material change needs a <b>revision</b>: approvals lapse and signatures on the old version are invalidated. <button className="linky" onClick={() => setRevise(true)}>Revise…</button></span></Notice>}
      <Table rows={x.versions.length} empty="No file uploaded yet." cols={["Version", "File", "State", "Uploaded", "SHA-256", ""]}>
        {x.versions.map((v) => (
          <tr key={v.id}>
            <td><b>v{v.no}</b>{v.isActive && <div><Chip tone="info">Active</Chip></div>}</td>
            <td><div className="two-line"><b>{v.fileName}</b><small>{bytes(v.size)}{v.pages ? ` · ${v.pages} page(s)` : ""}{v.note ? ` · ${v.note}` : ""}</small></div></td>
            <td><Chip tone={v.state === "Signed" ? "ok" : v.state === "Approved" ? "ok" : v.state === "Superseded" ? "muted" : "info"}>{v.state}</Chip></td>
            <td className="tiny">{v.createdBy}<br />{dateTime(v.createdAt)}</td>
            <td><span className="hash">{v.sha256.slice(0, 20)}…</span></td>
            <td><div className="row-actions"><button className="btn small" onClick={() => show(v.id, v.fileName, v.contentType)}>Preview</button>
              <button className="btn small" onClick={() => downloadFile(`/api/documents/${id}/versions/${v.id}/file`, v.fileName).catch((e) => toast(errText(e), true))}>Download</button></div></td>
          </tr>
        ))}
      </Table>
      <div className="tiny">Previews and downloads are recorded in the audit trail.</div>
      {preview && <div className="fsec"><h4>Preview — {preview.name} <button className="linky" onClick={() => setPreview(null)}>close</button></h4>
        {preview.name.toLowerCase().endsWith(".pdf") ? <iframe className="preview-frame" src={preview.url} title="Preview" /> : <PreviewImage src={preview.url} alt={preview.name} />}
      </div>}
      {revise && <ReviseDialog x={x} onClose={() => setRevise(false)} onDone={(n) => { setRevise(false); onChanged(n, "Revised — approvals lapsed; re-route the document"); }} />}
    </>
  );
}

function ReviseDialog({ x, onClose, onDone }: { x: DocumentDetailDto; onClose: () => void; onDone: (n: DocumentDetailDto) => void }) {
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null); const [reason, setReason] = useState(""); const [busy, setBusy] = useState(false);
  const go = async () => {
    if (!file) return;
    setBusy(true);
    const fd = new FormData(); fd.append("file", file); fd.append("reason", reason);
    try { onDone(await upload<DocumentDetailDto>(`/api/documents/${x.document.id}/revise`, fd)); } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  return (
    <Modal open onClose={onClose} size="narrow" title="Material revision after approval" sub={x.document.number}
      footer={<><button className="btn danger" disabled={busy || !file || !reason.trim()} onClick={go}>Revise and invalidate</button><span className="spacer" /><button className="btn" onClick={onClose}>Cancel</button></>}>
      <Notice kind="crit"><span>Every approval lapses and every signature on the current version is invalidated. The document returns to you to re-route.</span></Notice>
      <div className="field"><label>Revised file</label><input type="file" onChange={(e) => setFile(e.target.files?.[0] ?? null)} /></div>
      <div className="field"><label>Reason (required)</label><textarea rows={3} value={reason} onChange={(e) => setReason(e.target.value)} /></div>
    </Modal>
  );
}

// ---- route --------------------------------------------------------------------------------------------------
type Line = { id: number | null; step: number; name: string; type: string; userId: string | null; mandatory: boolean; fromTemplate: boolean; canChange: boolean };

function RoutePanel({ x, onChanged }: { x: DocumentDetailDto; onChanged: (n: DocumentDetailDto, m: string) => void }) {
  const toast = useToast();
  const people = useAsync(() => get<UserRefDto[]>("/api/org/directory"), []);
  const templates = useAsync(() => get<TemplateDto[]>(`/api/workflow/templates?typeId=${x.document.documentTypeId}`), [x.document.documentTypeId]);
  const editable = x.document.status === "Draft" && x.can.edit;
  const toLines = (r: RouteStageDto[]): Line[] => r.filter((s) => !s.adHoc).sort((a, b) => a.step - b.step || a.id - b.id)
    .map((s) => ({ id: s.id, step: s.step, name: s.name, type: s.type, userId: s.userId, mandatory: s.mandatory, fromTemplate: s.fromTemplate, canChange: s.canChangeAssignee }));
  const [lines, setLines] = useState<Line[]>(() => toLines(x.route));
  const [busy, setBusy] = useState(false);
  const [issues, setIssues] = useState<RouteIssue[]>(x.routeIssues);
  const set = (i: number, k: keyof Line, v: unknown) => setLines(lines.map((l, j) => j === i ? { ...l, [k]: v } : l));
  const save = async () => {
    setBusy(true);
    try { onChanged(await put<DocumentDetailDto>(`/api/documents/${x.document.id}/route`, { lines: lines.map((l) => ({ id: l.id, step: l.step, name: l.name, type: l.type, userId: l.userId })) }), "Route saved and checked"); }
    catch (e) { toast(errText(e), true); setIssues(errIssues(e).length ? errIssues(e) : issues); } finally { setBusy(false); }
  };
  const applyTemplate = async (tid: number) => {
    try { onChanged(await post<DocumentDetailDto>(`/api/documents/${x.document.id}/route/template`, { templateId: tid }), "Template route loaded"); } catch (e) { toast(errText(e), true); }
  };
  if (!editable) return (
    <>
      <RouteTracker route={x.route} />
      <Table rows={x.route.length} cols={["Step", "Stage", "Officer", "Status", "Received", "Acted", "Held", "Version"]}>
        {[...x.route].sort((a, b) => a.step - b.step || a.id - b.id).map((s) => (
          <tr key={s.id}>
            <td className="num">{s.step}</td>
            <td><div className="two-line"><b>{s.name}</b><small>{s.type}{s.mandatory ? " · mandatory" : ""}{s.adHoc ? " · ad hoc" : ""}</small></div></td>
            <td>{s.userName}{s.actingForName && <div className="tiny">acted by {s.actedByName} for {s.actingForName}</div>}</td>
            <td><Chip tone={stageTone(s.status)}>{s.status}</Chip>{s.outcome && <div className="tiny">{ACTION_LABEL[s.outcome] ?? s.outcome}</div>}</td>
            <td className="tiny">{dateTime(s.receivedAt)}</td>
            <td className="tiny">{dateTime(s.actedAt)}</td>
            <td className="tiny">{hours(s.heldHours)}</td>
            <td className="tiny">{s.versionNo ? `v${s.versionNo}` : "—"}</td>
          </tr>
        ))}
      </Table>
      {x.document.status !== "Draft" && <div className="tiny">After submission the route changes only through an amendment (see Amendments).</div>}
    </>
  );
  return (
    <>
      <Notice kind="info"><span>Propose the route. Stages with the same step run in parallel. Mandatory stages (●) cannot be removed; fixed officers cannot be changed. The route is checked against the authority matrix each time you save.</span></Notice>
      <div className="frow">
        <div className="field"><label>Start from a template</label>
          <select value="" onChange={(e) => e.target.value && applyTemplate(+e.target.value)}>
            <option value="">— choose to replace the route —</option>{(templates.data ?? []).filter((t) => t.status === "Active").map((t) => <option key={t.id} value={t.id}>{t.name} (v{t.version})</option>)}
          </select></div>
      </div>
      <Table rows={lines.length} cols={["Step", "Stage name", "Type", "Officer", ""]}>
        {lines.map((l, i) => (
          <tr key={l.id ?? `n${i}`}>
            <td><input type="number" min={1} value={l.step} onChange={(e) => set(i, "step", +e.target.value)} style={{ width: 64 }} /></td>
            <td><input value={l.name} disabled={l.fromTemplate} onChange={(e) => set(i, "name", e.target.value)} style={{ width: "100%" }} />{l.mandatory && <span className="tiny" title="Mandatory"> ● mandatory</span>}</td>
            <td><select value={l.type} disabled={l.mandatory} onChange={(e) => set(i, "type", e.target.value)}>{STAGE_TYPES.map((t) => <option key={t}>{t}</option>)}</select></td>
            <td><UserSelect users={people.data ?? []} value={l.userId} disabled={!l.canChange} onChange={(v) => set(i, "userId", v)} /></td>
            <td><button className="btn small" disabled={l.mandatory} title={l.mandatory ? "Mandatory stage" : "Remove"} onClick={() => setLines(lines.filter((_, j) => j !== i))}>Remove</button></td>
          </tr>
        ))}
      </Table>
      <div className="actions">
        <button className="btn small" onClick={() => setLines([...lines, { id: null, step: Math.max(1, ...lines.map((l) => l.step)) , name: "Review", type: "Review", userId: null, mandatory: false, fromTemplate: false, canChange: true }])}>Add a stage</button>
        <button className="btn primary" disabled={busy} onClick={save}>Save and check route</button>
      </div>
      <Issues issues={issues} />
      {issues.length === 0 && <Notice kind="good"><span>The route passes every check.</span></Notice>}
      <RouteTracker route={x.route} />
    </>
  );
}

// ---- signatures -------------------------------------------------------------------------------------------
function SignaturePanel({ x, onChanged }: { x: DocumentDetailDto; onChanged: (n: DocumentDetailDto, m: string) => void }) {
  const toast = useToast();
  const signStages = x.route.filter((r) => r.type === "Signature" && !r.adHoc);
  const editable = x.can.edit;
  const pages = x.versions.find((v) => v.isActive)?.pages ?? 1;
  type F = { id: number | null; routeStageId: number; page: number; x: number; y: number; w: number; h: number; showName: boolean; showDate: boolean };
  const open = x.signatureFields.filter((f) => f.status === "Draft" || f.status === "Pending");
  const [fields, setFields] = useState<F[]>(() => open.map((f) => ({ id: f.id, routeStageId: f.routeStageId ?? 0, page: f.page, x: f.x, y: f.y, w: f.w, h: f.h, showName: f.showName, showDate: f.showDate })));
  const [sel, setSel] = useState(0);
  const [page, setPage] = useState(fields[0]?.page ?? pages);
  const [busy, setBusy] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const place = (e: React.MouseEvent) => {
    if (!editable || !fields[sel]) return;
    const r = ref.current!.getBoundingClientRect();
    const f = fields[sel];
    const nx = Math.min(1 - f.w, Math.max(0, (e.clientX - r.left) / r.width - f.w / 2));
    const ny = Math.min(1 - f.h, Math.max(0, (e.clientY - r.top) / r.height - f.h / 2));
    setFields(fields.map((y, i) => i === sel ? { ...y, x: +nx.toFixed(4), y: +ny.toFixed(4), page } : y));
  };
  const save = async () => {
    setBusy(true);
    try { onChanged(await put<DocumentDetailDto>(`/api/documents/${x.document.id}/signature-fields`, { fields }), "Signature placement saved"); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const signed = x.signatureFields.filter((f) => f.status === "Signed" || f.status === "Invalidated");
  return (
    <>
      {signStages.length === 0 && <Notice kind="info"><span>This route has no signature stage.</span></Notice>}
      {editable && signStages.length > 0 && <div className="split-2">
        <div>
          <div className="actions" style={{ marginBottom: 6 }}><span className="tiny">Page</span>
            <select value={page} onChange={(e) => setPage(+e.target.value)}>{Array.from({ length: Math.max(pages, ...fields.map((f) => f.page)) }, (_, i) => i + 1).map((p) => <option key={p} value={p}>{p}</option>)}</select>
            <span className="tiny">Click the page to place the selected box.</span></div>
          <div ref={ref} className="sigpage" onClick={place} role="img" aria-label={`Page ${page} with signature boxes`}>
            {fields.map((f, i) => f.page === page && (
              <div key={i} className={`sigbox ${i === sel ? "on" : ""}`} style={{ left: `${f.x * 100}%`, top: `${f.y * 100}%`, width: `${f.w * 100}%`, height: `${f.h * 100}%` }}
                onClick={(e) => { e.stopPropagation(); setSel(i); }}>{x.route.find((r) => r.id === f.routeStageId)?.userName ?? "Signer"}</div>
            ))}
          </div>
        </div>
        <div className="form">
          {fields.map((f, i) => {
            const st = x.route.find((r) => r.id === f.routeStageId);
            return (
              <div key={i} className={`fsec`} style={{ outline: i === sel ? "2px solid var(--primary-soft)" : undefined, padding: 6, borderRadius: 6 }} onClick={() => { setSel(i); setPage(f.page); }}>
                <h4>{st?.name ?? "Signature"} — {st?.userName ?? "no officer yet"}</h4>
                <div className="frow">
                  <div className="field"><label>Page</label><input type="number" min={1} value={f.page} onChange={(e) => setFields(fields.map((y, j) => j === i ? { ...y, page: +e.target.value } : y))} /></div>
                  <div className="field"><label>Left %</label><input type="number" step={1} value={Math.round(f.x * 100)} onChange={(e) => setFields(fields.map((y, j) => j === i ? { ...y, x: +e.target.value / 100 } : y))} /></div>
                  <div className="field"><label>Top %</label><input type="number" step={1} value={Math.round(f.y * 100)} onChange={(e) => setFields(fields.map((y, j) => j === i ? { ...y, y: +e.target.value / 100 } : y))} /></div>
                  <div className="field"><label>Width %</label><input type="number" step={1} value={Math.round(f.w * 100)} onChange={(e) => setFields(fields.map((y, j) => j === i ? { ...y, w: +e.target.value / 100 } : y))} /></div>
                  <div className="field"><label>Height %</label><input type="number" step={1} value={Math.round(f.h * 100)} onChange={(e) => setFields(fields.map((y, j) => j === i ? { ...y, h: +e.target.value / 100 } : y))} /></div>
                </div>
                <div className="actions">
                  <label className="tiny"><input type="checkbox" style={{ width: "auto" }} checked={f.showName} onChange={(e) => setFields(fields.map((y, j) => j === i ? { ...y, showName: e.target.checked } : y))} /> name</label>
                  <label className="tiny"><input type="checkbox" style={{ width: "auto" }} checked={f.showDate} onChange={(e) => setFields(fields.map((y, j) => j === i ? { ...y, showDate: e.target.checked } : y))} /> date</label>
                  <button className="btn small" onClick={() => setFields(fields.filter((_, j) => j !== i))}>Remove</button>
                </div>
              </div>
            );
          })}
          <div className="actions">
            {signStages.filter((s) => !fields.some((f) => f.routeStageId === s.id)).map((s) => <button key={s.id} className="btn small" onClick={() => { setFields([...fields, { id: null, routeStageId: s.id, page, x: 0.6, y: 0.8, w: 0.32, h: 0.09, showName: true, showDate: true }]); setSel(fields.length); }}>Add box for {s.name}</button>)}
            <button className="btn primary" disabled={busy} onClick={save}>Save placement</button>
          </div>
          <div className="tiny">Coordinates are stored with the document as page and fractions of the page. A signature stage without a box gets one at the bottom right of the last page.</div>
        </div>
      </div>}
      {!editable && open.length > 0 && <Table rows={open.length} cols={["Signer", "Stage", "Page", "Position", "Status"]}>
        {open.map((f) => <tr key={f.id}><td>{f.signerName}</td><td>{f.stageName}</td><td className="num">{f.page}</td><td className="tiny">left {Math.round(f.x * 100)}% · top {Math.round(f.y * 100)}%</td><td><Chip tone="info">{f.status}</Chip></td></tr>)}
      </Table>}
      {signed.length > 0 && <SignedTable x={x} rows={signed} />}
    </>
  );
}

function SignedTable({ x, rows }: { x: DocumentDetailDto; rows: SignatureFieldDto[] }) {
  const toast = useToast();
  return (
    <Card flush title="Signatures recorded" sub="Each signature is bound to the SHA-256 of the version signed and sealed by the server">
      <Table rows={rows.length} cols={["Signer", "Signed", "Version", "Code", "Document SHA-256", "Status", ""]}>
        {rows.map((f) => (
          <tr key={f.id}>
            <td><div className="two-line"><b>{f.signedByName}</b><small>{f.actingForName ? `for ${f.actingForName} · ` : ""}{f.authMethod}</small></div></td>
            <td className="tiny">{dateTime(f.signedAt)}</td>
            <td>v{f.versionNo}</td>
            <td className="mono">{f.signatureCode}</td>
            <td><span className="hash">{f.documentHash?.slice(0, 24)}…</span></td>
            <td><Chip tone={f.status === "Signed" ? "ok" : "crit"}>{f.status}</Chip>{f.invalidatedReason && <div className="tiny">{f.invalidatedReason}</div>}</td>
            <td><button className="btn small" onClick={() => downloadFile(`/api/documents/${x.document.id}/signatures/${f.id}/file`, `${x.document.number.replace(/\//g, "-")}-signed.pdf`).catch((e) => toast(errText(e), true))}>Stamped copy</button></td>
          </tr>
        ))}
      </Table>
    </Card>
  );
}

function VerifyModal({ v, onClose }: { v: VerificationDto; onClose: () => void }) {
  const ok = v.overall.startsWith("All");
  return (
    <Modal open onClose={onClose} title={`Verification — ${v.number}`} sub={`Checked ${dateTime(v.checkedAt)}`}
      footer={<><span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className={ok ? "verdict-ok" : "verdict-bad"} style={{ fontSize: 16 }}>{v.overall}</div>
      <VerifyTable v={v} />
      <div className="tiny">{v.mechanism}</div>
    </Modal>
  );
}
// ---- movement ---------------------------------------------------------------------------------------------
function MovementPanel({ x }: { x: DocumentDetailDto }) {
  return (
    <Table rows={x.movements.length} cols={["When", "Action", "From", "To", "Stage", "Version", "Held", "Comment"]}>
      {[...x.movements].reverse().map((m) => (
        <tr key={m.id}>
          <td className="tiny nowrap">{dateTime(m.at)}</td>
          <td><b>{m.action}</b>{m.actionRequested && <div className="tiny">for: {m.actionRequested}</div>}</td>
          <td className="tiny">{m.fromName ?? (m.action.startsWith("Escalated") ? "System" : "—")}{m.actingForName && <div>for {m.actingForName}</div>}</td>
          <td className="tiny">{m.toName ?? "—"}</td>
          <td className="tiny">{m.stageName ?? "—"}</td>
          <td className="tiny">{m.versionNo ? `v${m.versionNo}` : "—"}</td>
          <td className="tiny">{hours(m.heldHours)}</td>
          <td className="tiny" style={{ maxWidth: 320, whiteSpace: "pre-wrap" }}>{m.comment}</td>
        </tr>
      ))}
    </Table>
  );
}

// ---- amendments ------------------------------------------------------------------------------------------
function AmendmentPanel({ x, onChanged }: { x: DocumentDetailDto; onChanged: (n: DocumentDetailDto, m: string) => void }) {
  const toast = useToast();
  const people = useAsync(() => get<UserRefDto[]>("/api/org/directory"), []);
  const [ops, setOps] = useState<AmendmentOp[]>([]);
  const [reason, setReason] = useState(""); const [busy, setBusy] = useState(false);
  const [deciding, setDeciding] = useState<{ id: number; approve: boolean } | null>(null);
  const pending = x.route.filter((r) => !r.adHoc && (r.status === "Pending" || r.status === "Active"));
  const name = (uid: string | null) => people.data?.find((u) => u.id === uid)?.name ?? uid ?? "—";
  const describe = (o: AmendmentOp) => o.op === "add" ? `Add ${o.type} stage "${o.name ?? ""}" for ${name(o.userId)} at step ${o.step}`
    : o.op === "remove" ? `Remove "${x.route.find((r) => r.id === o.stageId)?.name ?? o.stageId}"` : `Reassign "${x.route.find((r) => r.id === o.stageId)?.name ?? o.stageId}" to ${name(o.userId)}`;
  const send = async () => {
    setBusy(true);
    try { onChanged(await post<DocumentDetailDto>(`/api/documents/${x.document.id}/amendments`, { reason, ops }), "Amendment recorded"); setOps([]); setReason(""); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const [draft, setDraft] = useState<AmendmentOp>({ op: "add", stageId: null, step: Math.max(1, ...pending.map((p) => p.step)), name: "", type: "Review", userId: null });
  return (
    <>
      {x.amendments.length === 0 && !x.can.amend && <Empty>No route amendments.</Empty>}
      {x.amendments.map((a) => (
        <div key={a.id} className="card">
          <div className="card-head"><div><h3>{a.reason}</h3><div className="sub">Requested by {a.requestedBy} · {dateTime(a.requestedAt)}{a.decidedBy ? ` · ${a.status.toLowerCase()} by ${a.decidedBy} ${dateTime(a.decidedAt)}` : ""}</div></div>
            <Chip tone={a.status === "Applied" ? "ok" : a.status === "Rejected" ? "crit" : "watch"}>{a.status}</Chip></div>
          <ul style={{ margin: 0, paddingLeft: 18 }}>{a.ops.map((o, i) => <li key={i}>{describe(o)}</li>)}</ul>
          {a.decisionNote && <div className="tiny">Note: {a.decisionNote}</div>}
          {a.canDecide && <div className="actions" style={{ marginTop: 8 }}><button className="btn primary" onClick={() => setDeciding({ id: a.id, approve: true })}>Approve amendment</button><button className="btn danger" onClick={() => setDeciding({ id: a.id, approve: false })}>Refuse</button></div>}
        </div>
      ))}
      {x.can.amend && <div className="fsec"><h4>Propose a route change</h4>
        <Notice kind="info"><span>After submission the route changes only with a reason{x.type.amendmentNeedsApproval ? " and the approval of your reporting officer or a workflow administrator" : ""}. Mandatory stages cannot be removed. Earlier versions of the route stay in the history.</span></Notice>
        <div className="frow">
          <div className="field"><label>Change</label><select value={draft.op} onChange={(e) => setDraft({ ...draft, op: e.target.value, stageId: null })}><option value="add">Add a stage</option><option value="remove">Remove a pending stage</option><option value="reassign">Change an officer</option></select></div>
          {draft.op !== "add" && <div className="field"><label>Stage</label><select value={draft.stageId ?? ""} onChange={(e) => setDraft({ ...draft, stageId: e.target.value ? +e.target.value : null })}>
            <option value="">—</option>{pending.filter((p) => draft.op === "reassign" || (p.status === "Pending" && !p.mandatory)).map((p) => <option key={p.id} value={p.id}>Step {p.step}: {p.name} ({p.userName})</option>)}</select></div>}
          {draft.op === "add" && <>
            <div className="field"><label>Type</label><select value={draft.type ?? "Review"} onChange={(e) => setDraft({ ...draft, type: e.target.value })}>{STAGE_TYPES.map((t) => <option key={t}>{t}</option>)}</select></div>
            <div className="field"><label>Step</label><input type="number" min={1} value={draft.step ?? 1} onChange={(e) => setDraft({ ...draft, step: +e.target.value })} /></div>
            <div className="field"><label>Name</label><input value={draft.name ?? ""} onChange={(e) => setDraft({ ...draft, name: e.target.value })} /></div>
          </>}
          {draft.op !== "remove" && <div className="field"><label>Officer</label><UserSelect users={people.data ?? []} value={draft.userId} onChange={(v) => setDraft({ ...draft, userId: v })} /></div>}
        </div>
        <div className="actions"><button className="btn small" disabled={(draft.op !== "add" && !draft.stageId) || (draft.op !== "remove" && !draft.userId)} onClick={() => setOps([...ops, draft])}>Add to the request</button></div>
        {ops.length > 0 && <ul style={{ margin: 0, paddingLeft: 18 }}>{ops.map((o, i) => <li key={i}>{describe(o)} <button className="linky" onClick={() => setOps(ops.filter((_, j) => j !== i))}>remove</button></li>)}</ul>}
        <div className="field"><label>Reason (required)</label><textarea rows={2} value={reason} onChange={(e) => setReason(e.target.value)} /></div>
        <div className="actions"><button className="btn primary" disabled={busy || !ops.length || !reason.trim()} onClick={send}>Submit amendment</button></div>
      </div>}
      <Prompt open={!!deciding} title={deciding?.approve ? "Approve route amendment" : "Refuse route amendment"} label="Note" required={!deciding?.approve} confirm={deciding?.approve ? "Approve" : "Refuse"} tone={deciding?.approve ? "primary" : "danger"}
        onCancel={() => setDeciding(null)} onConfirm={async (note) => { try { onChanged(await post<DocumentDetailDto>(`/api/documents/${x.document.id}/amendments/${deciding!.id}/decide`, { approve: deciding!.approve, note: note || null }), "Decision recorded"); setDeciding(null); } catch (e) { toast(errText(e), true); } }} />
    </>
  );
}

function InformDialog({ x, onClose, onDone }: { x: DocumentDetailDto; onClose: () => void; onDone: (n: DocumentDetailDto) => void }) {
  const toast = useToast();
  const people = useAsync(() => get<UserRefDto[]>("/api/org/directory"), []);
  const [ids, setIds] = useState<string[]>([]); const [note, setNote] = useState(""); const [busy, setBusy] = useState(false);
  const [pick, setPick] = useState<string | null>(null);
  const go = async () => {
    setBusy(true);
    try { onDone(await post<DocumentDetailDto>(`/api/documents/${x.document.id}/inform`, { userIds: ids, note: note || null })); } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  return (
    <Modal open onClose={onClose} size="narrow" title="Mark for information" sub={x.document.number}
      footer={<><button className="btn primary" disabled={busy || !ids.length} onClick={go}>Send for information</button><span className="spacer" /><button className="btn" onClick={onClose}>Cancel</button></>}>
      <Notice kind="info"><span>The officers receive the current version to acknowledge. This never counts as approval or concurrence and does not hold up the route.</span></Notice>
      <div className="field"><label>Add an officer</label><div className="actions"><UserSelect users={(people.data ?? []).filter((u) => !ids.includes(u.id))} value={pick} onChange={setPick} /><button className="btn small" disabled={!pick} onClick={() => { setIds([...ids, pick!]); setPick(null); }}>Add</button></div></div>
      {ids.map((i) => <div key={i} className="tiny">{people.data?.find((u) => u.id === i)?.name} <button className="linky" onClick={() => setIds(ids.filter((y) => y !== i))}>remove</button></div>)}
      <div className="field"><label>Note</label><textarea rows={2} value={note} onChange={(e) => setNote(e.target.value)} /></div>
    </Modal>
  );
}

function PreviewImage({ src, alt }: { src: string; alt: string }) {
  // an object URL of an authenticated download: next/image cannot optimise it
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={src} alt={alt} style={{ maxWidth: "100%" }} />;
}
