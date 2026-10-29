"use client";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { ApiError } from "@/lib/api";
import { int } from "@/lib/format";
import type { RouteIssue, UserRefDto } from "@/lib/types";

// ---- toast -----------------------------------------------------------------
type Toast = (msg: string, bad?: boolean) => void;
const ToastCtx = createContext<Toast>(() => {});
export function ToastProvider({ children }: { children: ReactNode }) {
  const [msg, setMsg] = useState<{ text: string; bad?: boolean } | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const toast = useCallback<Toast>((text, bad) => {
    setMsg({ text, bad });
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setMsg(null), bad ? 7000 : 3500);
  }, []);
  return <ToastCtx.Provider value={toast}>{children}{msg && <div id="toast" className={msg.bad ? "bad" : ""} role="status">{msg.text}</div>}</ToastCtx.Provider>;
}
export const useToast = () => useContext(ToastCtx);
export const errText = (e: unknown) => e instanceof Error ? e.message : String(e);
/** Route validation issues the API attached to a refusal, if any. */
export const errIssues = (e: unknown): RouteIssue[] => e instanceof ApiError && Array.isArray(e.detail) ? (e.detail as RouteIssue[]) : [];

// ---- data hook -------------------------------------------------------------
/** Loads data whenever the deps change. The previous data stays on screen while the next request runs;
 *  `loading` is true until the result for the current deps has arrived. */
export function useAsync<T>(fn: () => Promise<T>, deps: unknown[]) {
  const [tick, setTick] = useState(0);
  const key = JSON.stringify([...deps, tick]);
  const [state, setState] = useState<{ key: string | null; data: T | null; error: unknown }>({ key: null, data: null, error: null });
  useEffect(() => {
    let alive = true;
    fn().then((d) => { if (alive) setState({ key, data: d, error: null }); }).catch((e) => { if (alive) setState((s) => ({ key, data: s.data, error: e })); });
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key]);
  const reload = useCallback(() => setTick((t) => t + 1), []);
  const setData = useCallback((d: T | null) => setState((s) => ({ ...s, data: d })), []);
  const loading = state.key !== key;
  return useMemo(() => ({ data: state.data, error: loading ? null : state.error, loading, reload, setData }), [state, loading, reload, setData]);
}

/** The ?id= in the address bar, so a notice or a dashboard row can open a record directly. */
export function useQueryId(): [number | null, (id: number | null) => void] {
  const params = useSearchParams();
  const router = useRouter();
  const pathname = usePathname();
  const v = params.get("id");
  const setId = useCallback((next: number | null) => {
    const p = new URLSearchParams(params.toString());
    if (next) p.set("id", String(next)); else p.delete("id");
    const s = p.toString();
    router.push(`${pathname}${s ? `?${s}` : ""}`, { scroll: false });
  }, [params, router, pathname]);
  return [v ? +v : null, setId];
}

