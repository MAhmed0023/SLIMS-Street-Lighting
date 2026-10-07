"use client";
import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { useAuth } from "@/lib/auth";
import { get } from "@/lib/api";
import { date, today } from "@/lib/format";
import type { MyMobileDto } from "@/lib/types";
import { PasswordDialog } from "./PasswordDialog";

const I: Record<string, ReactNode> = {
  dashboard: <svg viewBox="0 0 24 24"><rect x="3" y="3" width="8" height="8" rx="1.5" /><rect x="13" y="3" width="8" height="5" rx="1.5" /><rect x="13" y="10" width="8" height="11" rx="1.5" /><rect x="3" y="13" width="8" height="8" rx="1.5" /></svg>,
  inward: <svg viewBox="0 0 24 24"><path d="M12 3v11m0 0l-4-4m4 4l4-4" /><path d="M4 17v2a2 2 0 002 2h12a2 2 0 002-2v-2" /></svg>,
  stock: <svg viewBox="0 0 24 24"><rect x="3" y="7" width="18" height="13" rx="2" /><path d="M3 11h18M8 7V4h8v3M8 11v9M16 11v9" /></svg>,
  articles: <svg viewBox="0 0 24 24"><rect x="3" y="3" width="7" height="7" rx="1" /><rect x="14" y="3" width="7" height="7" rx="1" /><rect x="3" y="14" width="7" height="7" rx="1" /><path d="M14 14h3v3h-3zM18 18h3v3h-3z" /></svg>,
  requisitions: <svg viewBox="0 0 24 24"><rect x="4" y="3" width="16" height="18" rx="2" /><path d="M8 8h8M8 12h8M8 16h5" /></svg>,
  issue: <svg viewBox="0 0 24 24"><path d="M3 7h13v10H3z" /><path d="M16 10h3l2 3v4h-5" /><circle cx="7" cy="19" r="1.6" /><circle cx="18" cy="19" r="1.6" /></svg>,
  field: <svg viewBox="0 0 24 24"><path d="M12 21s7-6.2 7-11a7 7 0 10-14 0c0 4.8 7 11 7 11z" /><circle cx="12" cy="10" r="2.6" /></svg>,
  returns: <svg viewBox="0 0 24 24"><path d="M3 10a9 9 0 1 1 2 6" /><path d="M3 5v5h5" /></svg>,
  "purchase-orders": <svg viewBox="0 0 24 24"><path d="M6 2h9l5 5v15H6z" /><path d="M15 2v5h5M9 13h7M9 17h5" /></svg>,
  vendors: <svg viewBox="0 0 24 24"><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20a6.5 6.5 0 0113 0" /><path d="M17 8h5M17 12h5M17 16h5" /></svg>,
  sites: <svg viewBox="0 0 24 24"><path d="M18 54V16" /><path d="M9 21V6h7a4 4 0 014 4v2" /><path d="M5 21h8" /><circle cx="19" cy="15" r="2.5" /></svg>,
  stores: <svg viewBox="0 0 24 24"><path d="M3 9l2-5h14l2 5" /><path d="M4 9v11a1 1 0 001 1h14a1 1 0 001-1V9" /><path d="M9 21v-6h6v6" /></svg>,
  reports: <svg viewBox="0 0 24 24"><path d="M4 19h16" /><path d="M7 16V9M12 16V5M17 16v-5" /></svg>,
  audit: <svg viewBox="0 0 24 24"><rect x="4" y="3" width="16" height="18" rx="2" /><path d="M8 9h8M8 13h8M8 17h5" /></svg>,
  access: <svg viewBox="0 0 24 24"><circle cx="9" cy="8" r="3.5" /><path d="M2.5 20a6.5 6.5 0 0113 0" /><path d="M17 11l1.5 1.5L22 9" /></svg>,
  parameters: <svg viewBox="0 0 24 24"><circle cx="12" cy="12" r="3" /><path d="M12 2v3M12 19v3M2 12h3M19 12h3M5 5l2 2M17 17l2 2M5 19l2-2M17 7l2-2" /></svg>,
  putaway: <svg viewBox="0 0 24 24"><rect x="3" y="4" width="18" height="16" rx="2" /><path d="M3 12h18M12 4v16M15 8l-3 3-3-3" /></svg>,
  transfers: <svg viewBox="0 0 24 24"><path d="M4 8h13l-3-3M20 16H7l3 3" /></svg>,
  adjustments: <svg viewBox="0 0 24 24"><path d="M4 7h10M18 7h2M4 17h2M10 17h10" /><circle cx="16" cy="7" r="2" /><circle cx="8" cy="17" r="2" /></svg>,
  counts: <svg viewBox="0 0 24 24"><path d="M9 4H5a1 1 0 00-1 1v15a1 1 0 001 1h14a1 1 0 001-1V5a1 1 0 00-1-1h-4" /><rect x="9" y="2" width="6" height="4" rx="1" /><path d="M8 13l2.5 2.5L16 10" /></svg>,
  amendments: <svg viewBox="0 0 24 24"><path d="M4 20h4l11-11-4-4L4 16z" /><path d="M13 7l4 4" /></svg>,
  explore: <svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="7" /><path d="M21 21l-4.3-4.3M8 11h6M11 8v6" /></svg>,
  map: <svg viewBox="0 0 24 24"><path d="M9 4L3 6v14l6-2 6 2 6-2V4l-6 2z" /><path d="M9 4v14M15 6v14" /></svg>,
  "mobile-setup": <svg viewBox="0 0 24 24"><rect x="7" y="2" width="10" height="20" rx="2" /><path d="M11 18h2" /></svg>,
  notifications: <svg viewBox="0 0 24 24"><path d="M6 8a6 6 0 0112 0c0 7 3 8 3 8H3s3-1 3-8" /><path d="M10 20a2 2 0 004 0" /></svg>,
};

