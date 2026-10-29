"use client";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { useAuth } from "@/lib/auth";
import { get } from "@/lib/api";
import { date, today } from "@/lib/format";
import { PasswordDialog } from "./PasswordDialog";

const I: Record<string, ReactNode> = {
  dashboard: <svg viewBox="0 0 24 24"><rect x="3" y="3" width="8" height="8" rx="1.5" /><rect x="13" y="3" width="8" height="5" rx="1.5" /><rect x="13" y="10" width="8" height="11" rx="1.5" /><rect x="3" y="13" width="8" height="8" rx="1.5" /></svg>,
  tasks: <svg viewBox="0 0 24 24"><path d="M9 6h11M9 12h11M9 18h11" /><path d="M3.5 6l1.5 1.5L7.5 5M3.5 12l1.5 1.5 2.5-2.5M3.5 18l1.5 1.5 2.5-2.5" /></svg>,
  documents: <svg viewBox="0 0 24 24"><path d="M6 2h9l5 5v15H6z" /><path d="M15 2v5h5M9 13h7M9 17h5" /></svg>,
  approvals: <svg viewBox="0 0 24 24"><path d="M4 20h4l11-11-4-4L4 16z" /><path d="M13 7l4 4" /><path d="M14 20h6" /></svg>,
  notifications: <svg viewBox="0 0 24 24"><path d="M6 8a6 6 0 0112 0c0 7 3 8 3 8H3s3-1 3-8" /><path d="M10 20a2 2 0 004 0" /></svg>,
  reports: <svg viewBox="0 0 24 24"><path d="M4 19h16" /><path d="M7 16V9M12 16V5M17 16v-5" /></svg>,
  audit: <svg viewBox="0 0 24 24"><rect x="4" y="3" width="16" height="18" rx="2" /><path d="M8 9h8M8 13h8M8 17h5" /></svg>,
  verify: <svg viewBox="0 0 24 24"><path d="M12 3l7 3v6c0 4.5-3 7.6-7 9-4-1.4-7-4.5-7-9V6z" /><path d="M9 12l2 2 4-4" /></svg>,
  access: <svg viewBox="0 0 24 24"><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20a6.5 6.5 0 0113 0" /><path d="M17 11l1.5 1.5L22 9" /></svg>,
  hierarchy: <svg viewBox="0 0 24 24"><rect x="9" y="3" width="6" height="5" rx="1" /><rect x="3" y="16" width="6" height="5" rx="1" /><rect x="15" y="16" width="6" height="5" rx="1" /><path d="M12 8v4M6 16v-4h12v4" /></svg>,
  delegations: <svg viewBox="0 0 24 24"><path d="M4 8h13l-3-3M20 16H7l3 3" /></svg>,
  workflows: <svg viewBox="0 0 24 24"><circle cx="5" cy="6" r="2.2" /><circle cx="19" cy="6" r="2.2" /><circle cx="12" cy="18" r="2.2" /><path d="M7.2 6h9.6M6.2 8l4.6 8M17.8 8l-4.6 8" /></svg>,
  parameters: <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="3" /><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M5 19l2-2M17 7l2-2" /></svg>,
};

export interface NavItem { key: string; label: string; group: string; title: string; eyebrow: string; perm?: string }

