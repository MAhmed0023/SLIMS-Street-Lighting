"use client";
import { useState } from "react";
import { useRouter } from "next/navigation";
import { get, post } from "@/lib/api";
import type { NotificationDto } from "@/lib/types";
import { ago, dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Table, errText, useAsync, useToast } from "@/components/ui";

const TONE: Record<string, string> = { escalation: "crit", returned: "watch", deadline: "watch", blocked: "crit", assigned: "info", route: "info", decision: "ok", closed: "ok" };

export default function NotificationsPage() {
  const router = useRouter();
  const toast = useToast();
  const [unread, setUnread] = useState(false);
  const list = useAsync(() => get<NotificationDto[]>(`/api/notifications${unread ? "?unread=true" : ""}`), [unread]);
  const read = async (id?: number) => {
    try { await post(`/api/notifications/read${id ? `?id=${id}` : ""}`); window.dispatchEvent(new Event("pera:notifications")); list.reload(); } catch (e) { toast(errText(e), true); }
  };
  const open = async (n: NotificationDto) => {
    if (!n.readAt) await read(n.id);
    if (n.entityType === "task" && n.entityId) router.push(`/tasks?id=${n.entityId}`);
    else if (n.entityType === "document" && n.entityId) router.push(`/documents?id=${n.entityId}`);
  };
  return (
    <>
      <div className="filters">
        <div className="seg"><button className={!unread ? "active" : ""} onClick={() => setUnread(false)}>All</button><button className={unread ? "active" : ""} onClick={() => setUnread(true)}>Unread</button></div>
        <span className="spacer" /><button className="btn" onClick={() => read()}>Mark all as read</button>
      </div>
      <Card flush title="Notifications" sub="In-app notices; the time you read each one is recorded">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No notifications." cols={["", "Notice", "Kind", "When", ""]}>
            {list.data!.map((n) => (
              <tr key={n.id} className="row-link" onClick={() => open(n)} style={{ fontWeight: n.readAt ? 400 : 600 }}>
                <td>{!n.readAt && <span className="chip info" title="Unread">new</span>}</td>
                <td><div className="two-line"><b>{n.title}</b><small>{n.body}</small></div></td>
                <td><Chip tone={TONE[n.kind]}>{n.kind}</Chip></td>
                <td className="tiny nowrap">{dateTime(n.at)}<div>{ago(n.at)}</div></td>
                <td className="tiny">{n.readAt ? `read ${dateTime(n.readAt)}` : <button className="linky" onClick={(e) => { e.stopPropagation(); read(n.id); }}>Mark read</button>}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
    </>
  );
}