export interface NavItem { key: string; label: string; group: string; title: string; eyebrow: string; perm?: string }

export const NAV: NavItem[] = [
  { key: "dashboard", label: "Dashboard", group: "Dashboard", title: "Operations Dashboard", eyebrow: "Stock · installation · exceptions", perm: "view" },
  { key: "inward", label: "Goods Receipt", group: "Inventory", title: "Goods Receipt (GRN)", eyebrow: "Receive by quantity against purchase order", perm: "receive" },
  { key: "stock", label: "Stock Position", group: "Inventory", title: "Stock Position", eyebrow: "Store ▸ rack ▸ bin, location-wise", perm: "view" },
  { key: "articles", label: "Articles & QR Labels", group: "Inventory", title: "Articles & QR Labels", eyebrow: "Item codes created article-wise with PO reference", perm: "view" },
  { key: "putaway", label: "Put-away & Bin Moves", group: "Inventory", title: "Put-away & Bin Moves", eyebrow: "Receiving area → stock bin, by scanning item and bin", perm: "view" },
  { key: "transfers", label: "Store Transfers", group: "Inventory", title: "Inter-store Transfers", eyebrow: "Despatch leg · in transit · receipt leg", perm: "view" },
  { key: "adjustments", label: "Stock Adjustments", group: "Inventory", title: "Stock Adjustments", eyebrow: "Reason code · second-person approval", perm: "view" },
  { key: "counts", label: "Physical Verification", group: "Inventory", title: "Physical Verification", eyebrow: "Scan-based count · variance against book stock", perm: "view" },
  { key: "requisitions", label: "Requisitions", group: "Operations", title: "Digital Requisitions", eyebrow: "Site demand · availability · approval", perm: "view" },
  { key: "issue", label: "Issue to Site", group: "Operations", title: "Issue Against Requisition", eyebrow: "Issue by quantity · gate pass · custody to site", perm: "issue" },
  { key: "field", label: "Field Installation", group: "Operations", title: "Field Installation & Evidence", eyebrow: "Geo-tagged before/after · geo-fence verification", perm: "view" },
  { key: "returns", label: "Returns & Condemnation", group: "Operations", title: "Out-of-Order Returns", eyebrow: "Quarantine · usable · warranty · repair · scrap", perm: "view" },
  { key: "amendments", label: "Amendments", group: "Operations", title: "Amendment Requests", eyebrow: "Corrections to completed records, with approval", perm: "view" },
  { key: "purchase-orders", label: "Purchase Orders", group: "Procurement", title: "Purchase Orders", eyebrow: "PO lines · receipt balance · discrepancies", perm: "view" },
  { key: "vendors", label: "Vendors", group: "Procurement", title: "Vendors", eyebrow: "Supply and warranty counterparties", perm: "view" },
  { key: "sites", label: "Site Locations", group: "Master data", title: "Site Locations", eyebrow: "Coordinates and geo-fence per site", perm: "view" },
  { key: "stores", label: "Stores & Bins", group: "Master data", title: "Stores & Bin Locations", eyebrow: "Store ▸ section ▸ rack ▸ bin", perm: "view" },
  { key: "reports", label: "Reports", group: "Reporting", title: "Reports", eyebrow: "Generated from the transaction record · scheduled delivery", perm: "view" },
  { key: "explore", label: "Drill-down", group: "Reporting", title: "Drill-down", eyebrow: "Summary → area → site → document → item → evidence", perm: "view" },
  { key: "map", label: "Map", group: "Reporting", title: "Installation Map", eyebrow: "Installations and pending sites", perm: "view" },
  { key: "audit", label: "Audit Trail", group: "Reporting", title: "Audit Trail", eyebrow: "Who did what, when and from where", perm: "view" },
  { key: "access", label: "Users & Access", group: "Settings", title: "Users & Access", eyebrow: "Roles, scope and passwords", perm: "configure" },
  { key: "parameters", label: "Parameters", group: "Settings", title: "Application Parameters", eyebrow: "Geo-fence · tolerances · approval matrix · alerts", perm: "configure" },
  { key: "mobile-setup", label: "Mobile Setup", group: "Settings", title: "Mobile Setup", eyebrow: "Bind your phone · SLIMS Field app" },
  { key: "notifications", label: "Notifications", group: "Settings", title: "Notifications", eyebrow: "Your work and alerts" },
];
const GROUPS = ["Dashboard", "Inventory", "Operations", "Procurement", "Master data", "Reporting", "Settings"];

