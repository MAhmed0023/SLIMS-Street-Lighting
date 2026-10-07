"use client";
import { useMemo, useState } from "react";
import { get, post } from "@/lib/api";
import type { IssueAvailableDto, IssueDto, RequisitionDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { byItemCode } from "@/lib/items";
import { ago, dateTime, money, qty } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

export default function IssuePage() {
  const { config, can } = useAuth();
  const toast = useToast();
  const [tab, setTab] = useState<"issue" | "register">("issue");
  const [reqId, setReqId] = useState<number | null>(null);
  const [qtys, setQtys] = useState<Record<number, string>>({});
  const [issuedTo, setIssuedTo] = useState(""); const [vehicle, setVehicle] = useState(""); const [remarks, setRemarks] = useState("");
  const [busy, setBusy] = useState(false);
  const [detail, setDetail] = useState<IssueDto | null>(null);

  const ready = useAsync(() => get<RequisitionDto[]>("/api/requisitions?status=Approved"), []);
  const partial = useAsync(() => get<RequisitionDto[]>("/api/requisitions?status=PartIssued"), []);
  const issues = useAsync(() => get<IssueDto[]>("/api/issues"), [tab === "register"]);
  const req = useAsync(() => reqId ? get<RequisitionDto>(`/api/requisitions/${reqId}`) : Promise.resolve(null), [reqId]);
  const avail = useAsync(() => reqId ? get<IssueAvailableDto[]>(`/api/issues/available?requisitionId=${reqId}`) : Promise.resolve([] as IssueAvailableDto[]), [reqId]);
  const stockOf = (articleId: number) => { const a = avail.data?.find((x) => x.articleId === articleId); return a ? a.reserved + a.inStock : 0; };
  // each line starts at what can go now: the balance, limited by the stock in the bins
  const val = (l: { articleId: number; qtyApproved: number; qtyIssued: number }) =>
    qtys[l.articleId] ?? (avail.data ? String(Math.max(0, Math.min(l.qtyApproved - l.qtyIssued, stockOf(l.articleId)))) : "");

  const candidates = useMemo(() => [...(ready.data ?? []), ...(partial.data ?? [])], [ready.data, partial.data]);
  const n = (v: string | undefined) => { const x = Number(v); return Number.isFinite(x) && x > 0 ? x : 0; };
  const entered = (req.data?.lines ?? []).filter((l) => n(val(l)) > 0);
  const total = entered.reduce((s, l) => s + n(val(l)), 0);
  const cur = config?.app.currency ?? "Rs.";

  const postIssue = async () => {
    if (!reqId || entered.length === 0) return;
    setBusy(true);
    try {
      const i = await post<IssueDto>("/api/issues", { requisitionId: reqId, qrCodes: [], lines: entered.map((l) => ({ articleId: l.articleId, qty: n(val(l)) })), issuedTo: issuedTo || null, vehicle: vehicle || null, remarks: remarks || null });
      toast(`${i.number} posted — gate pass ${i.gatePassNo}, custody moved to ${i.siteCode}`);
      setQtys({}); setIssuedTo(""); setVehicle(""); setRemarks(""); setDetail(i);
      ready.reload(); partial.reload(); issues.reload(); req.reload(); avail.reload();
    } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };

  return (
    <>
      <div className="seg">
        <button className={tab === "issue" ? "active" : ""} onClick={() => setTab("issue")}>Issue against requisition</button>
        <button className={tab === "register" ? "active" : ""} onClick={() => setTab("register")}>Issue register &amp; gate passes</button>
      </div>

      {tab === "issue" && (!can("issue") ? <Notice kind="crit">Your role cannot issue material.</Notice> : (
        <div className="split wide">
          <Card title="1 · Approved requisition" sub="There is no direct issue — material only leaves against an approved requisition (BR-006)">
            {ready.loading ? <Loading /> : (
              <div className="form">
                <div className="field">
                  <label>Requisition</label>
                  <select value={reqId ?? ""} onChange={(e) => { setReqId(e.target.value ? +e.target.value : null); setQtys({}); }}>
                    <option value="">— choose an approved requisition —</option>
                    {candidates.map((r) => <option key={r.id} value={r.id}>{r.number} — {r.siteCode} ({r.status})</option>)}
                  </select>
                  {candidates.length === 0 && <span className="hint">Nothing approved and waiting to be issued.</span>}
                </div>
                {req.data && <>
                  <div className="kv">
                    <div><span>Site</span><b>{req.data.siteCode}</b></div>
                    <div><span>Store</span><b>{req.data.storeCode}</b></div>
                    <div><span>Issue slip</span><b className="mono">{req.data.issueSlipNo ?? "—"}</b></div>
                    <div><span>Raised by</span><b>{req.data.raisedBy}</b></div>
                  </div>
                  <div className="frow">
                    <div className="field"><label>Handed over to</label><input value={issuedTo} onChange={(e) => setIssuedTo(e.target.value)} placeholder="Name of the person collecting" /></div>
                    <div className="field"><label>Vehicle</label><input value={vehicle} onChange={(e) => setVehicle(e.target.value)} placeholder="LEA-1234" /></div>
                  </div>
                  <div className="field"><label>Remarks</label><input value={remarks} onChange={(e) => setRemarks(e.target.value)} /></div>
                </>}
              </div>
            )}
          </Card>

          <div className="grid">
            <Card title="2 · Quantities to issue" sub="Enter the quantity on each line — no scanning. The store's stock is picked automatically (reserved first, then oldest receipt), custody moves to the site and the gate pass is numbered.">
              {req.data && <div style={{ marginBottom: 12 }}>
              <Table rows={req.data.lines.length} cols={["Item", { t: "Balance", c: "r" }, { t: "In stock", c: "r" }, { t: "Issue now", c: "r" }]}>
                {req.data.lines.map((l) => {
                  const bal = l.qtyApproved - l.qtyIssued; const a = avail.data?.find((x) => x.articleId === l.articleId);
                  return (
                    <tr key={l.id}>
                      <td><div className="two-line"><b className="mono">{l.itemCode}</b><small>{l.description} · approved {qty(l.qtyApproved)}, issued {qty(l.qtyIssued)}{a && a.packSize > 1 ? ` · goes in whole packs of ${qty(a.packSize)} ${a.uom}` : ""}</small></div></td>
                      <td className="r num"><b className={bal > 0 ? "watch" : ""}>{qty(bal)}</b></td>
                      <td className="r num">{a ? qty(a.reserved + a.inStock) : "…"}{a && a.inReceiving > 0 && <div className="tiny muted">+{qty(a.inReceiving)} in Receiving</div>}</td>
                      <td className="r">{bal > 0 ? <input type="number" min="0" max={bal} step="any" style={{ width: 90, textAlign: "right" }} value={val(l)} placeholder="0" onChange={(e) => setQtys((q) => ({ ...q, [l.articleId]: e.target.value }))} /> : <span className="tiny muted">complete</span>}</td>
                    </tr>
                  );
                })}
              </Table>
              </div>}
              <div className="actions">
                <button className="btn primary" disabled={busy || entered.length === 0} onClick={postIssue}>
                  {busy ? "Posting…" : `Post issue & print gate pass — ${qty(total)} unit${total === 1 ? "" : "s"}`}
                </button>
              </div>
              {!reqId && <Notice kind="info">Choose an approved requisition.</Notice>}
              {total > 0 && req.data && <Notice kind="info">{qty(total)} units on {entered.length} line(s) → <b>{req.data.siteCode}</b> · {req.data.siteName}</Notice>}
            </Card>
          </div>
        </div>
      ))}

      {tab === "register" && (
        <Card flush title="Issue register" sub="Every issue note and gate pass, newest first" actions={<button className="btn small" onClick={issues.reload}>Refresh</button>}>
          {issues.loading ? <Loading /> : issues.error ? <ErrorBox error={issues.error} /> : (
            <Table rows={issues.data!.length} empty="Nothing issued yet." cols={["Issue", "Gate pass", "Issued", "Store", "Site", "Requisition", { t: "Qty", c: "r" }, "Acknowledged", ""]}>
              {issues.data!.map((i) => (
                <tr key={i.id} className="row-link" onClick={() => get<IssueDto>(`/api/issues/${i.id}`).then(setDetail)}>
                  <td className="mono">{i.number}</td>
                  <td className="mono tiny">{i.gatePassNo}</td>
                  <td className="tiny">{dateTime(i.issuedAt)}<div className="tiny">{i.issuedBy}</div></td>
                  <td>{i.storeCode}</td>
                  <td><div className="two-line"><b>{i.siteCode}</b><small>{i.siteName}</small></div></td>
                  <td className="mono tiny">{i.requisitionNo}</td>
                  <td className="r num">{qty(i.qty)}</td>
                  <td>{i.receivedAt ? <Chip tone="ok">{ago(i.receivedAt)}</Chip> : <Chip tone="watch">awaiting site</Chip>}</td>
                  <td><span className="linky">Open</span></td>
                </tr>
              ))}
            </Table>
          )}
        </Card>
      )}

      <Modal open={!!detail} onClose={() => setDetail(null)} title={detail?.number ?? ""} sub={`Gate pass ${detail?.gatePassNo} · ${detail?.siteCode} ${detail?.siteName}`}
        footer={<>
          {detail && !detail.receivedAt && can("install") && <button className="btn primary" onClick={async () => {
            try {
              const pos = await new Promise<GeolocationPosition | null>((res) => navigator.geolocation ? navigator.geolocation.getCurrentPosition((p) => res(p), () => res(null), { enableHighAccuracy: true, timeout: 8000 }) : res(null));
              const u = await post<IssueDto>(`/api/issues/${detail.id}/acknowledge`, { lat: pos?.coords.latitude ?? null, lng: pos?.coords.longitude ?? null, accuracyM: pos?.coords.accuracy ?? null });
              setDetail(u); toast("Receipt acknowledged at site"); issues.reload();
            } catch (e) { toast(errText(e), true); }
          }}>Acknowledge receipt at site</button>}
          <button className="btn" onClick={() => window.print()}>Print gate pass</button>
          <span className="spacer" /><button className="btn" onClick={() => setDetail(null)}>Close</button>
        </>}>
        <div className="kv">
          <div><span>Issued by</span><b>{detail?.issuedBy}</b></div>
          <div><span>Issued at</span><b>{dateTime(detail?.issuedAt)}</b></div>
          <div><span>Handed to</span><b>{detail?.issuedTo ?? "—"}</b></div>
          <div><span>Vehicle</span><b>{detail?.vehicle ?? "—"}</b></div>
          <div><span>Quantity</span><b className="num">{qty(detail?.qty)}</b></div>
          <div><span>Value</span><b className="num">{cur} {money(detail?.value)}</b></div>
          <div><span>Acknowledged</span><b>{detail?.receivedAt ? dateTime(detail.receivedAt) : "not yet"}</b></div>
        </div>
        <Table rows={byItemCode(detail?.items ?? []).length} cols={["Item code", "Description", { t: "Qty", c: "r" }, { t: "Value", c: "r" }, "Status"]}>
          {byItemCode(detail?.items ?? []).map((l) => (
            <tr key={l.itemCode}><td className="mono">{l.itemCode}</td><td className="tiny">{l.description}</td><td className="r num">{qty(l.qty)} {l.uom}</td><td className="r num">{cur} {money(l.value)}</td><td>{l.status}</td></tr>
          ))}
        </Table>
      </Modal>
    </>
  );
}