export const NAV: NavItem[] = [
  { key: "dashboard", label: "Dashboard", group: "Dashboard", title: "Dashboard", eyebrow: "My work queue · portfolio status · bottlenecks", perm: "view" },
  { key: "tasks", label: "Tasks", group: "Work", title: "Tasks", eyebrow: "Assign down the hierarchy · progress · blockers · closure", perm: "view" },
  { key: "documents", label: "Documents", group: "Work", title: "Documents & Movement", eyebrow: "Register · route · review · concur · approve · sign · dispatch", perm: "view" },
  { key: "approvals", label: "Approvals & Signatures", group: "Work", title: "Approval & Signature Tracker", eyebrow: "Pending approvals and signatures · authority basis · signed versions", perm: "view" },
  { key: "notifications", label: "Notifications", group: "Work", title: "Notifications", eyebrow: "Assignments, returns, decisions, deadlines and escalations" },
  { key: "reports", label: "Reports", group: "Reporting", title: "Reports", eyebrow: "Aging · workload · turnaround · movement · signatures · audit", perm: "report" },
  { key: "audit", label: "Audit Trail", group: "Reporting", title: "Audit Trail", eyebrow: "Who did what, when, for whom and from where", perm: "view" },
  { key: "verify", label: "Verify a Signature", group: "Reporting", title: "Verify a Signature", eyebrow: "Check a signature code against the seal and the stored file", perm: "view" },
  { key: "access", label: "Users & Access", group: "Settings", title: "Users & Access", eyebrow: "Roles, designation, unit, reporting officer, passwords", perm: "configure" },
  { key: "hierarchy", label: "Hierarchy", group: "Settings", title: "Organisation Hierarchy", eyebrow: "Offices, wings, sections, designations, task categories", perm: "view" },
  { key: "delegations", label: "Acting Officers", group: "Settings", title: "Acting Officers & Delegation", eyebrow: "Who may act for whom, for what, and until when", perm: "view" },
  { key: "workflows", label: "Workflow Setup", group: "Settings", title: "Workflow Setup", eyebrow: "Document types · versioned route templates · authority matrix", perm: "view" },
  { key: "parameters", label: "Parameters", group: "Settings", title: "Application Parameters", eyebrow: "Reminders · escalation thresholds · file rules · sign-in", perm: "configure" },
];
const GROUPS = ["Dashboard", "Work", "Reporting", "Settings"];

