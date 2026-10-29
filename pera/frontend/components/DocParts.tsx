"use client";
import { useMemo } from "react";
import type { RouteStageDto, VerificationDto } from "@/lib/types";
import { ACTION_LABEL, ago, dateTime, hours, stageTone } from "@/lib/format";
import { Chip, Empty, Table } from "./ui";

/** The route as columns of steps; parallel stages stack within a step. */
export function RouteTracker({ route }: { route: RouteStageDto[] }) {
  const steps = useMemo(() => {
    const m = new Map<number, RouteStageDto[]>();
    route.filter((r) => !r.adHoc).forEach((r) => m.set(r.step, [...(m.get(r.step) ?? []), r]));
    return [...m.entries()].sort((a, b) => a[0] - b[0]);
  }, [route]);
  const info = route.filter((r) => r.adHoc);
  if (!route.length) return <Empty>No route yet.</Empty>;
  return (
    <>
      <div className="route">
        {steps.map(([step, stages]) => (
          <div key={step} className="rstep">
            <div className="rs-head">Step {step}{stages.length > 1 ? " · parallel" : ""}</div>
            {stages.map((s) => <Node key={s.id} s={s} />)}
          </div>
        ))}
      </div>
      {info.length > 0 && <div className="tiny">For information: {info.map((s) => `${s.userName} (${s.status === "Done" ? "acknowledged " + dateTime(s.actedAt) : s.status.toLowerCase()})`).join(" · ")}</div>}
    </>
  );
}
function Node({ s }: { s: RouteStageDto }) {
  return (
    <div className={`rnode ${s.status} ${s.overdue ? "overdue" : ""}`}>
      <div className="rn-type">{s.type}{s.mandatory && <span title="Mandatory stage">●</span>}<Chip tone={stageTone(s.status)}>{s.status}</Chip></div>
      <div className="rn-name">{s.name}</div>
      <div className="rn-who">{s.userName ?? <span className="err">no officer</span>}{s.designation ? <span className="tiny"> · {s.designation}</span> : null}</div>
      {s.status === "Active" && <div className="rn-meta">received {ago(s.receivedAt)} · due {dateTime(s.dueAt)}{s.overdue && <span className="err"> · overdue</span>}</div>}
      {s.actedAt && <div className="rn-meta">{s.outcome ? (ACTION_LABEL[s.outcome] ?? s.outcome) : ""} by {s.actedByName}{s.actingForName ? ` for ${s.actingForName}` : ""} · {dateTime(s.actedAt)} · held {hours(s.heldHours)}</div>}
      {s.comment && <div className="rn-meta">“{s.comment}”</div>}
    </div>
  );
}


export function VerifyTable({ v }: { v: VerificationDto }) {
  const yes = (b: boolean) => b ? <span className="verdict-ok">yes</span> : <span className="verdict-bad">no</span>;
  return (
    <Table rows={v.signatures.length} empty="No signatures on this document." cols={["Signer", "Signed", "Code", "Seal", "File unchanged", "Stamped copy intact", "Verdict"]}>
      {v.signatures.map((s) => (
        <tr key={s.fieldId}><td>{s.signer}{s.actingFor && <div className="tiny">for {s.actingFor}</div>}</td><td className="tiny">{dateTime(s.signedAt)}</td><td className="mono">{s.code}</td>
          <td>{yes(s.sealValid)}</td><td>{yes(s.documentUnchanged)}</td><td>{yes(s.signedFileIntact)}</td><td className={s.verdict === "Valid" ? "verdict-ok" : "verdict-bad"}>{s.verdict}</td></tr>
      ))}
    </Table>
  );
}

