"use client";
import { useState } from "react";
import Link from "next/link";
import { get } from "@/lib/api";
import type { VerificationDto } from "@/lib/types";
import { dateTime } from "@/lib/format";
import { VerifyTable } from "@/components/DocParts";
import { Card, Notice, errText } from "@/components/ui";

export default function VerifyPage() {
  const [code, setCode] = useState(""); const [v, setV] = useState<VerificationDto | null>(null); const [err, setErr] = useState(""); const [busy, setBusy] = useState(false);
  const check = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setErr(""); setV(null);
    try { setV(await get<VerificationDto>(`/api/documents/verify/${encodeURIComponent(code.trim())}`)); } catch (x) { setErr(errText(x)); } finally { setBusy(false); }
  };
  return (
    <div className="grid">
      <Card title="Check a signature" sub="Enter the code printed on the signature stamp, e.g. 7KQ4-M2XD">
        <form onSubmit={check} className="actions">
          <input value={code} onChange={(e) => setCode(e.target.value.toUpperCase())} placeholder="XXXX-XXXX" className="mono" style={{ width: 160 }} />
          <button className="btn primary" disabled={busy || code.trim().length < 9}>Verify</button>
        </form>
        <div className="tiny" style={{ marginTop: 8 }}>The check recomputes the SHA-256 of the signed version from the file store, re-checks the server seal and confirms the stamped copy is intact. You can only check documents you are allowed to see.</div>
      </Card>
      {err && <Notice kind="crit"><span>{err}</span></Notice>}
      {v && <Card title={<>{v.number} — <span className={v.overall.startsWith("All") ? "verdict-ok" : "verdict-bad"}>{v.overall}</span></>} sub={`Checked ${dateTime(v.checkedAt)}`} actions={<Link className="btn small" href={`/documents?id=${v.documentId}`}>Open document</Link>}>
        <VerifyTable v={v} />
        <div className="tiny" style={{ marginTop: 8 }}>{v.mechanism}</div>
      </Card>}
    </div>
  );
}
