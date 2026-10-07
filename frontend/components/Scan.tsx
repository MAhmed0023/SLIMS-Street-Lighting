"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { post } from "@/lib/api";
import type { ItemDto, ScanResultDto } from "@/lib/types";
import { errText, useToast } from "./ui";

export interface ScannedLine { qr: string; item?: ItemDto | null; message: string; ok: boolean; step?: string | null }

/**
 * The scan panel used by goods receipt, issuance and returns.
 *
 * A hardware QR scanner types the payload and presses Enter, so the input stays focused and the
 * form submits on Enter. Every scan is validated by the API before it joins the list — the same
 * check the posting endpoint repeats, so nothing can be smuggled in by editing the page.
 */
export function ScanPanel({ endpoint, body, lines, setLines, placeholder, disabled, hint }: {
  endpoint: string;
  body: (qr: string, already: string[]) => unknown;
  lines: ScannedLine[];
  setLines: (f: (prev: ScannedLine[]) => ScannedLine[]) => void;
  placeholder?: string;
  disabled?: boolean;
  hint?: string;
}) {
  const [value, setValue] = useState("");
  const [busy, setBusy] = useState(false);
  const [last, setLast] = useState<ScannedLine | null>(null);
  const ref = useRef<HTMLInputElement>(null);
  const toast = useToast();
  useEffect(() => { if (!disabled) ref.current?.focus(); }, [disabled, lines.length]);

  const submit = useCallback(async (raw: string) => {
    const qr = raw.trim();
    if (!qr) return;
    setValue("");
    if (lines.some((l) => l.ok && l.qr === qr)) { const dup = { qr, message: "Already scanned in this batch.", ok: false, step: "BR-002" }; setLast(dup); setLines((p) => [dup, ...p.filter((x) => x.ok || x.qr !== qr)]); return; }
    setBusy(true);
    try {
      const r = await post<ScanResultDto>(endpoint, body(qr, lines.filter((l) => l.ok).map((l) => l.qr)));
      const line: ScannedLine = { qr, item: r.item, message: r.message, ok: r.ok, step: r.step };
      setLast(line);
      setLines((p) => [line, ...p.filter((x) => x.qr !== qr)]);
      if (!r.ok) toast(r.message, true);
    } catch (e) {
      const line: ScannedLine = { qr, message: errText(e), ok: false };
      setLast(line); setLines((p) => [line, ...p.filter((x) => x.qr !== qr)]); toast(errText(e), true);
    } finally { setBusy(false); ref.current?.focus(); }
  }, [endpoint, body, lines, setLines, toast]);

  const good = lines.filter((l) => l.ok);
  const qty = good.reduce((s, l) => s + (l.item?.qty ?? 0), 0);
  return (
    <div className="scanner">
      <form className="scan-input" onSubmit={(e) => { e.preventDefault(); submit(value); }}>
        <input ref={ref} value={value} onChange={(e) => setValue(e.target.value)} disabled={disabled || busy}
          placeholder={placeholder ?? "Scan or type the QR payload, then press Enter"} autoComplete="off" spellCheck={false} aria-label="QR payload" />
        <button type="submit" className="btn primary" disabled={disabled || busy || !value.trim()}>{busy ? "Checking…" : "Scan"}</button>
      </form>
      <div className="scan-hint">
        <span>{hint ?? "A hardware scanner types the code and presses Enter by itself. Every scan is validated on the server."}</span>
      </div>
      {last && <div className={`notice ${last.ok ? "good" : "crit"}`}>
        <span><b>{last.ok ? "Accepted" : "Rejected"}</b> — {last.message}{last.step ? <> <span className="rulebox">{last.step}</span></> : null}</span>
      </div>}
      {lines.length > 0 && <>
        <div className="scan-total">
          <span>Accepted <b>{good.length}</b> label{good.length === 1 ? "" : "s"}</span>
          <span>Quantity <b>{qty.toLocaleString("en-PK", { maximumFractionDigits: 3 })}</b></span>
          {lines.length - good.length > 0 && <span className="err">{lines.length - good.length} rejected</span>}
          <span className="spacer" />
          <button type="button" className="btn small" onClick={() => setLines(() => [])}>Clear all</button>
        </div>
        <div className="scan-list">
          {lines.map((l, i) => (
            <div key={l.qr + i} className={`scan-row ${l.ok ? "" : "bad"}`}>
              <span className="n">{lines.length - i}</span>
              <span>
                {l.item ? <><b className="mono">{l.item.itemCode}</b> <span className="tiny">#{l.item.serial} · {l.item.description}</span></> : <span className="mono">{l.qr}</span>}
                {!l.ok && <div className="tiny err">{l.message}</div>}
              </span>
              <span className="mono tiny">{l.item ? `${l.item.qty} ${l.item.uom}` : ""}</span>
              <button type="button" className="x" onClick={() => setLines((p) => p.filter((x) => x.qr !== l.qr))} aria-label="Remove">×</button>
            </div>
          ))}
        </div>
      </>}
    </div>
  );
}