// ---- building blocks -------------------------------------------------------
export function Card({ title, sub, actions, children, className, flush }: { title?: ReactNode; sub?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string; flush?: boolean }) {
  return (
    <div className={`card ${flush ? "flush" : ""} ${className ?? ""}`}>
      {(title || sub || actions) && <div className="card-head">
        <div>{title && <h3>{title}</h3>}{sub && <div className="sub">{sub}</div>}</div>
        {actions && <div className="actions">{actions}</div>}
      </div>}
      {children}
    </div>
  );
}
export function Kpi({ label, value, sub, tone, onClick }: { label: string; value: ReactNode; sub?: ReactNode; tone?: string; onClick?: () => void }) {
  return (
    <div className={`card kpi ${tone ?? ""} ${onClick ? "clickable" : ""}`} onClick={onClick} role={onClick ? "button" : undefined} tabIndex={onClick ? 0 : undefined}
      onKeyDown={onClick ? (e) => { if (e.key === "Enter") onClick(); } : undefined}>
      <div className="label">{label}</div>
      <div className="value">{value}</div>
      {sub && <div className="sub">{sub}</div>}
    </div>
  );
}
export const Chip = ({ tone, children, title }: { tone?: string; children: ReactNode; title?: string }) => <span className={`chip ${tone ?? ""}`} title={title}>{children}</span>;
export const Tag = ({ children }: { children: ReactNode }) => <span className="tag">{children}</span>;
export const Notice = ({ kind, children }: { kind?: "good" | "info" | "crit" | "watch" | ""; children: ReactNode }) => <div className={`notice ${kind ?? ""}`}>{children}</div>;
export const Loading = ({ what }: { what?: string }) => <div className="loading">Loading {what ?? ""}…</div>;
export const ErrorBox = ({ error }: { error: unknown }) => <div className="notice crit"><span>{errText(error)}</span></div>;
export const Empty = ({ children }: { children: ReactNode }) => <div className="table-empty">{children}</div>;
export function Bar({ value, max, tone, label }: { value: number; max: number; tone?: string; label?: string }) {
  const pctv = max <= 0 ? 0 : Math.min(100, (value / max) * 100);
  return <div className="bar-cell"><div className={`bar ${tone ?? ""}`}><i style={{ width: `${pctv}%` }} /></div><span className="num tiny">{label ?? int(value)}</span></div>;
}
export function TwoLine({ top, bottom }: { top: ReactNode; bottom: ReactNode }) {
  return <div className="two-line"><b>{top}</b><small>{bottom}</small></div>;
}
export function Issues({ issues }: { issues: RouteIssue[] }) {
  if (!issues.length) return null;
  return (
    <div className="issues">
      {issues.map((i, k) => <div key={k} className={`notice ${i.severity === "error" ? "crit" : "watch"}`}><span><b>{i.severity === "error" ? "Blocks submission" : "Warning"}:</b> {i.message} <span className="tiny mono">{i.code}</span></span></div>)}
    </div>
  );
}

/** Table with a sticky header and a friendly empty state. */
export function Table({ cols, children, empty, rows }: { cols: (string | { t: string; c?: string })[]; children: ReactNode; empty?: string; rows: number }) {
  return (
    <div className="table-wrap">
      <table>
        <thead><tr>{cols.map((c, i) => typeof c === "string" ? <th key={i}>{c}</th> : <th key={i} className={c.c}>{c.t}</th>)}</tr></thead>
        <tbody>{rows > 0 ? children : <tr><td colSpan={cols.length}><Empty>{empty ?? "Nothing to show yet."}</Empty></td></tr>}</tbody>
      </table>
    </div>
  );
}

export function Tabs<T extends string>({ tabs, value, onChange }: { tabs: { key: T; label: ReactNode }[]; value: T; onChange: (k: T) => void }) {
  return <>{tabs.map((t) => <button key={t.key} type="button" className={value === t.key ? "on" : ""} onClick={() => onChange(t.key)}>{t.label}</button>)}</>;
}

export function UserSelect({ users, value, onChange, placeholder, disabled, id }: { users: UserRefDto[]; value: string | null | undefined; onChange: (v: string | null) => void; placeholder?: string; disabled?: boolean; id?: string }) {
  return (
    <select id={id} value={value ?? ""} disabled={disabled} onChange={(e) => onChange(e.target.value || null)}>
      <option value="">{placeholder ?? "— choose an officer —"}</option>
      {users.map((u) => <option key={u.id} value={u.id}>{u.name}{u.designation ? ` — ${u.designation}` : ""}</option>)}
    </select>
  );
}

// ---- dialogs ---------------------------------------------------------------
function useBodyLock(open: boolean) {
  useEffect(() => {
    if (!open) return;
    const { overflow, paddingRight } = document.body.style;
    const bar = window.innerWidth - document.documentElement.clientWidth;
    document.body.style.overflow = "hidden";
    if (bar > 0) document.body.style.paddingRight = bar + "px";
    return () => { document.body.style.overflow = overflow; document.body.style.paddingRight = paddingRight; };
  }, [open]);
}

