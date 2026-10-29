"use client";
import { useRouter } from "next/navigation";
import { get } from "@/lib/api";
import type { DashboardDto, QueueItemDto } from "@/lib/types";
import { ago, date, dateTime, hours, taskLabel } from "@/lib/format";
import { Card, Chip, Empty, ErrorBox, HBars, Kpi, Loading, Table, useAsync } from "@/components/ui";

const TASK_TONE: Record<string, string> = { Blocked: "crit", Returned: "watch", Completed: "ok", Cancelled: "muted", Draft: "muted", SubmittedForReview: "info" };
const RISK_TONE: Record<string, string> = { overdue: "crit", blocked: "crit", escalated: "watch", "due-soon": "watch", review: "info" };

export default function DashboardPage() {
  const router = useRouter();
  const d = useAsync(() => get<DashboardDto>("/api/dashboard"), []);
  if (d.loading && !d.data) return <Loading what="dashboard" />;
  if (d.error) return <ErrorBox error={d.error} />;
  const x = d.data!;
  const open = (q: QueueItemDto) => router.push(q.kind === "task" ? `/tasks?id=${q.id}` : `/documents?id=${q.id}`);
  const count = (k: string) => x.myCounts.find((c) => c.key === k)?.count ?? 0;
  const myLinks: Record<string, string> = {
    "my-open": "/tasks?scope=mine&status=open", "my-overdue": "/tasks?scope=mine&status=overdue", "my-review": "/tasks?scope=review",
    "my-docs": "/documents?box=inbox", "my-sign": "/documents?box=inbox", "my-returned": "/documents?box=returned", unread: "/notifications",
  };

  return (
    <div className="grid">
      <div className="tiny">{x.scope} · updated {dateTime(x.generatedAt)} <button className="linky" onClick={d.reload}>Refresh</button></div>
      <div className="grid g-kpi">
        {x.myCounts.map((c) => <Kpi key={c.key} label={c.label} value={c.count}
          tone={c.count > 0 && ["my-overdue", "my-returned"].includes(c.key) ? "crit" : c.count > 0 && ["my-docs", "my-sign", "my-review"].includes(c.key) ? "watch" : undefined}
          onClick={() => router.push(myLinks[c.key])} />)}
      </div>

      <div className="grid g-2">
        <Card flush title="My work queue" sub={`${x.myQueue.length} item(s) awaiting you, overdue first`}>
          {x.myQueue.length === 0 ? <Empty>Nothing is waiting for you.</Empty> : (
            <div className="queue">
              {x.myQueue.map((q, i) => (
                <div key={i} className="qrow" onClick={() => open(q)} role="button" tabIndex={0} onKeyDown={(e) => { if (e.key === "Enter") open(q); }}>
                  <span className={`qk ${q.kind === "document" ? "doc" : ""}`} title={q.kind}>
                    {q.kind === "task"
                      ? <svg viewBox="0 0 24 24"><path d="M9 6h11M9 12h11M9 18h11M3.5 6l1.5 1.5L7.5 5" /></svg>
                      : <svg viewBox="0 0 24 24"><path d="M6 2h9l5 5v15H6z" /><path d="M15 2v5h5" /></svg>}
                  </span>
                  <span className="qt"><b>{q.title}</b><small><span className="mono">{q.number}</span> · {q.action}{q.actingFor ? ` · acting for ${q.actingFor}` : ""}</small></span>
                  <span className="qr">
                    {q.overdue ? <Chip tone="crit">Overdue</Chip> : q.dueAt ? <>due {date(q.dueAt)}</> : null}
                    {q.priority && ["High", "Urgent"].includes(q.priority) && <> <Chip tone={q.priority === "Urgent" ? "crit" : "watch"}>{q.priority}</Chip></>}
                    <div className="tiny">{q.receivedAt ? `received ${ago(q.receivedAt)}` : ""}</div>
                  </span>
                </div>
              ))}
            </div>
          )}
        </Card>
        <div className="grid">
          <Card title="Task risk" sub="Open tasks you can see — select a bar to open the list">
            <HBars data={x.taskRisk.map((c) => ({ key: c.key, label: c.label, value: c.count, tone: RISK_TONE[c.key] }))}
              onPick={(k) => router.push(k === "overdue" ? "/tasks?scope=all&status=overdue" : k === "blocked" ? "/tasks?scope=all&status=Blocked" : k === "review" ? "/tasks?scope=all&status=SubmittedForReview" : "/tasks?scope=all&status=open")} />
          </Card>
          <Card title="Overdue tasks by age" sub="Days past the due date">
            <HBars data={x.aging.map((c) => ({ key: c.key, label: c.label, value: c.count, tone: "crit" }))} onPick={() => router.push("/tasks?scope=all&status=overdue")} />
          </Card>
        </div>
      </div>

      <div className="grid g-2">
        <Card title="Tasks by status" sub="Everything in your portfolio">
          <HBars data={x.taskStatus.filter((c) => c.count > 0 || ["Assigned", "InProgress", "Blocked"].includes(c.key)).map((c) => ({ key: c.key, label: taskLabel(c.key), value: c.count, tone: TASK_TONE[c.key] }))}
            onPick={(k) => router.push(`/tasks?scope=all&status=${k}`)} />
        </Card>
        <Card title="Documents" sub={`${x.returnedDocuments} returned now · ${x.reworkCycles} rework cycle(s) in total`}>
          <HBars data={[...x.documentRisk.map((c) => ({ key: "r-" + c.key, label: c.label, value: c.count, tone: c.key === "overdue" ? "crit" : c.key === "returned" ? "watch" : "info" })),
            ...x.documentStatus.filter((c) => c.count > 0).map((c) => ({ key: "s-" + c.key, label: c.label, value: c.count, tone: c.key === "Completed" ? "ok" : "muted" }))]}
            onPick={(k) => router.push(k === "r-approval" ? "/documents?box=approval" : k === "r-signature" ? "/documents?box=signature" : k.startsWith("s-") ? `/documents?box=all&status=${k.slice(2)}` : k === "r-returned" ? "/documents?box=all&status=Returned" : "/documents?box=all&status=InRoute")} />
        </Card>
      </div>

      <div className="grid g-2">
        <Card flush title="Officer-wise status" sub="Task owners in your portfolio, most overdue first">
          <Table rows={x.ownerLoad.length} empty="No assigned tasks yet." cols={["Officer", { t: "Open", c: "r" }, { t: "Overdue", c: "r" }, { t: "Blocked", c: "r" }, { t: "In review", c: "r" }, { t: "Done", c: "r" }]}>
            {x.ownerLoad.map((o) => (
              <tr key={o.userId} className="row-link" onClick={() => router.push(`/tasks?scope=all&owner=${o.userId}`)}>
                <td><div className="two-line"><b>{o.name}</b><small>{o.designation}</small></div></td>
                <td className="r num">{o.open}</td>
                <td className="r num">{o.overdue > 0 ? <span className="err">{o.overdue}</span> : 0}</td>
                <td className="r num">{o.blocked > 0 ? <span className="err">{o.blocked}</span> : 0}</td>
                <td className="r num">{o.submitted}</td>
                <td className="r num">{o.completed}</td>
              </tr>
            ))}
          </Table>
        </Card>
        <Card flush title="Bottlenecks — where documents are waiting" sub="Current holders, longest wait first">
          <Table rows={x.bottlenecks.length} empty="No document is waiting at a stage." cols={["Holder", { t: "Waiting", c: "r" }, { t: "Oldest", c: "r" }, { t: "Overdue", c: "r" }]}>
            {x.bottlenecks.map((b) => (
              <tr key={b.userId} className="row-link" onClick={() => router.push(`/documents?box=all&holder=${b.userId}`)}>
                <td><div className="two-line"><b>{b.name}</b><small>{b.designation}</small></div></td>
                <td className="r num">{b.pending}</td>
                <td className="r num">{hours(b.oldestHours)}</td>
                <td className="r num">{b.overdue > 0 ? <span className="err">{b.overdue}</span> : 0}</td>
              </tr>
            ))}
          </Table>
        </Card>
      </div>

      <div className="grid g-2">
        <Card flush title="Blocked tasks" sub="Oldest blocker first">
          <Table rows={x.blockedTasks.length} empty="No blocked tasks." cols={["Task", "Owner", { t: "Blocked for", c: "r" }, "Due"]}>
            {x.blockedTasks.map((t) => (
              <tr key={t.id} className="row-link" onClick={() => router.push(`/tasks?id=${t.id}`)}>
                <td><div className="two-line"><b>{t.title}</b><small className="mono">{t.number}</small></div></td>
                <td>{t.ownerName}</td>
                <td className="r num"><span className="err">{t.blockedDays ?? 0} d</span></td>
                <td>{date(t.dueOn)}{t.overdue && <> <Chip tone="crit">{t.overdueDays} d late</Chip></>}</td>
              </tr>
            ))}
          </Table>
        </Card>
        <Card flush title="Average time at each stage" sub="Completed document stages, last 180 days">
          <Table rows={x.turnaround.length} empty="No completed stages yet." cols={["Stage type", { t: "Completed", c: "r" }, { t: "Average", c: "r" }, { t: "Longest", c: "r" }]}>
            {x.turnaround.map((t) => (
              <tr key={t.stageType}><td>{t.stageType}</td><td className="r num">{t.completed}</td><td className="r num">{hours(t.avgHours)}</td><td className="r num">{hours(t.maxHours)}</td></tr>
            ))}
          </Table>
        </Card>
      </div>
      {count("unread") > 0 && <div className="tiny">You have {count("unread")} unread notification(s).</div>}
    </div>
  );
}
