"use client";
import { useState } from "react";
import { get } from "@/lib/api";
import type { DrillDto } from "@/lib/types";
import { dateTime, geoTone, money } from "@/lib/format";
import { Card, Chip, ErrorBox, Lightbox, Loading, Notice, StatusChip, Table, useAsync } from "@/components/ui";
import { EvidencePhoto, ScanBox } from "@/components/Controls";

const looksLikeDate = (v: unknown) => typeof v === "string" && /^\d{4}-\d{2}-\d{2}T/.test(v);

/** FR-RPT-005: Summary → administrative area → site → requisition / issue → item → evidence and validation record. */
export default function ExplorePage() {
  const [at, setAt] = useState<{ level: string; key?: string | null }>({ level: "summary" });
  const [zoom, setZoom] = useState<string | null>(null);
  const d = useAsync(() => get<DrillDto>(`/api/explore/drill?level=${at.level}${at.key ? `&key=${encodeURIComponent(at.key)}` : ""}`), [at.level, at.key]);
  const x = d.data;
  return (
    <>
      <div className="filters">
        <span className="tiny">Trace one item directly:</span>
        <div style={{ flex: 1, maxWidth: 520 }}><ScanBox onScan={(qr) => setAt({ level: "item", key: qr })} placeholder="Scan or paste an item QR" /></div>
      </div>
      {x && <div className="crumbs">
        {x.path.map((c, i) => <span key={i}>{i > 0 && <span className="sep">›</span>}{i === x.path.length - 1 ? <b>{c.label}</b> : <button onClick={() => setAt({ level: c.level, key: c.key })}>{c.label}</button>}</span>)}
      </div>}
      {d.loading ? <Loading /> : d.error ? <ErrorBox error={d.error} /> : x && (x.level !== "item" ? (
        <Card flush title={x.title} sub="Click a row to go one level down">
          <Table rows={x.rows.length} empty="Nothing here within your scope." cols={x.columns.map((c) => ({ t: c, c: /issued|installed|pending|exceptions|sites|quantity|labels/i.test(c) ? "r" : undefined }))}>
            {x.rows.map((r, i) => (
              <tr key={i} className={r.level ? "row-link" : ""} onClick={() => r.level && setAt({ level: r.level, key: r.key })}>
                {r.cells.map((v, j) => <td key={j} className={typeof v === "number" ? "r num" : ""}>{v === null || v === "" ? "—" : looksLikeDate(v) ? <span className="tiny">{dateTime(String(v))}</span> : typeof v === "number" ? money(v) : String(v)}</td>)}
              </tr>
            ))}
          </Table>
        </Card>
      ) : x.item && <>
        <Card title={x.title} sub={x.item.item.description} actions={<StatusChip s={x.item.item.status} />}>
          <div className="kv">
            <div><span>PO</span><b>{x.item.item.poNumber}</b></div><div><span>Vendor</span><b>{x.item.item.vendorName}</b></div>
            <div><span>GRN</span><b>{x.item.item.grnId ?? "—"}</b></div><div><span>Received</span><b>{dateTime(x.item.item.receivedAt)}</b></div>
            <div><span>Requisition</span><b>{x.item.item.requisitionId ?? "—"}</b></div><div><span>Issue</span><b>{x.item.item.issueId ?? "—"}</b></div>
            <div><span>Site</span><b>{x.item.item.siteCode ?? "—"}</b></div><div><span>Location</span><b className="mono">{x.item.item.locationCode ?? "—"}</b></div>
            <div><span>Installed</span><b>{dateTime(x.item.item.installedAt)}</b></div><div><span>Warranty until</span><b>{x.item.item.warrantyExpires ?? "—"}</b></div>
          </div>
        </Card>
        {x.validation && <Card title={`Evidence & validation — ${x.validation.number}`} actions={<Chip tone={geoTone(x.validation.geoStatus)}>{x.validation.geoStatus}</Chip>}>
          <Notice kind={x.validation.geoStatus === "Confirmed" || x.validation.geoStatus === "ApprovedException" ? "good" : "watch"}>
            <span>{Math.round(x.validation.distanceM)} m from the site point (geo-fence {x.validation.geofenceM} m) · accuracy ±{Math.round(x.validation.accuracyM)} m · officer {x.validation.officer}{x.validation.verifiedBy ? ` · verified by ${x.validation.verifiedBy}` : ""}{x.validation.justification ? ` — “${x.validation.justification}”` : ""}</span>
          </Notice>
          <div className="evidence-pair"><EvidencePhoto e={x.validation.before} onZoom={setZoom} /><EvidencePhoto e={x.validation.after} onZoom={setZoom} /></div>
        </Card>}
        <Card flush title="Complete history" sub="Every movement of this label, from the audit trail">
          <Table rows={x.item.history.length} cols={["When", "Action", "By", "Details"]}>
            {x.item.history.map((h, i) => <tr key={i}><td className="tiny">{dateTime(h.at)}</td><td><b>{h.action}</b></td><td className="tiny">{h.by ?? "—"}{h.role ? ` · ${h.role}` : ""}</td><td className="mono tiny" style={{ maxWidth: 420, wordBreak: "break-word" }}>{h.nextJson ?? h.reason ?? ""}</td></tr>)}
          </Table>
        </Card>
      </>)}
      <Lightbox src={zoom} onClose={() => setZoom(null)} />
    </>
  );
}