export function Modal({ open, onClose, title, sub, chips, tabs, children, footer, size }: {
  open: boolean; onClose: () => void; title: ReactNode; sub?: ReactNode; chips?: ReactNode; tabs?: ReactNode; children: ReactNode; footer?: ReactNode; size?: "narrow" | "wide";
}) {
  useBodyLock(open);
  useEffect(() => { if (!open) return; const h = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); }; document.addEventListener("keydown", h); return () => document.removeEventListener("keydown", h); }, [open, onClose]);
  if (!open) return null;
  return (
    <>
      <div className="back" onClick={onClose} />
      <div className="modal-wrap" role="dialog" aria-modal="true">
        <div className={`modal ${size ?? ""}`}>
          <div className="modal-head">
            <div>{sub && <div className="path">{sub}</div>}<h3>{title}</h3>{chips && <div className="actions" style={{ marginTop: 6 }}>{chips}</div>}</div>
            <button className="close" onClick={onClose} aria-label="Close">×</button>
          </div>
          {tabs && <div className="modal-tabs">{tabs}</div>}
          <div className="modal-body">{children}</div>
          {footer && <div className="modal-foot">{footer}</div>}
        </div>
      </div>
    </>
  );
}

type PromptProps = {
  open: boolean; title: string; label: string; placeholder?: string; required?: boolean; confirm: string; tone?: "danger" | "primary";
  onCancel: () => void; onConfirm: (text: string) => Promise<void> | void; children?: ReactNode;
};
/** A small dialog that asks for a reason (or other text) before an action. Each opening starts empty. */
export function Prompt(p: PromptProps) {
  return p.open ? <PromptBody {...p} /> : null;
}
function PromptBody({ title, label, placeholder, required, confirm, tone, onCancel, onConfirm, children }: PromptProps) {
  const [text, setText] = useState(""); const [busy, setBusy] = useState(false);
  return (
    <Modal open onClose={onCancel} title={title} size="narrow"
      footer={<><button className={`btn ${tone ?? "primary"}`} disabled={busy || (required && !text.trim())} onClick={async () => { setBusy(true); try { await onConfirm(text.trim()); } finally { setBusy(false); } }}>{confirm}</button>
        <span className="spacer" /><button className="btn" onClick={onCancel}>Cancel</button></>}>
      {children}
      <div className="field"><label>{label}{required ? " (required)" : ""}</label><textarea autoFocus rows={4} value={text} placeholder={placeholder} onChange={(e) => setText(e.target.value)} /></div>
    </Modal>
  );
}

// ---- inline SVG charts (no charting dependency) ----------------------------
/** Horizontal bars, one row per category. Values are labelled directly, so no legend is needed. */
export function HBars({ data, onPick }: { data: { key: string; label: string; value: number; tone?: string }[]; onPick?: (key: string) => void }) {
  const max = Math.max(1, ...data.map((d) => d.value));
  return (
    <div className="hbars">
      {data.map((d) => (
        <button key={d.key} type="button" className="hbar" onClick={onPick ? () => onPick(d.key) : undefined} disabled={!onPick} aria-label={`${d.label}: ${d.value}`}>
          <span className="hb-label">{d.label}</span>
          <span className="hb-track"><i className={d.tone ?? ""} style={{ width: `${(d.value / max) * 100}%` }} /></span>
          <span className="hb-value num">{int(d.value)}</span>
        </button>
      ))}
    </div>
  );
}

export function Donut({ segments, size = 132, unit = "items" }: { segments: { label: string; value: number; color: string }[]; size?: number; unit?: string }) {
  const total = segments.reduce((s, x) => s + x.value, 0);
  const r = size / 2 - 13, c = 2 * Math.PI * r;
  let offset = 0;
  return (
    <svg className="chart" viewBox={`0 0 ${size} ${size}`} style={{ width: size, height: size, flex: "none" }} role="img" aria-label={segments.map((s) => `${s.label} ${s.value}`).join(", ")}>
      <g transform={`translate(${size / 2},${size / 2}) rotate(-90)`}>
        <circle r={r} fill="none" stroke="var(--track)" strokeWidth="13" />
        {total > 0 && segments.map((s, i) => {
          const len = (s.value / total) * c;
          const el = <circle key={i} r={r} fill="none" stroke={s.color} strokeWidth="13" strokeDasharray={`${Math.max(0, len - 1.5)} ${c - len + 1.5}`} strokeDashoffset={-offset} />;
          offset += len; return el;
        })}
      </g>
      <text x={size / 2} y={size / 2 - 2} textAnchor="middle" className="ch-value" style={{ fontSize: 19 }}>{int(total)}</text>
      <text x={size / 2} y={size / 2 + 13} textAnchor="middle" className="ch-tick">{unit}</text>
    </svg>
  );
}