export function Shell({ children }: { children: ReactNode }) {
  const { user, meta, logout, ready, can } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [menu, setMenu] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  const [pw, setPw] = useState(false);
  const [theme, setTheme] = useState<"light" | "dark" | null>(() => {
    if (typeof window === "undefined") return null;
    try { return localStorage.getItem("pera.theme") as "light" | "dark" | null; } catch { return null; /* private window */ }
  });
  const [unread, setUnread] = useState(0);

  // unread notices, refreshed every minute
  useEffect(() => {
    if (!user) return;
    let alive = true;
    const tick = () => get<{ count: number }>("/api/notifications/unread").then((r) => { if (alive) setUnread(r.count); }).catch(() => {});
    tick(); const t = setInterval(tick, 60000);
    const onRead = () => tick(); window.addEventListener("pera:notifications", onRead);
    return () => { alive = false; clearInterval(t); window.removeEventListener("pera:notifications", onRead); };
  }, [user]);

  useEffect(() => { if (ready && !user) router.replace("/login"); }, [ready, user, router]);
  useEffect(() => {
    const close = () => { setMenu(false); setOpen(null); };
    const click = (e: MouseEvent) => { if (!(e.target as Element | null)?.closest?.(".menubar, .umenu")) close(); };
    const esc = (e: KeyboardEvent) => { if (e.key === "Escape") close(); };
    document.addEventListener("click", click); document.addEventListener("keydown", esc);
    return () => { document.removeEventListener("click", click); document.removeEventListener("keydown", esc); };
  }, []);
  useEffect(() => { if (theme) document.documentElement.dataset.theme = theme; }, [theme]);
  const flipTheme = () => {
    const next = (theme ?? (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")) === "dark" ? "light" : "dark";
    setTheme(next);
    try { localStorage.setItem("pera.theme", next); } catch { /* ignore */ }
  };

  if (!ready || !user) return <div className="loading">Loading…</div>;
  const allowed = (n: NavItem) => !n.perm || can(n.perm);
  const current = NAV.find((n) => pathname.startsWith("/" + n.key)) ?? NAV[0];
  const initials = user.name.split(/[\s,()]+/).filter(Boolean).slice(0, 2).map((x) => x[0].toUpperCase()).join("");
  const scope = [user.designation, user.orgUnit].filter(Boolean).join(" · ") || user.roleLabel;

  return (
    <div className="app">
      <header className="globalbar">
        <div className="gb-brand">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img className="gb-logo" src="/pera-logo.svg" alt="" />
          <div>
            <div className="gb-title">{meta?.app.short ?? "PERA Workflow"} — {meta?.app.name ?? "Task, Document Movement & Approval System"}</div>
            <div className="gb-sub">{meta?.app.client} · {meta?.app.agency}</div>
          </div>
        </div>
        <div className="gb-right">
          <Link href="/notifications" className="iconbtn bell" title="Notifications" aria-label={`Notifications, ${unread} unread`}>
            <svg viewBox="0 0 24 24"><path d="M6 8a6 6 0 0112 0c0 7 3 8 3 8H3s3-1 3-8" /><path d="M10 20a2 2 0 004 0" /></svg>
            {unread > 0 && <span className="badge-count">{unread > 99 ? "99+" : unread}</span>}
          </Link>
          <button className="iconbtn" onClick={flipTheme} title="Light / dark" aria-label="Toggle theme">
            <svg viewBox="0 0 24 24"><path d="M21 12.8A9 9 0 1111.2 3a7 7 0 009.8 9.8z" /></svg>
          </button>
          <div className="umenu" onClick={(e) => e.stopPropagation()}>
            <button className="ubtn" onClick={() => { setOpen(null); setMenu((m) => !m); }} aria-haspopup="true" aria-expanded={menu}>
              <span className="avatar">{initials}</span>
              <span className="who"><b>{user.name}</b><span>{user.roleLabel}</span></span>
              <span className="caret">▾</span>
            </button>
            {menu && <div className="udrop">
              <div className="uhead"><b>{user.name}</b><div className="tiny">{user.designation ?? user.roleLabel}<br />{user.orgUnit}{user.reportsTo ? <><br />Reports to {user.reportsTo}</> : null}</div></div>
              <button onClick={() => { setMenu(false); setPw(true); }}>{user.hasPassword ? "Change password" : "Set password"}</button>
              {can("configure") && <button onClick={() => { setMenu(false); router.push("/access"); }}>Users &amp; Access</button>}
              <div className="usep" />
              <button className="danger" onClick={async () => { await logout(); router.replace("/login"); }}>Sign out</button>
            </div>}
          </div>
        </div>
      </header>

      <nav className="menubar">
        <div className="mb-items">
          {GROUPS.map((g) => {
            const items = NAV.filter((n) => n.group === g && allowed(n));
            if (!items.length) return null;
            const here = items.some((n) => n.key === current.key);
            if (items.length === 1) {
              const only = items[0];
              return <Link key={g} href={"/" + only.key} className={`mb-top${here ? " here" : ""}`} onClick={() => setOpen(null)}>{I[only.key]}<span>{only.label}</span></Link>;
            }
            if (g === "Work") return items.map((n) => <Link key={n.key} href={"/" + n.key} className={`mb-top${n.key === current.key ? " here" : ""}`} onClick={() => setOpen(null)}>{I[n.key]}<span>{n.label}</span></Link>);
            return (
              <div key={g} className="mb-menu">
                <button type="button" className={`mb-top${here ? " here" : ""}${open === g ? " on" : ""}`} aria-haspopup="true" aria-expanded={open === g}
                  onClick={() => { setMenu(false); setOpen(open === g ? null : g); }} onMouseEnter={() => { if (open) setOpen(g); }}>
                  <span>{g}</span><span className="caret">▾</span>
                </button>
                {open === g && <div className="mb-drop">
                  {items.map((n) => <Link key={n.key} href={"/" + n.key} className={n.key === current.key ? "active" : ""} onClick={() => setOpen(null)}>{I[n.key]}<span>{n.label}</span></Link>)}
                </div>}
              </div>
            );
          })}
        </div>
        <div className="mb-right">
          <span>{scope}</span>
          <span className="mb-sep" />
          <span>{date(today())}</span>
        </div>
      </nav>

      <main>
        <header className="topbar">
          <div className="titleblock">
            <div><div className="eyebrow">{current.eyebrow}</div><h2>{current.title}</h2></div>
          </div>
        </header>
        <section className="content">{children}</section>
      </main>
      <PasswordDialog open={pw || user.mustChangePassword} forced={user.mustChangePassword} onClose={() => setPw(false)} />
    </div>
  );
}
