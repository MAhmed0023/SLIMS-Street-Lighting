"use client";
import { useState } from "react";
import { useRouter } from "next/navigation";
import { get } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { DocumentDto, ReportResult } from "@/lib/types";
import { ago, dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Kpi, Loading, Table, Tabs, useAsync } from "@/components/ui";

type T = "approval" | "signature" | "register";

export default function ApprovalsPage() {
  const router = useRouter();
  const { can } = useAuth();
  const [tab, setTab] = useState<T>("approval");
  const approvals = useAsync(() => get<DocumentDto[]>("/api/documents?box=approval"), []);
  const signatures = useAsync(() => get<DocumentDto[]>("/api/documents?box=signature"), []);
  const register = useAsync(() => can("report") ? get<ReportResult>("/api/reports/signatures") : Promise.resolve(null), [can]);
  const list = tab === "approval" ? approvals : signatures;
  return (
    <div className="grid">
      <div className="grid g-kpi">
        <Kpi label="Awaiting approval / concurrence" value={approvals.data?.length ?? "…"} tone={(approvals.data ?? []).some((d) => d.overdue) ? "crit" : undefined} onClick={() => setTab("approval")} />
        <Kpi label="Awaiting signature" value={signatures.data?.length ?? "…"} onClick={() => setTab("signature")} />
        <Kpi label="Overdue at these stages" value={[...(approvals.data ?? []), ...(signatures.data ?? [])].filter((d) => d.overdue).length} tone="crit" />
        <Kpi label="Awaiting you" value={[...(approvals.data ?? []), ...(signatures.data ?? [])].filter((d) => d.awaitingMe).length} tone="watch" onClick={() => router.push("/documents?box=inbox")} />
      </div>
      <div className="modal-tabs" style={{ borderRadius: 8 }}>
        <Tabs<T> value={tab} onChange={setTab} tabs={[{ key: "approval", label: "Pending approvals" }, { key: "signature", label: "Pending signatures" }, ...(can("report") ? [{ key: "register" as T, label: "Signature register" }] : [])]} />
      </div>
      {tab !== "register" && <Card flush title={tab === "approval" ? "Waiting at an approval or concurrence stage" : "Approved and waiting for signature"} sub="Authority is checked against the delegation of powers when the officer acts">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="Nothing is waiting." cols={["Document", "Type", "With", "Stage", "Received", "Due", ""]}>
            {list.data!.map((d) => (
              <tr key={d.id} className="row-link" onClick={() => router.push(`/documents?id=${d.id}`)}>
                <td><div className="two-line"><b>{d.subject}</b><small className="mono">{d.number}{d.activeVersionNo ? ` · v${d.activeVersionNo}` : ""}</small></div></td>
                <td className="tiny">{d.documentType}</td>
                <td>{d.currentHolders}{d.awaitingMe && <div><Chip tone="info">You</Chip></div>}</td>
                <td className="tiny">{d.currentStage}</td>
                <td className="tiny">{dateTime(d.heldSince)}<div>{ago(d.heldSince)}</div></td>
                <td className="tiny">{dateTime(d.dueAt)}{d.overdue && <div><Chip tone="crit">Overdue</Chip></div>}</td>
                <td><span className="linky">Open</span></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>}
      {tab === "register" && <Card flush title="Signature register" sub="Every signature field: designated signer, who signed, the version and its fingerprint">
        {register.loading ? <Loading /> : register.error ? <ErrorBox error={register.error} /> : (
          <Table rows={register.data?.rows.length ?? 0} empty="No signatures yet." cols={["Document", "Designated signer", "Signed by", "Status", "Signed", "Code"]}>
            {(register.data?.rows ?? []).map((r, i) => (
              <tr key={i}><td className="mono">{String(r.document)}</td><td>{String(r.signer ?? "")}</td><td>{String(r.signedBy ?? "")}{r.actingFor ? <div className="tiny">for {String(r.actingFor)}</div> : null}</td>
                <td><Chip tone={r.status === "Signed" ? "ok" : r.status === "Invalidated" ? "crit" : "info"}>{String(r.status)}</Chip></td><td className="tiny">{dateTime(r.signedAt as string)}</td><td className="mono">{String(r.code ?? "")}</td></tr>
            ))}
          </Table>
        )}
      </Card>}
    </div>
  );
}
