"use client";
import { useMemo, useState } from "react";
import { get, post } from "@/lib/api";
import type { ArticleDto, RequisitionDto, SiteDto, StoreDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { ago, date, dateTime, money, qty, reqTone } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

const STATUSES = ["", "Draft", "Submitted", "Approved", "PartIssued", "Issued", "Rejected", "Cancelled", "Lapsed"];

export default function RequisitionsPage() {
  const { config, can, user } = useAuth();
  const toast = useToast();
  const [status, setStatus] = useState("");
  const [open, setOpen] = useState<RequisitionDto | null>(null);
  const [creating, setCreating] = useState(false);
  const list = useAsync(() => get<RequisitionDto[]>(`/api/requisitions${status ? `?status=${status}` : ""}`), [status]);
  const cur = config?.app.currency ?? "Rs.";

  const reload = () => { list.reload(); if (open) get<RequisitionDto>(`/api/requisitions/${open.id}`).then(setOpen).catch(() => setOpen(null)); };

  return (
    <>
      <div className="filters">
        <div className="seg">{STATUSES.map((s) => <button key={s || "all"} className={status === s ? "active" : ""} onClick={() => setStatus(s)}>{s || "All"}</button>)}</div>
        <span className="spacer" />
        {can("requisition") && <button className="btn primary" onClick={() => setCreating(true)}>New requisition</button>}
      </div>

      <Card flush title="Requisitions" sub="A requisition must name a site from the site master (BR-007); approval reserves the stock (BR-009)">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No requisitions in this view." cols={["Requisition", "Raised", "Site", "Store", { t: "Lines", c: "r" }, { t: "Req / App / Iss", c: "r" }, { t: "Value", c: "r" }, "Status", ""]}>
            {list.data!.map((r) => {
              const req = r.lines.reduce((s, l) => s + l.qtyRequested, 0), app = r.lines.reduce((s, l) => s + l.qtyApproved, 0), iss = r.lines.reduce((s, l) => s + l.qtyIssued, 0);
              return (
                <tr key={r.id} className="row-link" onClick={() => get<RequisitionDto>(`/api/requisitions/${r.id}`).then(setOpen)}>
                  <td><div className="two-line"><b className="mono">{r.number}</b><small>{r.raisedBy}</small></div></td>
                  <td className="tiny">{date(r.raisedAt)}<div className="tiny">{ago(r.raisedAt)}</div></td>
                  <td><div className="two-line"><b>{r.siteCode}</b><small>{r.siteName}</small></div></td>
                  <td>{r.storeCode}</td>
                  <td className="r num">{r.lines.length}</td>
                  <td className="r num tiny">{qty(req)} / {qty(app)} / {qty(iss)}</td>
                  <td className="r num">{cur} {money(r.value)}</td>
                  <td><Chip tone={reqTone(r.status)}>{r.status}</Chip></td>
                  <td><span className="linky">Open</span></td>
                </tr>
              );
            })}
          </Table>
        )}
      </Card>

      <DetailModal r={open} onClose={() => setOpen(null)} onChanged={reload} cur={cur} canApprove={can("approve")} canRequisition={can("requisition")} myId={user?.id ?? ""} toast={toast} />
      {creating && <CreateModal onClose={() => setCreating(false)} onDone={() => { setCreating(false); list.reload(); }} />}
    </>
  );
}

