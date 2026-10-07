"use client";
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from "react";
import { int, statusLabel, statusTone } from "@/lib/format";

// ---- toast -----------------------------------------------------------------
type Toast = (msg: string, bad?: boolean) => void;
const ToastCtx = createContext<Toast>(() => {});
export function ToastProvider({ children }: { children: ReactNode }) {
  const [msg, setMsg] = useState<{ text: string; bad?: boolean } | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const toast = useCallback<Toast>((text, bad) => {
    setMsg({ text, bad });
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setMsg(null), bad ? 6000 : 3500);
  }, []);
  return <ToastCtx.Provider value={toast}>{children}{msg && <div id="toast" className={msg.bad ? "bad" : ""}>{msg.text}</div>}</ToastCtx.Provider>;
}
export const useToast = () => useContext(ToastCtx);
export const errText = (e: unknown) => e instanceof Error ? e.message : String(e);

// ---- data hook -------------------------------------------------------------
export function useAsync<T>(fn: () => Promise<T>, deps: unknown[]) {
  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);
  const [tick, setTick] = useState(0);
  useEffect(() => {
    let alive = true; setLoading(true); setError(null);
    fn().then((d) => { if (alive) { setData(d); setLoading(false); } }).catch((e) => { if (alive) { setError(e); setLoading(false); } });
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick]);
  const reload = useCallback(() => setTick((t) => t + 1), []);
  return useMemo(() => ({ data, error, loading, reload, setData }), [data, error, loading, reload]);
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
    <div className={`card kpi ${tone ?? ""} ${onClick ? "clickable" : ""}`} onClick={onClick} role={onClick ? "button" : undefined} tabIndex={onClick ? 0 : undefined}>
      <div className="label">{label}</div>
      <div className="value">{value}</div>
      {sub && <div className="sub">{sub}</div>}
    </div>
  );
}
export const Chip = ({ tone, children }: { tone?: string; children: ReactNode }) => <span className={`chip ${tone ?? ""}`}>{children}</span>;
export const StatusChip = ({ s }: { s: string }) => <span className={`chip ${statusTone(s)}`}>{statusLabel(s)}</span>;
export const Tag = ({ children }: { children: ReactNode }) => <span className="tag">{children}</span>;
export const Notice = ({ kind, children }: { kind?: "good" | "info" | "crit" | "watch" | ""; children: ReactNode }) => <div className={`notice ${kind ?? ""}`}>{children}</div>;
export const Loading = ({ what }: { what?: string }) => <div className="loading">Loading {what ?? ""}…</div>;
export const ErrorBox = ({ error }: { error: unknown }) => <div className="notice crit"><span>{errText(error)}</span></div>;
export const Empty = ({ children }: { children: ReactNode }) => <div className="table-empty">{children}</div>;
export function Bar({ value, max, tone }: { value: number; max: number; tone?: string }) {
  const pctv = max <= 0 ? 0 : Math.min(100, (value / max) * 100);
  return <div className="bar-cell"><div className={`bar ${tone ?? ""}`}><i style={{ width: `${pctv}%` }} /></div><span className="num tiny">{int(value)}</span></div>;
}
export function TwoLine({ top, bottom }: { top: ReactNode; bottom: ReactNode }) {
  return <div className="two-line"><b>{top}</b><small>{bottom}</small></div>;
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

export function Lightbox({ src, onClose }: { src: string | null; onClose: () => void }) {
  useBodyLock(!!src);
  useEffect(() => { if (!src) return; const h = (e: KeyboardEvent) => { if (e.key === "Escape") onClose(); }; document.addEventListener("keydown", h); return () => document.removeEventListener("keydown", h); }, [src, onClose]);
  if (!src) return null;
  // eslint-disable-next-line @next/next/no-img-element
  return <div className="lightbox" onClick={onClose}><img src={src} alt="Evidence" /></div>;
}

// ---- inline SVG charts (no charting dependency) ----------------------------
export function BarChart({ data, height = 190, series = ["Installed", "Returned"] }: { data: { label: string; value: number; value2?: number | null }[]; height?: number; series?: [string, string] | string[] }) {
  const w = 720, pad = { l: 34, r: 8, t: 10, b: 24 };
  const max = Math.max(1, ...data.map((d) => Math.max(d.value, d.value2 ?? 0)));
  const iw = w - pad.l - pad.r, ih = height - pad.t - pad.b;
  const bw = iw / Math.max(1, data.length);
  const ticks = [0, 0.5, 1].map((f) => Math.round(max * f));
  return (
    <>
      <svg className="chart" viewBox={`0 0 ${w} ${height}`} preserveAspectRatio="xMidYMid meet" role="img">
        {ticks.map((t, i) => { const y = pad.t + ih - (t / max) * ih; return <g key={i}><line className="ch-grid" x1={pad.l} x2={w - pad.r} y1={y} y2={y} /><text className="ch-tick" x={pad.l - 6} y={y + 3} textAnchor="end">{t}</text></g>; })}
        {data.map((d, i) => {
          const x = pad.l + i * bw, h1 = (d.value / max) * ih, h2 = ((d.value2 ?? 0) / max) * ih;
          const bwid = Math.max(3, bw * 0.3);
          return (
            <g key={i}>
              <rect className="ch-s1" x={x + bw / 2 - bwid - 1} y={pad.t + ih - h1} width={bwid} height={Math.max(0, h1)} rx="2" />
              {d.value2 != null && <rect className="ch-crit" x={x + bw / 2 + 1} y={pad.t + ih - h2} width={bwid} height={Math.max(0, h2)} rx="2" />}
              <text className="ch-tick" x={x + bw / 2} y={height - 7} textAnchor="middle">{d.label}</text>
            </g>
          );
        })}
        <line className="ch-axis" x1={pad.l} x2={w - pad.r} y1={pad.t + ih} y2={pad.t + ih} />
      </svg>
      <div className="legend"><span><i style={{ background: "var(--series-1)" }} />{series[0]}</span><span><i style={{ background: "var(--crit)" }} />{series[1]}</span></div>
    </>
  );
}

export function StackRow({ parts, total }: { parts: { v: number; cls: string }[]; total: number }) {
  return <div className="stack">{parts.filter((p) => p.v > 0).map((p, i) => <i key={i} className={p.cls} style={{ width: `${total > 0 ? (p.v / total) * 100 : 0}%`, background: `var(--${p.cls})` }} />)}</div>;
}

export function Donut({ segments, size = 132 }: { segments: { label: string; value: number; color: string }[]; size?: number }) {
  const total = segments.reduce((s, x) => s + x.value, 0) || 1;
  const r = size / 2 - 13, c = 2 * Math.PI * r;
  let offset = 0;
  return (
    <svg className="chart" viewBox={`0 0 ${size} ${size}`} style={{ width: size, height: size, flex: "none" }} role="img">
      <g transform={`translate(${size / 2},${size / 2}) rotate(-90)`}>
        <circle r={r} fill="none" stroke="var(--track)" strokeWidth="13" />
        {segments.map((s, i) => {
          const len = (s.value / total) * c;
          const el = <circle key={i} r={r} fill="none" stroke={s.color} strokeWidth="13" strokeDasharray={`${len} ${c - len}`} strokeDashoffset={-offset} />;
          offset += len; return el;
        })}
      </g>
      <text x={size / 2} y={size / 2 - 2} textAnchor="middle" className="ch-value" style={{ fontSize: 19 }}>{int(total)}</text>
      <text x={size / 2} y={size / 2 + 13} textAnchor="middle" className="ch-tick">items</text>
    </svg>
  );
}
