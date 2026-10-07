"use client";
import { useState } from "react";
import { useRouter } from "next/navigation";
import { get, post, put } from "@/lib/api";
import type { NotificationDto, NotifyPrefsDto } from "@/lib/types";
import { ago, dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

const bump = () => window.dispatchEvent(new Event("slims:notifications"));

/** FR-NTF-001/002/005: the inbox, and which optional notices to receive. Assigned work cannot be switched off. */
export default function NotificationsPage() {
  const toast = useToast();
  const router = useRouter();
  const [unread, setUnread] = useState(false);
  const list = useAsync(() => get<NotificationDto[]>(`/api/notifications?take=200${unread ? "&unread=true" : ""}`), [unread]);
  const prefs = useAsync(() => get<NotifyPrefsDto>("/api/notifications/prefs"), []);
  const [saving, setSaving] = useState(false);

  const read = async (id: number | null) => { try { await post("/api/notifications/read", { id }); list.reload(); bump(); } catch (e) { toast(errText(e), true); } };
  const open = async (n: NotificationDto) => {
    if (!n.readAt) await read(n.id);
    if (n.link?.startsWith("/api/")) window.open(n.link, "_blank"); else if (n.link) router.push(n.link);
  };
  const toggle = async (code: string) => {
    if (!prefs.data) return;
    const muted = prefs.data.muted.includes(code) ? prefs.data.muted.filter((c) => c !== code) : [...prefs.data.muted, code];
    setSaving(true);
    try { prefs.setData(await put<NotifyPrefsDto>("/api/notifications/prefs", { muted, email: prefs.data.email, sms: prefs.data.sms })); } catch (e) { toast(errText(e), true); } finally { setSaving(false); }
  };
  const channel = async (k: "email" | "sms") => {
    if (!prefs.data) return;
    try { prefs.setData(await put<NotifyPrefsDto>("/api/notifications/prefs", { muted: prefs.data.muted, email: k === "email" ? !prefs.data.email : prefs.data.email, sms: k === "sms" ? !prefs.data.sms : prefs.data.sms })); } catch (e) { toast(errText(e), true); }
  };

  return (
    <div className="split wide">
      <Card flush title="Inbox" actions={<>
        <div className="seg"><button className={!unread ? "active" : ""} onClick={() => setUnread(false)}>All</button><button className={unread ? "active" : ""} onClick={() => setUnread(true)}>Unread</button></div>
        <button className="btn small" onClick={() => read(null)}>Mark all read</button></>}>
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No notifications." cols={["", "Notice", "When", "Delivery"]}>
            {list.data!.map((n) => (
              <tr key={n.id} className="row-link" onClick={() => open(n)} style={{ fontWeight: n.readAt ? 400 : 600 }}>
                <td>{!n.readAt && <span className="dot" style={{ display: "inline-block", width: 8, height: 8, borderRadius: 4, background: "var(--primary)" }} />}</td>
                <td>
                  <div>{n.title} {n.level > 0 && n.level < 9 && <Chip tone="crit">escalated</Chip>} {n.mandatory && <Chip tone="plain">assigned work</Chip>}</div>
                  <div className="tiny" style={{ whiteSpace: "pre-line", fontWeight: 400 }}>{n.body}</div>
                  <div className="tiny muted">{n.code} · {n.label}</div>
                </td>
                <td className="tiny" title={dateTime(n.createdAt)}>{ago(n.createdAt)}</td>
                <td className="tiny">{n.emailStatus !== "None" && <div>e-mail: {n.emailStatus}</div>}{n.smsStatus !== "None" && <div>SMS: {n.smsStatus}</div>}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      <Card title="What I receive" sub="Assigned work — approvals, exceptions, re-captures, field verification — cannot be switched off (FR-NTF-005)">
        {prefs.loading ? <Loading /> : prefs.error ? <ErrorBox error={prefs.error} /> : <>
          <div className="actions" style={{ marginBottom: 8 }}>
            <label className="tiny"><input type="checkbox" checked={prefs.data!.email} onChange={() => channel("email")} /> Copies by e-mail</label>
            <label className="tiny"><input type="checkbox" checked={prefs.data!.sms} onChange={() => channel("sms")} /> Copies by SMS</label>
          </div>
          <Notice kind="info">E-mail and SMS go out only where the administrator has configured the department&apos;s mail server and SMS gateway, and only to the address and mobile number on your account.</Notice>
          {prefs.data!.codes.map((c) => (
            <label key={c.code} style={{ display: "flex", gap: 8, alignItems: "center", padding: "5px 0", borderTop: "1px solid var(--line)", opacity: c.mandatory ? .8 : 1 }}>
              <input type="checkbox" disabled={c.mandatory || saving} checked={c.mandatory || !prefs.data!.muted.includes(c.code)} onChange={() => toggle(c.code)} />
              <span style={{ flex: 1 }}>{c.label} <span className="tiny muted">{c.code}</span></span>
              {c.mandatory && <Chip tone="plain">always on</Chip>}
            </label>
          ))}
        </>}
      </Card>
    </div>
  );
}
