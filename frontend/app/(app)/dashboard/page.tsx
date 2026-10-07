"use client";
import Link from "next/link";
import { get } from "@/lib/api";
import type { DashboardDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { ago, date, dateTime, int, money, statusLabel } from "@/lib/format";
import { BarChart, Card, Chip, Donut, ErrorBox, Kpi, Loading, StatusChip, Table, useAsync } from "@/components/ui";

const STATUS_COLOURS: Record<string, string> = {
  Created: "var(--muted)", Received: "var(--info)", Reserved: "var(--series-4)", Issued: "var(--watch)", AtSite: "var(--series-3)",
  PendingVerification: "var(--crit)", Installed: "var(--ok)", OutOfOrder: "var(--crit)", Dismantled: "var(--muted)",
  Returned: "var(--watch)", UnderRepair: "var(--series-2)", WarrantyClaim: "var(--series-2)", Scrap: "var(--muted)", Disposed: "var(--muted)",
};

export default function DashboardPage() {
  const { config } = useAuth();
  const d = useAsync(() => get<DashboardDto>("/api/dashboard"), []);
  if (d.loading) return <Loading what="dashboard" />;
  if (d.error) return <ErrorBox error={d.error} />;
  const x = d.data!;
  const cur = config?.app.currency ?? "Rs.";
  const segs = Object.entries(x.itemsByStatus).filter(([, v]) => v > 0).sort((a, b) => b[1] - a[1])
    .map(([k, v]) => ({ label: statusLabel(k), value: v, color: STATUS_COLOURS[k] ?? "var(--muted)" }));

  return (
    <>
      <div className="grid g-kpi">
        {x.kpis.map((k) => <Kpi key={k.key} label={k.label} tone={k.tone}
          value={k.key === "failure" ? `${k.value}%` : int(k.value)} sub={k.sub} />)}
      </div>

      <div className="grid g-2">
        <Card title="Installations and returns, last 12 months" sub="Geo-verified installations against items reported out of order">
          <BarChart data={x.monthly} series={["Installed", "Out of order"]} />
        </Card>
        <Card title="Where every label is right now" sub="Item lifecycle status across the whole estate">
          <div style={{ display: "flex", gap: 18, alignItems: "center", flexWrap: "wrap" }}>
            <Donut segments={segs} />
            <div className="legend" style={{ flexDirection: "column", gap: 4, marginTop: 0 }}>
              {segs.map((s) => <span key={s.label}><i style={{ background: s.color }} />{s.label} — <b className="num">{int(s.value)}</b></span>)}
            </div>
          </div>
        </Card>
      </div>

      <div className="grid g-2">
        <Card flush title="Geo-fence exceptions awaiting verification" sub="Evidence fell outside the site geo-fence (BR-013)"
          actions={<Link className="btn small" href="/field">Open field module</Link>}>
          <Table rows={x.exceptions.length} empty="No exceptions — every installation passed geo-validation." cols={["Site", "Item", "Officer", { t: "Deviation", c: "r" }, "Captured"]}>
            {x.exceptions.slice(0, 8).map((e) => (
              <tr key={e.id}>
                <td><div className="two-line"><b>{e.siteCode}</b><small>{e.siteName}</small></div></td>
                <td className="mono">{e.itemCode}</td>
                <td>{e.officer}</td>
                <td className="r"><b className="err">{Math.round(e.distanceM)} m</b><div className="tiny">allowed {e.geofenceM} m</div></td>
                <td className="tiny">{dateTime(e.capturedAt)}</td>
              </tr>
            ))}
          </Table>
        </Card>

        <Card flush title="Requisitions awaiting approval" sub="Stock is only reserved once a requisition is approved"
          actions={<Link className="btn small" href="/requisitions">Open requisitions</Link>}>
          <Table rows={x.pendingApprovals.length} empty="Approval queue is clear." cols={["Requisition", "Site", "Raised by", { t: "Lines", c: "r" }, { t: "Value", c: "r" }, "Waiting"]}>
            {x.pendingApprovals.slice(0, 8).map((r) => (
              <tr key={r.id}>
                <td className="mono">{r.number}</td>
                <td><div className="two-line"><b>{r.siteCode}</b><small>{r.siteName}</small></div></td>
                <td>{r.raisedBy}</td>
                <td className="r num">{r.lines.length}</td>
                <td className="r num">{cur} {money(r.value)}</td>
                <td className="tiny">{ago(r.raisedAt)}</td>
              </tr>
            ))}
          </Table>
        </Card>
      </div>

      <div className="grid g-2">
        <Card flush title="Overdue returns from site" sub={`Removed at site but not scanned back into store within ${config?.controls.returnDays ?? 7} days (BR-029)`}
          actions={<Link className="btn small" href="/returns">Open returns</Link>}>
          <Table rows={x.overdueReturns.length} empty="Every removed item has been returned on time." cols={["Return", "Item", "Site", "Reason", "Reported"]}>
            {x.overdueReturns.slice(0, 6).map((r) => (
              <tr key={r.id}>
                <td className="mono">{r.number}</td>
                <td><div className="two-line"><b className="mono">{r.itemCode}</b><small>{r.description}</small></div></td>
                <td>{r.siteCode ?? "—"}</td>
                <td>{r.reason}</td>
                <td className="tiny">{date(r.reportedAt)} <Chip tone="crit">{r.serviceDays != null ? `${r.serviceDays} d in service` : "overdue"}</Chip></td>
              </tr>
            ))}
          </Table>
        </Card>

        <Card flush title="Issued but not acknowledged at site" sub={`Gate pass raised over ${config?.controls.ackOverdueHours ?? 48} hours ago with no field acknowledgement`}
          actions={<Link className="btn small" href="/issue">Open issuance</Link>}>
          <Table rows={x.unacknowledgedIssues.length} empty="All despatched material has been acknowledged." cols={["Issue", "Gate pass", "Site", { t: "Labels", c: "r" }, "Issued"]}>
            {x.unacknowledgedIssues.slice(0, 6).map((i) => (
              <tr key={i.id}>
                <td className="mono">{i.number}</td>
                <td className="mono tiny">{i.gatePassNo}</td>
                <td><div className="two-line"><b>{i.siteCode}</b><small>{i.siteName}</small></div></td>
                <td className="r num">{i.itemCount}</td>
                <td className="tiny">{ago(i.issuedAt)}</td>
              </tr>
            ))}
          </Table>
        </Card>
      </div>

      <div className="grid g-2">
        <Card flush title="By zone" sub="Installed · in the field · failed · sites">
          <Table rows={x.byZone.length} cols={["Zone", { t: "Installed", c: "r" }, { t: "In field", c: "r" }, { t: "Failed", c: "r" }, { t: "Sites", c: "r" }]}>
            {x.byZone.map((z) => (
              <tr key={z.key}>
                <td><b>{z.label}</b></td>
                <td className="r num">{int(z.a)}</td><td className="r num">{int(z.b)}</td>
                <td className="r num">{z.c > 0 ? <span className="err">{int(z.c)}</span> : "—"}</td>
                <td className="r num">{int(z.d)}</td>
              </tr>
            ))}
          </Table>
        </Card>

        <Card flush title="By article category" sub="Quantity in stock · installed · in field · quarantine">
          <Table rows={x.byCategory.length} cols={["Category", { t: "In stock", c: "r" }, { t: "Installed", c: "r" }, { t: "In field", c: "r" }, { t: "Quarantine", c: "r" }]}>
            {x.byCategory.map((c) => (
              <tr key={c.key}>
                <td><b>{c.label}</b> <span className="mono tiny">{c.key}</span></td>
                <td className="r num">{int(c.a)}</td><td className="r num">{int(c.b)}</td><td className="r num">{int(c.c)}</td>
                <td className="r num">{c.d > 0 ? <span className="watch">{int(c.d)}</span> : "—"}</td>
              </tr>
            ))}
          </Table>
        </Card>
      </div>
    </>
  );
}