function DetailModal({ r, onClose, onChanged, cur, canApprove, canRequisition, myId, toast }: {
  r: RequisitionDto | null; onClose: () => void; onChanged: () => void; cur: string; canApprove: boolean; canRequisition: boolean; myId: string;
  toast: (m: string, bad?: boolean) => void;
}) {
  const [note, setNote] = useState("");
  const [approved, setApproved] = useState<Record<number, string>>({});
  const [busy, setBusy] = useState(false);
  const { config, user } = useAuth();
  if (!r) return null;
  // FR-REQ-010: the approver's level in the matrix decides whether "approve" is final or a recommendation that escalates
  const levels = [...(config?.approvalLevels ?? [])].sort((a, b) => a.level - b.level);
  const myLevel = Math.max(0, ...levels.filter((l) => l.roleKey === user?.roleKey).map((l) => l.level), user?.roleKey === "sysadmin" ? Math.max(0, ...levels.map((l) => l.level)) : 0);
  const label = (n: number) => levels.find((l) => l.level === n)?.label ?? `level ${n}`;
  const nextLabel = label(levels.find((l) => l.level > myLevel)?.level ?? r.requiredLevel);
  const recommendOnly = myLevel > 0 && myLevel < r.requiredLevel;
  const notMyTurn = myLevel > 0 && myLevel < r.currentLevel;
  const decide = async (approve: boolean) => {
    setBusy(true);
    try {
      await post(`/api/requisitions/${r.id}/decide`, { approve, note: note || null, lines: r.lines.map((l) => ({ lineId: l.id, qtyApproved: approved[l.id] !== undefined ? +approved[l.id] : l.qtyRequested })) });
      toast(!approve ? "Rejected and returned to the supervisor" : recommendOnly ? `Recommended and escalated to ${nextLabel}` : "Approved — stock reserved and Issue Slip generated");
      setNote(""); onChanged();
    } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const act = async (path: string, ok: string) => {
    setBusy(true);
    try { await post(`/api/requisitions/${r.id}/${path}`, path === "cancel" ? { reason: note || null } : undefined); toast(ok); onChanged(); }
    catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };
  const decidable = r.status === "Submitted" && canApprove && r.raisedById !== myId && !notMyTurn && myLevel > 0;
  return (
    <Modal open onClose={onClose} title={r.number} sub={`${r.siteCode} · ${r.siteName} · ${r.zone} zone`}
      chips={<><Chip tone={reqTone(r.status)}>{r.status}</Chip>{r.issueSlipNo && <Chip tone="plain">Issue slip {r.issueSlipNo}</Chip>}</>}
      footer={<>
        {decidable && <><button className="btn primary" disabled={busy} onClick={() => decide(true)}>{recommendOnly ? `Recommend → ${nextLabel}` : "Approve & reserve stock"}</button>
          <button className="btn danger" disabled={busy || !note.trim()} onClick={() => decide(false)}>Reject</button></>}
        {r.status === "Submitted" && r.raisedById === myId && <span className="tiny">You raised this — someone else must approve it (BR-026).</span>}
        {r.status === "Submitted" && notMyTurn && <span className="tiny">Now with {label(r.currentLevel)} (level {r.currentLevel}).</span>}
        {r.status === "Draft" && canRequisition && <button className="btn primary" disabled={busy} onClick={() => act("submit", "Submitted for approval")}>Submit for approval</button>}
        {["Draft", "Submitted", "Approved"].includes(r.status) && <button className="btn" disabled={busy} onClick={() => act("cancel", "Cancelled; reservations released")}>Cancel requisition</button>}
        <span className="spacer" /><button className="btn" onClick={onClose}>Close</button>
      </>}>
      <div className="kv">
        <div><span>Raised by</span><b>{r.raisedBy}</b></div>
        <div><span>Raised at</span><b>{dateTime(r.raisedAt)}</b></div>
        <div><span>Issuing store</span><b>{r.storeCode}</b></div>
        <div><span>Needed by</span><b>{date(r.needBy)}</b></div>
        <div><span>Decided by</span><b>{r.approvedBy ?? "—"}</b></div>
        <div><span>Decided at</span><b>{dateTime(r.decidedAt)}</b></div>
        <div><span>Value</span><b className="num">{cur} {money(r.value)}</b></div>
        <div><span>Approval</span><b>{r.status === "Submitted" ? `with ${label(r.currentLevel)} · needs ${label(r.requiredLevel)}` : `level ${r.requiredLevel} (${label(r.requiredLevel)})`}</b></div>
      </div>
      {r.recommendedBy && <Notice kind="info"><span><b>Recommended by:</b> {r.recommendedBy}</span></Notice>}
      {recommendOnly && decidable && <Notice kind="watch"><span>The value is above your approval limit. Approving records your recommendation and escalates the requisition to <b>{nextLabel}</b> (FR-REQ-010).</span></Notice>}
      {r.lapsedAt && <Notice kind="crit"><span>The reservation lapsed on {dateTime(r.lapsedAt)} and the stock was released (FR-REQ-014).</span></Notice>}
      {r.purpose && <Notice kind="info"><span><b>Purpose:</b> {r.purpose}</span></Notice>}
      {r.decisionNote && <Notice kind={r.status === "Rejected" ? "crit" : "good"}><span><b>Decision note:</b> {r.decisionNote}</span></Notice>}
      {r.lines.some((l) => l.indentRaised) && <Notice kind="watch"><span>An automatic purchase indent was raised to Procurement for the shortfall lines (FR-RQ-06).</span></Notice>}

      <Table rows={r.lines.length} cols={["Item code", { t: "Requested", c: "r" }, { t: "Available", c: "r" }, { t: "Approved", c: "r" }, { t: "Issued", c: "r" }, { t: "Shortfall", c: "r" }, { t: "Value", c: "r" }]}>
        {r.lines.map((l) => (
          <tr key={l.id}>
            <td><div className="two-line"><b className="mono">{l.itemCode}</b><small>{l.description}</small></div></td>
            <td className="r num">{qty(l.qtyRequested)} {l.uom}</td>
            <td className="r num">{l.available > 0 ? qty(l.available) : <span className="err">0</span>}</td>
            <td className="r">
              {decidable
                ? <input type="number" min="0" max={l.qtyRequested} step="0.001" style={{ width: 88, textAlign: "right" }}
                    value={approved[l.id] ?? String(l.qtyRequested)} onChange={(e) => setApproved({ ...approved, [l.id]: e.target.value })} />
                : <span className="num">{qty(l.qtyApproved)}</span>}
            </td>
            <td className="r num">{qty(l.qtyIssued)}</td>
            <td className="r num">{l.shortfall > 0 ? <span className="err">{qty(l.shortfall)}</span> : "—"}</td>
            <td className="r num tiny">{cur} {money((l.qtyApproved || l.qtyRequested) * 0)}</td>
          </tr>
        ))}
      </Table>

      {(decidable || r.status === "Draft" || r.status === "Approved") &&
        <div className="field"><label>{decidable ? "Decision note (required to reject)" : "Note"}</label>
          <textarea value={note} onChange={(e) => setNote(e.target.value)} placeholder={decidable ? "Approved in part — balance to follow after the next consignment" : "Reason"} /></div>}
    </Modal>
  );
}

function CreateModal({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const sites = useAsync(() => get<SiteDto[]>("/api/sites"), []);
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const articles = useAsync(() => get<ArticleDto[]>("/api/articles?status=Active"), []);
  const [siteId, setSiteId] = useState<number | null>(null);
  const [storeId, setStoreId] = useState<number | null>(null);
  const [needBy, setNeedBy] = useState("");
  const [purpose, setPurpose] = useState("");
  const [rows, setRows] = useState<{ articleId: number | null; qty: string }[]>([{ articleId: null, qty: "" }]);
  const [busy, setBusy] = useState(false);

  const avail = useAsync(() => storeId ? get<Record<string, number>>(`/api/requisitions/availability/${storeId}`) : Promise.resolve({} as Record<string, number>), [storeId]);
  const lines = useMemo(() => rows.filter((r) => r.articleId && +r.qty > 0).map((r) => ({ articleId: r.articleId!, qty: +r.qty })), [rows]);

  const save = async (submit: boolean) => {
    if (!siteId || !storeId || lines.length === 0) { toast("Choose the site, the store and at least one item.", true); return; }
    setBusy(true);
    try {
      await post("/api/requisitions", { siteId, storeId, needBy: needBy || null, purpose: purpose || null, lines, submit });
      toast(submit ? "Requisition submitted for approval" : "Draft saved");
      onDone();
    } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
  };

  return (
    <Modal open onClose={onClose} title="New requisition" sub="Material is requested for one site at a time"
      footer={<><button className="btn primary" disabled={busy} onClick={() => save(true)}>Submit for approval</button>
        <button className="btn" disabled={busy} onClick={() => save(false)}>Save as draft</button>
        <span className="spacer" /><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>Site location</label>
            <select value={siteId ?? ""} onChange={(e) => setSiteId(e.target.value ? +e.target.value : null)}>
              <option value="">— choose a site —</option>
              {(sites.data ?? []).filter((s) => s.status !== "Completed").map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name} ({s.zone})</option>)}
            </select>
            <span className="hint">Free-text site names are not accepted. Missing site? Add it under Master data ▸ Site Locations.</span>
          </div>
          <div className="field"><label>Issuing store</label>
            <select value={storeId ?? ""} onChange={(e) => setStoreId(e.target.value ? +e.target.value : null)}>
              <option value="">— choose a store —</option>
              {(stores.data ?? []).filter((s) => s.active).map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
            </select>
          </div>
          <div className="field"><label>Needed by</label><input type="date" value={needBy} onChange={(e) => setNeedBy(e.target.value)} /></div>
        </div>
        <div className="field"><label>Purpose</label><input value={purpose} onChange={(e) => setPurpose(e.target.value)} placeholder="New installation on the Mall Road stretch, poles 12–31" /></div>

        <div className="fsec">
          <h4>Items required</h4>
          <Table rows={rows.length} cols={["Article", { t: "Available at store", c: "r" }, { t: "Quantity", c: "r" }, ""]}>
            {rows.map((row, i) => {
              const a = (articles.data ?? []).find((x) => x.id === row.articleId);
              const have = row.articleId ? (avail.data?.[String(row.articleId)] ?? 0) : null;
              return (
                <tr key={i}>
                  <td>
                    <select value={row.articleId ?? ""} style={{ width: "100%" }}
                      onChange={(e) => setRows(rows.map((r, j) => j === i ? { ...r, articleId: e.target.value ? +e.target.value : null } : r))}>
                      <option value="">— choose an article —</option>
                      {(articles.data ?? []).map((x) => <option key={x.id} value={x.id}>{x.itemCode} — {x.description}</option>)}
                    </select>
                  </td>
                  <td className="r num">{have === null ? "—" : have > 0 ? <>{qty(have)} {a?.uom}</> : <span className="err">none</span>}</td>
                  <td className="r"><input type="number" min="0" step="0.001" style={{ width: 96, textAlign: "right" }} value={row.qty}
                    onChange={(e) => setRows(rows.map((r, j) => j === i ? { ...r, qty: e.target.value } : r))} /></td>
                  <td><button className="btn small" onClick={() => setRows(rows.length === 1 ? [{ articleId: null, qty: "" }] : rows.filter((_, j) => j !== i))}>Remove</button></td>
                </tr>
              );
            })}
          </Table>
          <div className="actions"><button className="btn small" onClick={() => setRows([...rows, { articleId: null, qty: "" }])}>Add another item</button></div>
          {lines.some((l) => (avail.data?.[String(l.articleId)] ?? 0) < l.qty) &&
            <Notice kind="watch">Some lines exceed what the store holds. You can still submit — the system raises an automatic purchase indent for the shortfall.</Notice>}
        </div>
      </div>
    </Modal>
  );
}