export function Shell({ children }: { children: ReactNode }) {
  const { user, meta, logout, ready, can } = useAuth();
  const router = useRouter();
  const pathname = usePathname();
  const [menu, setMenu] = useState(false);
  const [open, setOpen] = useState<string | null>(null);
  const [pw, setPw] = useState(false);
  const [theme, setTheme] = useState<"light" | "dark" | null>(null);
  const [unread, setUnread] = useState(0);
  const [mobile, setMobile] = useState<MyMobileDto | null>(null);
  const { config } = useAuth();

  // unread notices, refreshed every minute
  useEffect(() => {
    if (!user) return;
    let alive = true;
    const tick = () => get<{ count: number }>("/api/notifications/unread").then((r) => { if (alive) setUnread(r.count); }).catch(() => {});
    tick(); const t = setInterval(tick, 60000);
    const onRead = () => tick(); window.addEventListener("slims:notifications", onRead);
    return () => { alive = false; clearInterval(t); window.removeEventListener("slims:notifications", onRead); };
  }, [user]);
  // FR-SEC-009: officers must bind a phone before field work (mobile setup is mandatory)
  const needsPhone = !!user && !!config?.field?.mobileSetupRequired && !!user.permissions?.install && user.roleKey !== "sysadmin";
  useEffect(() => {
    if (!needsPhone) return;
    const load = () => get<MyMobileDto>("/api/mobile/mine").then(setMobile).catch(() => {});
    load(); window.addEventListener("slims:mobile", load);
    return () => window.removeEventListener("slims:mobile", load);
  }, [needsPhone, pathname]);

  useEffect(() => { if (ready && !user) router.replace("/login"); }, [ready, user, router]);
  useEffect(() => {
    const close = () => { setMenu(false); setOpen(null); };
    const click = (e: MouseEvent) => { if (!(e.target as Element | null)?.closest?.(".menubar, .umenu")) close(); };
    const esc = (e: KeyboardEvent) => { if (e.key === "Escape") close(); };
    document.addEventListener("click", click); document.addEventListener("keydown", esc);
    return () => { document.removeEventListener("click", click); document.removeEventListener("keydown", esc); };
  }, []);
  useEffect(() => {
    try { const t = localStorage.getItem("slims.theme") as "light" | "dark" | null; if (t) { setTheme(t); document.documentElement.dataset.theme = t; } } catch { /* private window */ }
  }, []);
  const flipTheme = () => {
    const next = (theme ?? (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light")) === "dark" ? "light" : "dark";
    setTheme(next); document.documentElement.dataset.theme = next;
    try { localStorage.setItem("slims.theme", next); } catch { /* ignore */ }
  };

  if (!ready || !user) return <div className="loading">Loading…</div>;
  const allowed = (n: NavItem) => !n.perm || can(n.perm);
  const current = NAV.find((n) => pathname.startsWith("/" + n.key)) ?? NAV[0];
  const initials = user.name.split(/[\s,]+/).filter(Boolean).slice(0, 2).map((x) => x[0].toUpperCase()).join("");

  return (
    <div className="app">
      <header className="globalbar">
        <div className="gb-brand">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img className="gb-logo" src="/slims-logo.svg" alt="" />
          <div>
            <div className="gb-title">{meta?.app.short ?? "SLIMS"} — {meta?.app.name ?? "Street Lighting Store"}</div>
            <div className="gb-sub">{meta?.app.client} · {meta?.app.agency}</div>
          </div>
        </div>
        <div className="gb-right">
          <span className="gb-chip"><i />Live</span>
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
              <div className="uhead"><b>{user.name}</b><div className="tiny">{user.designation ?? user.roleLabel}<br />{user.scopeDescription}</div></div>
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
          <span>{user.scopeDescription}</span>
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
        <section className="content">
          {needsPhone && mobile && !mobile.active && !["/mobile-setup", "/notifications"].some((p) => pathname.startsWith(p))
            ? <div className="card gate">
                <div className="gate-icon">{I["mobile-setup"]}</div>
                <h3>Mobile setup is required</h3>
                <p>Lighting Officers record installation evidence with the <b>SLIMS Field</b> app on their own phone. Bind your phone and take the setup photograph before you continue.</p>
                <div className="actions" style={{ justifyContent: "center" }}><Link className="btn primary" href="/mobile-setup">Start mobile setup</Link></div>
                <div className="tiny" style={{ marginTop: 10 }}>{mobile.devices.some((d) => d.status === "AwaitingApproval") ? "Your new phone is waiting for the administrator's approval." : "It takes about two minutes. You need your phone and a GPS fix outdoors."}</div>
              </div>
            : children}
        </section>
      </main>
      <PasswordDialog open={pw} onClose={() => setPw(false)} />
    </div>
  );
}
