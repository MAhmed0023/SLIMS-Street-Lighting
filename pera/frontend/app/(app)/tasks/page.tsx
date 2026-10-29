"use client";
import { useMemo, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { del, downloadFile, get, post, put, qs, upload } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { CategoryDto, TaskActivityDto, TaskDetailDto, TaskDto, UserRefDto } from "@/lib/types";
import { addDays, ago, bytes, date, dateTime, isoDate, priorityTone, taskLabel, taskTone } from "@/lib/format";
import { Bar, Card, Chip, Empty, ErrorBox, Loading, Modal, Notice, Prompt, Table, Tabs, UserSelect, errText, useAsync, useQueryId, useToast } from "@/components/ui";

const SCOPES = [{ k: "mine", l: "My tasks" }, { k: "assigned", l: "Assigned by me" }, { k: "team", l: "My team" }, { k: "review", l: "For my review" }, { k: "all", l: "All I can see" }];
const STATUSES = ["", "open", "overdue", "Draft", "Assigned", "InProgress", "Blocked", "SubmittedForReview", "Returned", "Completed", "Cancelled"];
const PRIORITIES = ["Low", "Normal", "High", "Urgent"];

export default function TasksPage() {
  const { can } = useAuth();
  // a dashboard card or notice can open this page pre-filtered: ?scope=&status=&owner=
  const params = useSearchParams();
  const [init] = useState(() => new URLSearchParams(params.toString()));
  const [scope, setScope] = useState(init.get("scope") ?? (init.get("owner") ? "all" : "mine"));
  const [status, setStatus] = useState(init.get("status") ?? "");
  const [owner, setOwner] = useState(init.get("owner") ?? "");
  const [q, setQ] = useState("");
  const [id, setId] = useQueryId();
  const [creating, setCreating] = useState<{ parentId?: number } | null>(null);
  const list = useAsync(() => get<TaskDto[]>(`/api/tasks${qs({ scope, status, q, ownerId: owner })}`), [scope, status, owner, q]);

  return (
    <>
      <div className="filters">
        <div className="seg">{SCOPES.map((s) => <button key={s.k} className={scope === s.k ? "active" : ""} onClick={() => setScope(s.k)}>{s.l}</button>)}</div>
        <select value={status} onChange={(e) => setStatus(e.target.value)} aria-label="Status">
          {STATUSES.map((s) => <option key={s} value={s}>{s === "" ? "Any status" : s === "open" ? "Open" : s === "overdue" ? "Overdue" : taskLabel(s)}</option>)}
        </select>
        <input type="search" placeholder="Search number or title" value={q} onChange={(e) => setQ(e.target.value)} />
        {owner && <button className="btn small" onClick={() => setOwner("")}>Owner: {owner} ×</button>}
        <span className="spacer" />
        {can("task") && <button className="btn primary" onClick={() => setCreating({})}>New task</button>}
      </div>

      <Card flush title="Tasks" sub="Every task has a named owner, a due date and an assigning officer; status changes are kept in its history">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No tasks in this view." cols={["Task", "Owner", "Assigned by", "Priority", "Due", "Progress", "Status"]}>
            {list.data!.map((t) => (
              <tr key={t.id} className="row-link" onClick={() => setId(t.id)}>
                <td><div className="two-line"><b>{t.title}</b><small><span className="mono">{t.number}</span>{t.parentNumber ? <> · sub-task of <span className="mono">{t.parentNumber}</span></> : null}{t.children > 0 ? ` · ${t.childrenDone}/${t.children} sub-tasks done` : ""}</small></div></td>
                <td>{t.ownerName ?? <span className="tiny">not set</span>}</td>
                <td className="tiny">{t.assignedByName}</td>
                <td><Chip tone={priorityTone(t.priority)}>{t.priority}</Chip></td>
                <td className="nowrap">{date(t.dueOn)}{t.overdue && <div><Chip tone="crit">{t.overdueDays} d late</Chip></div>}</td>
                <td style={{ minWidth: 120 }}><Bar value={t.progress} max={100} label={`${t.progress}%`} tone={t.status === "Completed" ? "ok" : t.overdue ? "crit" : undefined} /></td>
                <td><Chip tone={taskTone(t.status)}>{taskLabel(t.status)}</Chip>{t.escalationLevel > 0 && <div className="tiny err">escalated L{t.escalationLevel}</div>}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>

      {id && <TaskModal key={id} id={id} onClose={() => setId(null)} onChanged={list.reload} onOpen={setId} onNewSub={(pid) => setCreating({ parentId: pid })} />}
      {creating && <TaskForm parentId={creating.parentId} onClose={() => setCreating(null)} onSaved={(t) => { setCreating(null); list.reload(); setId(t.task.id); }} />}
    </>
  );
}

// ---- create / edit -------------------------------------------------------------------------------------
function TaskForm({ parentId, existing, onClose, onSaved }: { parentId?: number; existing?: TaskDetailDto; onClose: () => void; onSaved: (t: TaskDetailDto) => void }) {
  const toast = useToast();
  const { user } = useAuth();
  const people = useAsync(() => get<UserRefDto[]>("/api/org/assignable"), []);
  const everyone = useAsync(() => get<UserRefDto[]>("/api/org/directory"), []);
  const cats = useAsync(() => get<CategoryDto[]>("/api/org/categories"), []);
  const e = existing;
  const [f, setF] = useState({
    title: e?.task.title ?? "", purpose: e?.purpose ?? "", categoryId: e?.categoryId ?? null as number | null, priority: e?.task.priority ?? "Normal",
    ownerId: e?.task.ownerId ?? null as string | null, closureAuthorityId: e?.closureAuthorityId ?? null as string | null, dueOn: isoDate(e?.task.dueOn) || addDays(7),
    deliverable: e?.deliverable ?? "", instructions: e?.instructions ?? "", requireDocumentsComplete: e?.requireDocumentsComplete ?? false,
  });
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof f, v: unknown) => setF({ ...f, [k]: v });
  const chain = useMemo(() => {
    // the closure authority may be you or anyone above you
    const all = everyone.data ?? []; const out: UserRefDto[] = [];
    let cur = all.find((u) => u.id === user?.id);
    while (cur && out.length < 12) { out.push(cur); cur = all.find((u) => u.id === cur!.reportsToId); }
    return out;
  }, [everyone.data, user]);

  const save = async (assign: boolean) => {
    setBusy(true);
    try {
      const body = { ...f, purpose: f.purpose || null, deliverable: f.deliverable || null, instructions: f.instructions || null, dueOn: f.dueOn || null, parentId: parentId ?? null, assign };
      const t = e ? await put<TaskDetailDto>(`/api/tasks/${e.task.id}`, body) : await post<TaskDetailDto>("/api/tasks", body);
      toast(assign ? "Task assigned — the owner has been notified" : "Saved");
      onSaved(t);
    } catch (err) { toast(errText(err), true); } finally { setBusy(false); }
  };
  const draft = !e || e.task.status === "Draft";
  return (
    <Modal open onClose={onClose} title={e ? `Edit ${e.task.number}` : parentId ? "New sub-task" : "New task"} sub={e ? e.task.title : "Tasks are assigned down your reporting line"}
      footer={<>{draft && <button className="btn primary" disabled={busy} onClick={() => save(true)}>Save and assign</button>}
        <button className={`btn ${draft ? "" : "primary"}`} disabled={busy} onClick={() => save(false)}>{draft ? "Save as draft" : "Save changes"}</button>
        <span className="spacer" /><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="form">
        <div className="field"><label>Title</label><input value={f.title} onChange={(x) => set("title", x.target.value)} placeholder="Prepare the Q3 enforcement drive plan" /></div>
        <div className="frow">
          <div className="field"><label>Owner / responsible officer</label>
            <UserSelect users={people.data ?? []} value={f.ownerId} onChange={(v) => set("ownerId", v)} disabled={!draft} />
            <span className="hint">{draft ? "You, or an officer below you in the hierarchy." : "Use Reassign to change the owner."}</span></div>
          <div className="field"><label>Due date</label><input type="date" value={f.dueOn} onChange={(x) => set("dueOn", x.target.value)} /></div>
          <div className="field"><label>Priority</label><select value={f.priority} onChange={(x) => set("priority", x.target.value)}>{PRIORITIES.map((p) => <option key={p}>{p}</option>)}</select></div>
          <div className="field"><label>Category</label>
            <select value={f.categoryId ?? ""} onChange={(x) => set("categoryId", x.target.value ? +x.target.value : null)}>
              <option value="">—</option>{(cats.data ?? []).filter((c) => c.active).map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select></div>
        </div>
        <div className="field"><label>Expected deliverable</label><input value={f.deliverable} onChange={(x) => set("deliverable", x.target.value)} placeholder="Approved plan with deployment schedule and targets" /></div>
        <div className="field"><label>Purpose / objective</label><textarea rows={2} value={f.purpose} onChange={(x) => set("purpose", x.target.value)} /></div>
        <div className="field"><label>Instructions</label><textarea rows={3} value={f.instructions} onChange={(x) => set("instructions", x.target.value)} /></div>
        <div className="frow">
          <div className="field"><label>Closure authority</label>
            <UserSelect users={chain} value={f.closureAuthorityId} onChange={(v) => set("closureAuthorityId", v)} placeholder="— the assigning officer —" />
            <span className="hint">Accepts the output and closes the task.</span></div>
          <div className="field"><label>&nbsp;</label><label className="tiny" style={{ display: "flex", gap: 6, alignItems: "center" }}>
            <input type="checkbox" checked={f.requireDocumentsComplete} onChange={(x) => set("requireDocumentsComplete", x.target.checked)} style={{ width: "auto" }} />
            Linked documents must be completed before closure</label></div>
        </div>
      </div>
    </Modal>
  );
}

// ---- detail ----------------------------------------------------------------------------------------------
type TabKey = "overview" | "subtasks" | "activity" | "files" | "links";

function TaskModal({ id, onClose, onChanged, onOpen, onNewSub }: { id: number; onClose: () => void; onChanged: () => void; onOpen: (id: number) => void; onNewSub: (parentId: number) => void }) {
  const toast = useToast();
  const router = useRouter();
  const t = useAsync(() => get<TaskDetailDto>(`/api/tasks/${id}`), [id]);
  const [tab, setTab] = useState<TabKey>("overview");
  const [prompt, setPrompt] = useState<null | { action: string; title: string; label: string; required: boolean; confirm: string; tone?: "danger" }>(null);
  const [reassign, setReassign] = useState(false);
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);

  if (t.loading && !t.data) return <Modal open onClose={onClose} title="Task"><Loading /></Modal>;
  if (t.error) return <Modal open onClose={onClose} title="Task"><ErrorBox error={t.error} /></Modal>;
  const d = t.data!; const k = d.task;
  const can = (a: string) => d.allowedActions.includes(a);
  const done = (x: TaskDetailDto, msg: string) => { t.setData(x); onChanged(); toast(msg); };
  const act = async (action: string, reason?: string, ownerId?: string) => {
    setBusy(true);
    try { done(await post<TaskDetailDto>(`/api/tasks/${id}/actions/${action}`, { reason: reason || null, ownerId: ownerId ?? null }), MSG[action] ?? "Done"); setPrompt(null); setReassign(false); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };

  return (
    <>
      <Modal open onClose={onClose} size="wide" title={k.title}
        sub={<span className="crumbline">{d.ancestors.map((a) => <span key={a.id}><button className="linky" onClick={() => onOpen(a.id)}>{a.number}</button> ›</span>)}<span className="mono">{k.number}</span></span>}
        chips={<><Chip tone={taskTone(k.status)}>{taskLabel(k.status)}</Chip><Chip tone={priorityTone(k.priority)}>{k.priority}</Chip>
          {k.overdue && <Chip tone="crit">Overdue {k.overdueDays} d</Chip>}{k.escalationLevel > 0 && <Chip tone="watch">Escalated L{k.escalationLevel}</Chip>}
          {d.returnCount > 0 && <Chip tone="watch">Returned ×{d.returnCount}</Chip>}{k.category && <Chip tone="plain">{k.category}</Chip>}</>}
        tabs={<Tabs<TabKey> value={tab} onChange={setTab} tabs={[{ key: "overview", label: "Overview" }, { key: "subtasks", label: `Sub-tasks (${d.subTasks.length})` },
          { key: "activity", label: `Activity & comments (${d.activity.length})` }, { key: "files", label: `Attachments (${d.attachments.length})` }, { key: "links", label: `Dependencies & documents (${d.dependencies.length + d.documents.length})` }]} />}
        footer={<>
          {can("assign") && <button className="btn primary" disabled={busy} onClick={() => act("assign")}>Assign</button>}
          {can("start") && <button className="btn primary" disabled={busy} onClick={() => act("start")}>{k.status === "Returned" ? "Resume work" : "Accept and start"}</button>}
          {can("submit") && <button className="btn primary" disabled={busy} onClick={() => setPrompt({ action: "submit", title: "Submit for review", label: "Submission note", required: false, confirm: "Submit for review" })}>Submit for review</button>}
          {can("close") && <button className="btn primary" disabled={busy} onClick={() => setPrompt({ action: "close", title: "Accept output and close", label: "Closure note", required: false, confirm: "Close task" })}>Accept & close</button>}
          {can("return") && <button className="btn" disabled={busy} onClick={() => setPrompt({ action: "return", title: "Return for further work", label: "What needs to change", required: true, confirm: "Return" })}>Return</button>}
          {can("block") && <button className="btn" disabled={busy} onClick={() => setPrompt({ action: "block", title: "Record a blocker", label: "What is blocking the work", required: true, confirm: "Mark blocked", tone: "danger" })}>Report blocker</button>}
          {can("unblock") && <button className="btn" disabled={busy} onClick={() => setPrompt({ action: "unblock", title: "Blocker resolved", label: "How it was resolved", required: false, confirm: "Resume" })}>Blocker resolved</button>}
          {can("reopen") && <button className="btn" disabled={busy} onClick={() => setPrompt({ action: "reopen", title: "Reopen task", label: "Reason for reopening", required: true, confirm: "Reopen" })}>Reopen</button>}
          {can("reassign") && <button className="btn" disabled={busy} onClick={() => setReassign(true)}>Reassign</button>}
          {can("edit") && <button className="btn" onClick={() => setEditing(true)}>Edit</button>}
          {can("cancel") && <button className="btn danger" disabled={busy} onClick={() => setPrompt({ action: "cancel", title: "Cancel task", label: "Reason and authority for cancellation", required: true, confirm: "Cancel task", tone: "danger" })}>Cancel task</button>}
          <span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>

        {tab === "overview" && <>
          {k.status === "Blocked" && d.blocker && <Notice kind="crit"><span><b>Blocked for {k.blockedDays ?? 0} day(s):</b> {d.blocker}</span></Notice>}
          {d.openDependencies > 0 && <Notice kind="watch"><span>Depends on {d.openDependencies} task(s) that are not yet complete — see Dependencies.</span></Notice>}
          {d.closureNote && <Notice kind={k.status === "Cancelled" ? "crit" : "good"}><span><b>{k.status === "Cancelled" ? "Cancelled" : "Closed"} by {d.closedByName}:</b> {d.closureNote}</span></Notice>}
          <div className="kv">
            <div><span>Owner</span><b>{k.ownerName ?? "—"}</b></div>
            <div><span>Assigned by</span><b>{k.assignedByName}</b></div>
            <div><span>Closure authority</span><b>{d.closureAuthorityName}</b></div>
            <div><span>Due</span><b>{date(k.dueOn)}</b></div>
            <div><span>Forecast completion</span><b>{date(d.forecastOn)}</b></div>
            <div><span>Unit</span><b>{k.orgUnit ?? "—"}</b></div>
            <div><span>Created</span><b>{dateTime(k.createdAt)}</b></div>
            <div><span>Submitted</span><b>{dateTime(k.submittedAt)}</b></div>
            <div><span>Closed</span><b>{dateTime(k.closedAt)}</b></div>
          </div>
          <div className="grid g-2">
            <div className="fsec"><h4>Expected deliverable</h4><div>{d.deliverable ?? "—"}</div></div>
            <div className="fsec"><h4>Purpose</h4><div>{d.purpose ?? "—"}</div></div>
          </div>
          {d.instructions && <div className="fsec"><h4>Instructions</h4><div style={{ whiteSpace: "pre-wrap" }}>{d.instructions}</div></div>}
          <div className="fsec"><h4>Progress</h4>
            <Bar value={k.progress} max={100} label={`${k.progress}%`} tone={k.status === "Completed" ? "ok" : undefined} />
            {d.progressNote && <div style={{ whiteSpace: "pre-wrap" }}>{d.progressNote}</div>}
            {d.nextAction && <div className="tiny">Next action: {d.nextAction}</div>}
          </div>
          {can("progress") && <ProgressForm d={d} onSaved={(x) => done(x, "Progress updated")} />}
        </>}

        {tab === "subtasks" && <>
          {can("subtask") && <SubTaskForm parent={d} onSaved={(x) => done(x, "Sub-tasks created and assigned")} onFull={() => onNewSub(id)} />}
          <Table rows={d.subTasks.length} empty="No sub-tasks." cols={["Sub-task", "Owner", "Due", "Progress", "Status"]}>
            {d.subTasks.map((c) => (
              <tr key={c.id} className="row-link" onClick={() => onOpen(c.id)}>
                <td><div className="two-line"><b>{c.title}</b><small className="mono">{c.number}{c.children > 0 ? ` · ${c.childrenDone}/${c.children} below` : ""}</small></div></td>
                <td>{c.ownerName}</td>
                <td>{date(c.dueOn)}{c.overdue && <> <Chip tone="crit">{c.overdueDays} d late</Chip></>}</td>
                <td style={{ minWidth: 120 }}><Bar value={c.progress} max={100} label={`${c.progress}%`} /></td>
                <td><Chip tone={taskTone(c.status)}>{taskLabel(c.status)}</Chip></td>
              </tr>
            ))}
          </Table>
        </>}

        {tab === "activity" && <Activity d={d} onSaved={(x) => done(x, "Comment added")} />}
        {tab === "files" && <Files d={d} onSaved={(x) => done(x, "File attached")} />}
        {tab === "links" && <Links d={d} onSaved={(x) => done(x, "Updated")} onOpen={onOpen} onDoc={(docId) => router.push(`/documents?id=${docId}`)} onNewDoc={() => router.push(`/documents?new=1&task=${id}`)} />}
      </Modal>

      <Prompt open={!!prompt} title={prompt?.title ?? ""} label={prompt?.label ?? ""} required={prompt?.required} confirm={prompt?.confirm ?? "OK"} tone={prompt?.tone}
        onCancel={() => setPrompt(null)} onConfirm={(text) => act(prompt!.action, text)} />
      {reassign && <ReassignDialog d={d} busy={busy} onCancel={() => setReassign(false)} onConfirm={(owner, reason) => act("reassign", reason, owner)} />}
      {editing && <TaskForm existing={d} onClose={() => setEditing(false)} onSaved={(x) => { setEditing(false); done(x, "Saved"); }} />}
    </>
  );
}

const MSG: Record<string, string> = {
  assign: "Assigned — the owner has been notified", start: "Started", block: "Blocker recorded; your manager has been notified", unblock: "Work resumed", submit: "Submitted for review",
  close: "Closed", return: "Returned to the owner", reopen: "Reopened", cancel: "Cancelled", reassign: "Reassigned",
};

function ProgressForm({ d, onSaved }: { d: TaskDetailDto; onSaved: (x: TaskDetailDto) => void }) {
  const toast = useToast();
  const [p, setP] = useState(d.task.progress); const [note, setNote] = useState(d.progressNote ?? ""); const [next, setNext] = useState(d.nextAction ?? ""); const [fc, setFc] = useState(isoDate(d.forecastOn));
  const [busy, setBusy] = useState(false);
  const save = async () => {
    setBusy(true);
    try { onSaved(await post<TaskDetailDto>(`/api/tasks/${d.task.id}/progress`, { progress: p, progressNote: note || null, nextAction: next || null, forecastOn: fc || null })); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  return (
    <div className="fsec"><h4>Update progress</h4>
      <div className="frow">
        <div className="field"><label>Progress {p}%</label><input type="range" min={0} max={100} step={5} value={p} onChange={(e) => setP(+e.target.value)} /></div>
        <div className="field"><label>Next action</label><input value={next} onChange={(e) => setNext(e.target.value)} /></div>
        <div className="field"><label>Forecast completion</label><input type="date" value={fc} onChange={(e) => setFc(e.target.value)} /></div>
      </div>
      <div className="field"><label>Progress narrative</label><textarea rows={3} value={note} onChange={(e) => setNote(e.target.value)} /></div>
      <div className="actions"><button className="btn primary" disabled={busy} onClick={save}>Save progress</button></div>
    </div>
  );
}

function SubTaskForm({ parent, onSaved, onFull }: { parent: TaskDetailDto; onSaved: (x: TaskDetailDto) => void; onFull: () => void }) {
  const toast = useToast();
  const people = useAsync(() => get<UserRefDto[]>("/api/org/assignable"), []);
  const blank = () => ({ title: "", ownerId: null as string | null, dueOn: isoDate(parent.task.dueOn) || addDays(7), deliverable: "", priority: "Normal" });
  const [rows, setRows] = useState([blank()]);
  const [busy, setBusy] = useState(false);
  const set = (i: number, k: string, v: unknown) => setRows(rows.map((r, j) => j === i ? { ...r, [k]: v } : r));
  const save = async () => {
    setBusy(true);
    try { onSaved(await post<TaskDetailDto>(`/api/tasks/${parent.task.id}/subtasks`, { lines: rows.map((r) => ({ ...r, instructions: null })) })); setRows([blank()]); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  return (
    <div className="fsec"><h4>Break down into sub-tasks</h4>
      <Table rows={rows.length} cols={["Sub-task", "Owner", "Due", "Deliverable", "Priority", ""]}>
        {rows.map((r, i) => (
          <tr key={i}>
            <td><input value={r.title} onChange={(e) => set(i, "title", e.target.value)} placeholder="Title" style={{ width: "100%" }} /></td>
            <td><UserSelect users={people.data ?? []} value={r.ownerId} onChange={(v) => set(i, "ownerId", v)} /></td>
            <td><input type="date" value={r.dueOn} max={isoDate(parent.task.dueOn) || undefined} onChange={(e) => set(i, "dueOn", e.target.value)} /></td>
            <td><input value={r.deliverable} onChange={(e) => set(i, "deliverable", e.target.value)} placeholder="Expected output" style={{ width: "100%" }} /></td>
            <td><select value={r.priority} onChange={(e) => set(i, "priority", e.target.value)}>{PRIORITIES.map((p) => <option key={p}>{p}</option>)}</select></td>
            <td><button className="btn small" onClick={() => setRows(rows.length === 1 ? [blank()] : rows.filter((_, j) => j !== i))}>Remove</button></td>
          </tr>
        ))}
      </Table>
      <div className="actions">
        <button className="btn small" onClick={() => setRows([...rows, blank()])}>Add another</button>
        <button className="btn primary" disabled={busy || rows.some((r) => !r.title || !r.ownerId || !r.deliverable)} onClick={save}>Create and assign</button>
        <button className="linky" onClick={onFull}>Full form…</button>
      </div>
    </div>
  );
}

function Activity({ d, onSaved }: { d: TaskDetailDto; onSaved: (x: TaskDetailDto) => void }) {
  const toast = useToast();
  const [text, setText] = useState(""); const [reply, setReply] = useState<TaskActivityDto | null>(null); const [busy, setBusy] = useState(false);
  const byId = new Map(d.activity.map((a) => [a.id, a]));
  const send = async () => {
    setBusy(true);
    try { onSaved(await post<TaskDetailDto>(`/api/tasks/${d.task.id}/comments`, { text, replyToId: reply?.id ?? null })); setText(""); setReply(null); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const tone = (a: TaskActivityDto) => a.event === "comment" ? "comment" : a.event === "escalated" || a.newValue === "Blocked" ? "crit" : a.newValue === "Completed" ? "ok" : a.event === "reassigned" || a.newValue === "Returned" ? "watch" : "";
  return (
    <>
      {d.allowedActions.includes("comment") && <div className="fsec"><h4>{reply ? `Reply to ${reply.actorName}` : "Add a comment"}</h4>
        <textarea rows={3} value={text} onChange={(e) => setText(e.target.value)} placeholder="Write a comment. Mention an officer with @user-id to notify them." />
        <div className="actions"><button className="btn primary" disabled={busy || !text.trim()} onClick={send}>Post comment</button>{reply && <button className="btn" onClick={() => setReply(null)}>Cancel reply</button>}
          <span className="tiny">Comments are recorded against the current stage: {taskLabel(d.task.status)}.</span></div>
      </div>}
      <div className="timeline">
        {[...d.activity].reverse().map((a) => (
          <div key={a.id} className="tl-item">
            <span className={`tl-dot ${tone(a)}`} />
            <div className="tl-body">
              <div><b>{a.actorId === "system" ? "System" : a.actorName}</b>{a.actingForName && <span className="tiny"> for {a.actingForName}</span>} · {EVENT[a.event] ?? a.event}
                {a.oldValue || a.newValue ? <> — {a.oldValue && <span className="muted">{a.oldValue} → </span>}<b>{a.newValue}</b></> : null}</div>
              {a.replyToId && byId.get(a.replyToId) && <div className="tiny">in reply to {byId.get(a.replyToId)!.actorName}: “{(byId.get(a.replyToId)!.comment ?? "").slice(0, 80)}”</div>}
              {a.comment && <div className={a.event === "comment" ? "tl-quote" : "tl-text"}>{a.comment}</div>}
              {a.fileName && <div className="tiny">File: {a.fileName}</div>}
              <div className="tl-meta">{dateTime(a.at)} · {ago(a.at)} · stage {taskLabel(a.stage)}
                {a.event === "comment" && d.allowedActions.includes("comment") && <> · <button className="linky" onClick={() => setReply(a)}>Reply</button></>}</div>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
const EVENT: Record<string, string> = { created: "created the task", assigned: "assigned to", status: "changed status", progress: "updated progress", comment: "commented", reassigned: "reassigned",
  attachment: "attached a file", dependency: "changed dependencies", escalated: "escalated", "blocker-cleared": "cleared the blocker", subtask: "sub-task", document: "document", updated: "edited the task" };

function Files({ d, onSaved }: { d: TaskDetailDto; onSaved: (x: TaskDetailDto) => void }) {
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null); const [kind, setKind] = useState("evidence"); const [note, setNote] = useState(""); const [busy, setBusy] = useState(false);
  const send = async () => {
    if (!file) return;
    setBusy(true);
    const fd = new FormData(); fd.append("file", file); fd.append("kind", kind); if (note) fd.append("note", note);
    try { onSaved(await upload<TaskDetailDto>(`/api/tasks/${d.task.id}/attachments`, fd)); setFile(null); setNote(""); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  return (
    <>
      {d.allowedActions.includes("attach") && <div className="fsec"><h4>Attach a file</h4>
        <div className="frow">
          <div className="field"><label>File</label><input type="file" onChange={(e) => setFile(e.target.files?.[0] ?? null)} /></div>
          <div className="field"><label>Kind</label><select value={kind} onChange={(e) => setKind(e.target.value)}><option value="evidence">Evidence</option><option value="deliverable">Deliverable</option><option value="instruction">Instruction</option></select></div>
          <div className="field"><label>Note</label><input value={note} onChange={(e) => setNote(e.target.value)} /></div>
        </div>
        <div className="actions"><button className="btn primary" disabled={busy || !file} onClick={send}>Upload</button><span className="tiny">Uploading a file does not complete the task — the closure authority closes it.</span></div>
      </div>}
      <Table rows={d.attachments.length} empty="No files attached." cols={["File", "Kind", "Stage", "Uploaded", { t: "Size", c: "r" }, ""]}>
        {d.attachments.map((a) => (
          <tr key={a.id}>
            <td><div className="two-line"><b>{a.fileName}</b><small className="hash">SHA-256 {a.sha256.slice(0, 16)}…</small></div>{a.note && <div className="tiny">{a.note}</div>}</td>
            <td><Chip tone="plain">{a.kind}</Chip></td>
            <td className="tiny">{taskLabel(a.stage)}</td>
            <td className="tiny">{a.uploadedBy}<br />{dateTime(a.uploadedAt)}</td>
            <td className="r tiny">{bytes(a.size)}</td>
            <td><button className="btn small" onClick={() => downloadFile(`/api/tasks/${d.task.id}/attachments/${a.id}/file`, a.fileName).catch((e) => toast(errText(e), true))}>Download</button></td>
          </tr>
        ))}
      </Table>
    </>
  );
}

function Links({ d, onSaved, onOpen, onDoc, onNewDoc }: { d: TaskDetailDto; onSaved: (x: TaskDetailDto) => void; onOpen: (id: number) => void; onDoc: (id: number) => void; onNewDoc: () => void }) {
  const toast = useToast();
  const { can } = useAuth();
  const [q, setQ] = useState("");
  const found = useAsync(() => q.trim().length >= 2 ? get<TaskDto[]>(`/api/tasks${qs({ scope: "all", q })}`) : Promise.resolve([] as TaskDto[]), [q]);
  const add = async (depId: number) => { try { onSaved(await post<TaskDetailDto>(`/api/tasks/${d.task.id}/dependencies`, { dependsOnId: depId })); setQ(""); } catch (e) { toast(errText(e), true); } };
  const remove = async (depId: number) => { try { onSaved(await del<TaskDetailDto>(`/api/tasks/${d.task.id}/dependencies/${depId}`)); } catch (e) { toast(errText(e), true); } };
  const editable = d.allowedActions.includes("dependency");
  return (
    <div className="grid g-2">
      <Card title="Depends on" sub="Work that must finish first">
        {d.dependencies.length === 0 ? <Empty>No dependencies.</Empty> : d.dependencies.map((x) => (
          <div key={x.id} className="kv" style={{ gridTemplateColumns: "1fr" }}><div>
            <span><button className="linky" onClick={() => onOpen(x.id)}>{x.number}</button> {x.title}</span>
            <b><Chip tone={taskTone(x.status)}>{taskLabel(x.status)}</Chip> {editable && <button className="btn small" onClick={() => remove(x.id)}>Remove</button>}</b>
          </div></div>
        ))}
        {editable && <div className="field" style={{ marginTop: 10 }}><label>Add a dependency</label>
          <input type="search" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Type a task number or title" />
          {(found.data ?? []).filter((x) => x.id !== d.task.id).slice(0, 6).map((x) => <button key={x.id} className="linky" onClick={() => add(x.id)}>{x.number} — {x.title}</button>)}
        </div>}
      </Card>
      <Card title="Documents for this task" sub="Document cases registered against it" actions={can("document") && !["Completed", "Cancelled"].includes(d.task.status) ? <button className="btn small" onClick={onNewDoc}>Register a document</button> : undefined}>
        {d.documents.length === 0 ? <Empty>No documents linked.</Empty> : (
          <Table rows={d.documents.length} cols={["Document", "Status", "With"]}>
            {d.documents.map((x) => <tr key={x.id} className="row-link" onClick={() => onDoc(x.id)}><td><div className="two-line"><b>{x.subject}</b><small className="mono">{x.number}</small></div></td><td>{x.status}</td><td className="tiny">{x.currentHolder || "—"}</td></tr>)}
          </Table>
        )}
        {d.requireDocumentsComplete && <Notice kind="info"><span>This task closes only after its documents are completed.</span></Notice>}
      </Card>
    </div>
  );
}

function ReassignDialog({ d, busy, onCancel, onConfirm }: { d: TaskDetailDto; busy: boolean; onCancel: () => void; onConfirm: (owner: string, reason: string) => void }) {
  const people = useAsync(() => get<UserRefDto[]>("/api/org/assignable"), []);
  const [owner, setOwner] = useState<string | null>(null); const [reason, setReason] = useState("");
  return (
    <Modal open onClose={onCancel} size="narrow" title={`Reassign ${d.task.number}`} sub={`Now with ${d.task.ownerName}`}
      footer={<><button className="btn primary" disabled={busy || !owner || !reason.trim()} onClick={() => onConfirm(owner!, reason.trim())}>Reassign</button><span className="spacer" /><button className="btn" onClick={onCancel}>Cancel</button></>}>
      <div className="field"><label>New owner</label><UserSelect users={(people.data ?? []).filter((u) => u.id !== d.task.ownerId)} value={owner} onChange={setOwner} /></div>
      <div className="field"><label>Reason (required)</label><textarea rows={3} value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Officer transferred / on leave / workload" /></div>
      <Notice kind="info"><span>The original assignment stays in the history. The new owner must acknowledge the task.</span></Notice>
    </Modal>
  );
}
