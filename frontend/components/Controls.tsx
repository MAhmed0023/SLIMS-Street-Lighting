"use client";
import { useEffect, useRef, useState } from "react";
import { api, get, post } from "@/lib/api";
import type { AmendableFieldDto, AmendmentDto, EvidenceDto } from "@/lib/types";
import { coords, dateTime } from "@/lib/format";
import { Chip, Modal, Notice, errText, useAsync, useToast } from "./ui";

/** One input for a hardware scanner (types the payload and presses Enter). */
export function ScanBox({ onScan, placeholder, disabled, busy }: { onScan: (qr: string) => void | Promise<void>; placeholder?: string; disabled?: boolean; busy?: boolean }) {
  const [v, setV] = useState("");
  const ref = useRef<HTMLInputElement>(null);
  useEffect(() => { if (!disabled && !busy) ref.current?.focus(); }, [disabled, busy]);
  return (
    <form className="scan-input" onSubmit={async (e) => { e.preventDefault(); const q = v.trim(); if (!q) return; setV(""); await onScan(q); ref.current?.focus(); }}>
      <input ref={ref} value={v} onChange={(e) => setV(e.target.value)} disabled={disabled || busy} placeholder={placeholder ?? "Scan the item or bin label, then Enter"} autoComplete="off" spellCheck={false} aria-label="Scan" />
      <button type="submit" className="btn primary" disabled={disabled || busy || !v.trim()}>{busy ? "…" : "Scan"}</button>
    </form>
  );
}

/** Evidence files need the bearer token, which an <img> tag cannot send: fetch them and show a local object URL. */
export function AuthImg({ src, alt, onOpen, style, className }: { src: string; alt: string; onOpen?: (objectUrl: string) => void; style?: React.CSSProperties; className?: string }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let alive = true; let made: string | null = null;
    api<Response>("GET", src, undefined, { raw: true })
      .then(async (r) => { if (!r.ok) throw new Error(String(r.status)); made = URL.createObjectURL(await r.blob()); if (alive) setUrl(made); })
      .catch(() => { if (alive) setFailed(true); });
    return () => { alive = false; if (made) URL.revokeObjectURL(made); };
  }, [src]);
  if (!url) return <div className={className} style={{ ...style, display: "grid", placeItems: "center", background: "var(--surface-2)", color: "var(--ink-3)", fontSize: 12, minHeight: 80 }}>{failed ? "Photograph not available" : "Loading…"}</div>;
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={url} alt={alt} style={{ ...style, cursor: onOpen ? "zoom-in" : undefined }} className={className} onClick={() => onOpen?.(url)} />;
}

/** Opens an authenticated file (e.g. the original photograph) in a new tab. */
export async function openAuthed(src: string) {
  const r = await api<Response>("GET", src, undefined, { raw: true });
  if (r.ok) window.open(URL.createObjectURL(await r.blob()), "_blank");
}

/** A photograph from the evidence store: the server-stamped copy, with the original and its hash one click away (FR-FLD-009). */
export function EvidencePhoto({ e, title, onZoom }: { e: EvidenceDto; title?: string; onZoom?: (url: string) => void }) {
  const src = e.stampedUrl ?? e.url;
  return (
    <div className="ev-card">
      <h5>{title ?? `${e.kind} installation`}</h5>
      <AuthImg src={src} alt={`${e.kind} evidence`} onOpen={onZoom} />
      <div className="ev-meta">
        <span className="mono">{coords(e.lat, e.lng)}</span>
        <span>±{Math.round(e.accuracyM ?? 0)} m · captured {dateTime(e.capturedAt)}</span>
        <span>synchronised {dateTime(e.uploadedAt)} · {e.uploadedBy}</span>
        {e.sha256 && <span className="mono tiny" title={e.sha256}>SHA-256 {e.sha256.slice(0, 16)}…</span>}
        <span className="tiny">{e.stampedUrl ? <>Stamped copy shown · <button className="linky" style={{ background: "none", border: 0, padding: 0, cursor: "pointer" }} onClick={() => openAuthed(e.url)}>open original</button></> : "Original"}{e.deviceId ? " · from the bound phone" : ""}</span>
      </div>
    </div>
  );
}

/** FR-EXC-011: ask for a correction to a completed record. The change takes effect only after someone else approves it. */
export function AmendButton({ entityType, entityRef, current, onDone }: { entityType: string; entityRef: string; current?: Record<string, string | null | undefined>; onDone?: () => void }) {
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const fields = useAsync(() => open ? get<AmendableFieldDto[]>("/api/amendments/fields") : Promise.resolve([] as AmendableFieldDto[]), [open]);
  const history = useAsync(() => open ? get<AmendmentDto[]>(`/api/amendments?entityType=${entityType}&entityRef=${encodeURIComponent(entityRef)}`) : Promise.resolve([] as AmendmentDto[]), [open, entityRef]);
  const mine = (fields.data ?? []).filter((f) => f.entityType === entityType);
  const [field, setField] = useState(""); const [value, setValue] = useState(""); const [reason, setReason] = useState(""); const [busy, setBusy] = useState(false);
  const f = field || mine[0]?.field || "";
  return (<>
    <button className="btn" onClick={() => setOpen(true)}>Request amendment</button>
    <Modal open={open} onClose={() => setOpen(false)} size="narrow" title="Request an amendment" sub={`${entityType} ${entityRef}`}
      footer={<><button className="btn primary" disabled={busy || !f || reason.trim().length < 10} onClick={async () => {
        setBusy(true);
        try { const a = await post<AmendmentDto>("/api/amendments", { entityType, entityRef, field: f, newValue: value, reason }); toast(`${a.number} sent for approval`); setOpen(false); setValue(""); setReason(""); onDone?.(); }
        catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Send for approval</button><button className="btn" onClick={() => setOpen(false)}>Cancel</button></>}>
      <Notice kind="info">The record stays as it is until another officer approves the change. The original value and the correction both stay on record (FR-EXC-011). Photographs, coordinates and quantities cannot be amended.</Notice>
      <div className="form">
        <div className="field"><label>Field</label>
          <select value={f} onChange={(e) => setField(e.target.value)}>{mine.map((x) => <option key={x.field} value={x.field}>{x.label}</option>)}</select></div>
        {current && f && <div className="tiny">Current value: <b>{current[f] ?? "—"}</b></div>}
        <div className="field"><label>Correct value</label><input value={value} onChange={(e) => setValue(e.target.value)} /></div>
        <div className="field"><label>Reason</label><textarea value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Pole number misread at site; the plate reads P-017" /></div>
      </div>
      {(history.data ?? []).length > 0 && <div style={{ marginTop: 10 }}>
        <h4 className="tiny" style={{ textTransform: "uppercase", letterSpacing: ".08em" }}>Amendment history</h4>
        {history.data!.map((a) => <div key={a.id} className="tiny" style={{ padding: "4px 0", borderTop: "1px solid var(--line)" }}>
          <b className="mono">{a.number}</b> <Chip tone={a.status === "Approved" ? "ok" : a.status === "Rejected" ? "muted" : "watch"}>{a.status}</Chip> {a.field}: “{a.oldValue ?? "—"}” → “{a.newValue ?? "—"}” · {a.raisedBy} · {dateTime(a.raisedAt)}
        </div>)}
      </div>}
    </Modal>
  </>);
}
