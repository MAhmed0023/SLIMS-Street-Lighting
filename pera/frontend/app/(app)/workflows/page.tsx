"use client";
import { useState } from "react";
import { get, post, put } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { AuthorityRuleDto, DesignationDto, DocumentTypeDto, TemplateDto, TemplateStageDto, UserRefDto } from "@/lib/types";
import { STAGE_TYPES, date, isoDate } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, Tabs, UserSelect, errText, useAsync, useToast } from "@/components/ui";

type T = "templates" | "types" | "authority";
const MODES = [{ k: "ReportingOfficer", l: "Initiator's reporting officer" }, { k: "Designation", l: "Holder of a designation" }, { k: "User", l: "A fixed officer" }, { k: "Nominated", l: "Initiator nominates" }];

export default function WorkflowsPage() {
  const { can } = useAuth();
  const edit = can("workflow");
  const [tab, setTab] = useState<T>("templates");
  const [retired, setRetired] = useState(false);
  const types = useAsync(() => get<DocumentTypeDto[]>("/api/workflow/types"), []);
  const templates = useAsync(() => get<TemplateDto[]>(`/api/workflow/templates?retired=${retired}`), [retired]);
  const rules = useAsync(() => get<AuthorityRuleDto[]>("/api/workflow/authority"), []);
  const desig = useAsync(() => get<DesignationDto[]>("/api/org/designations"), []);
  const people = useAsync(() => get<UserRefDto[]>("/api/org/directory"), []);
  const [tpl, setTpl] = useState<TemplateDto | "new" | null>(null);
  const [type, setType] = useState<Partial<DocumentTypeDto> | null>(null);
  const [rule, setRule] = useState<Partial<AuthorityRuleDto> | null>(null);
  const toast = useToast();
  const status = async (t: TemplateDto, s: string) => { try { await post(`/api/workflow/templates/${t.id}/status/${s}`); templates.reload(); toast(`Template ${s.toLowerCase()}`); } catch (e) { toast(errText(e), true); } };
  const saveType = async () => {
    try { await put("/api/workflow/types", { ...type, id: type!.id ?? null, code: type!.code ?? "", name: type!.name ?? "", allowedExtensions: type!.allowedExtensions ?? "pdf,docx", maxSizeMb: type!.maxSizeMb ?? 20, stageServiceHours: type!.stageServiceHours ?? 48, defaultClassification: type!.defaultClassification ?? "Internal", defaultTemplateId: type!.defaultTemplateId ?? null, active: type!.active ?? true,
      requireAttachment: !!type!.requireAttachment, enforceAuthority: !!type!.enforceAuthority, allowSelfApproval: !!type!.allowSelfApproval, allowParallel: !!type!.allowParallel, amendmentNeedsApproval: !!type!.amendmentNeedsApproval, requireApprovalStage: !!type!.requireApprovalStage, approvalCommentRequired: !!type!.approvalCommentRequired });
      toast("Saved"); setType(null); types.reload(); } catch (e) { toast(errText(e), true); }
  };
  const saveRule = async () => {
    try { await put("/api/workflow/authority", { id: rule!.id ?? null, documentTypeId: rule!.documentTypeId ?? null, designationId: rule!.designationId, authority: rule!.authority ?? "Approve", effectiveFrom: rule!.effectiveFrom || null, effectiveTo: rule!.effectiveTo || null, active: rule!.active ?? true, note: rule!.note ?? null });
      toast("Saved"); setRule(null); rules.reload(); } catch (e) { toast(errText(e), true); }
  };
  const check = (k: keyof DocumentTypeDto, label: string) => <label className="tiny" style={{ display: "flex", gap: 6 }}><input type="checkbox" style={{ width: "auto" }} checked={!!type?.[k]} onChange={(e) => setType({ ...type, [k]: e.target.checked })} />{label}</label>;
  return (
    <div className="grid">
      {!edit && <Notice kind="info"><span>Read-only. Workflow administrators maintain templates and the authority matrix.</span></Notice>}
      <div className="modal-tabs" style={{ borderRadius: 8 }}>
        <Tabs<T> value={tab} onChange={setTab} tabs={[{ key: "templates", label: "Route templates" }, { key: "types", label: "Document types" }, { key: "authority", label: "Authority matrix (delegation of powers)" }]} />
      </div>
      {tab === "templates" && <>
        <div className="filters"><label className="tiny"><input type="checkbox" style={{ width: "auto" }} checked={retired} onChange={(e) => setRetired(e.target.checked)} /> show retired versions</label><span className="spacer" />{edit && <button className="btn primary" onClick={() => setTpl("new")}>New template</button>}</div>
        {templates.loading ? <Loading /> : templates.error ? <ErrorBox error={templates.error} /> : templates.data!.map((t) => (
          <Card key={t.id} title={<>{t.name} <span className="tiny mono">{t.code} v{t.version}</span></>} sub={`${t.documentType} · owner ${t.ownerName} · effective ${date(t.effectiveFrom)}${t.effectiveTo ? ` to ${date(t.effectiveTo)}` : ""} · ${t.allowCustomization ? "initiators may add stages" : "fixed route"}`}
            actions={<><Chip tone={t.status === "Active" ? "ok" : t.status === "Draft" ? "info" : "muted"}>{t.status}</Chip>{edit && t.status !== "Retired" && <button className="btn small" onClick={() => setTpl(t)}>{t.status === "Active" ? "Change (new version)" : "Edit"}</button>}
              {edit && t.status === "Draft" && <button className="btn small primary" onClick={() => status(t, "Active")}>Activate</button>}{edit && t.status === "Active" && <button className="btn small" onClick={() => status(t, "Retired")}>Retire</button>}</>}>
            <StageTable stages={t.stages} />
          </Card>
        ))}
      </>}
      {tab === "types" && <Card flush title="Document types" actions={edit && <button className="btn small primary" onClick={() => setType({ allowedExtensions: "pdf,docx", maxSizeMb: 20, stageServiceHours: 48, requireAttachment: true, enforceAuthority: true, allowParallel: true, amendmentNeedsApproval: true, requireApprovalStage: true, defaultClassification: "Internal", active: true })}>New type</button>}>
        {types.loading ? <Loading /> : <Table rows={types.data?.length ?? 0} cols={["Code", "Name", "Files", "Controls", "Stage target", "Default route", ""]}>
          {(types.data ?? []).map((t) => <tr key={t.id}><td className="mono">{t.code}</td><td>{t.name}{!t.active && <Chip tone="muted">inactive</Chip>}</td><td className="tiny">{t.allowedExtensions} · {t.maxSizeMb} MB</td>
            <td className="tiny">{[t.requireApprovalStage && "approval required", t.enforceAuthority && "authority enforced", !t.allowSelfApproval && "no self-approval", t.allowParallel && "parallel steps", t.amendmentNeedsApproval && "amendments approved"].filter(Boolean).join(" · ")}</td>
            <td className="num">{t.stageServiceHours} h</td><td className="tiny">{templates.data?.find((x) => x.id === t.defaultTemplateId)?.name ?? "—"}</td>
            <td>{edit && <button className="btn small" onClick={() => setType(t)}>Edit</button>}</td></tr>)}
        </Table>}
      </Card>}
      {tab === "authority" && <Card flush title="Authority matrix" sub="Which designation may concur, approve or sign which document type, and from when. Checked at route validation and again at the moment of action."
        actions={edit && <button className="btn small primary" onClick={() => setRule({ authority: "Approve", active: true })}>Grant authority</button>}>
        {rules.loading ? <Loading /> : <Table rows={rules.data?.length ?? 0} cols={["Designation", "Authority", "Document type", "Effective", "Note", "Status", ""]}>
          {(rules.data ?? []).map((r) => <tr key={r.id}><td>{r.designation}</td><td><b>{r.authority}</b></td><td>{r.documentType ?? "All types"}</td><td className="tiny">{date(r.effectiveFrom)}{r.effectiveTo ? ` → ${date(r.effectiveTo)}` : " onwards"}</td>
            <td className="tiny">{r.note}</td><td><Chip tone={r.active ? "ok" : "muted"}>{r.active ? "In force" : "Withdrawn"}</Chip></td><td>{edit && <button className="btn small" onClick={() => setRule(r)}>Edit</button>}</td></tr>)}
        </Table>}
      </Card>}

      {tpl && <TemplateForm t={tpl === "new" ? null : tpl} types={types.data ?? []} desig={desig.data ?? []} people={people.data ?? []} onClose={() => setTpl(null)} onSaved={() => { setTpl(null); templates.reload(); }} />}
      {type && <Modal open onClose={() => setType(null)} title={type.id ? `Edit ${type.name}` : "New document type"} footer={<><button className="btn primary" onClick={saveType}>Save</button><span className="spacer" /><button className="btn" onClick={() => setType(null)}>Cancel</button></>}>
        <div className="form"><div className="frow">
          <div className="field"><label>Code</label><input value={type.code ?? ""} onChange={(e) => setType({ ...type, code: e.target.value })} /></div>
          <div className="field"><label>Name</label><input value={type.name ?? ""} onChange={(e) => setType({ ...type, name: e.target.value })} /></div>
          <div className="field"><label>Allowed file types</label><input value={type.allowedExtensions ?? ""} onChange={(e) => setType({ ...type, allowedExtensions: e.target.value })} placeholder="pdf,docx" /></div>
          <div className="field"><label>Max size (MB)</label><input type="number" value={type.maxSizeMb ?? 20} onChange={(e) => setType({ ...type, maxSizeMb: +e.target.value })} /></div>
          <div className="field"><label>Stage target (hours)</label><input type="number" value={type.stageServiceHours ?? 48} onChange={(e) => setType({ ...type, stageServiceHours: +e.target.value })} /></div>
          <div className="field"><label>Default classification</label><select value={type.defaultClassification} onChange={(e) => setType({ ...type, defaultClassification: e.target.value })}>{["Public", "Internal", "Confidential", "Restricted"].map((c) => <option key={c}>{c}</option>)}</select></div>
          {type.id && <div className="field"><label>Default route</label><select value={type.defaultTemplateId ?? ""} onChange={(e) => setType({ ...type, defaultTemplateId: e.target.value ? +e.target.value : null })}><option value="">Newest active template</option>{(templates.data ?? []).filter((x) => x.documentTypeId === type.id).map((x) => <option key={x.id} value={x.id}>{x.name} v{x.version}</option>)}</select></div>}
        </div>
          <div className="grid g-2">
            {check("requireAttachment", "A working file is required")}{check("requireApprovalStage", "A competent authority must approve")}
            {check("enforceAuthority", "Enforce the authority matrix")}{check("allowSelfApproval", "Allow the initiator to approve / sign (not recommended)")}
            {check("allowParallel", "Allow parallel stages")}{check("amendmentNeedsApproval", "Route amendments need approval")}
            {check("approvalCommentRequired", "A remark is required with every approval")}{check("active", "Active")}
          </div>
        </div>
      </Modal>}
      {rule && <Modal open size="narrow" onClose={() => setRule(null)} title={rule.id ? "Edit authority" : "Grant authority"} footer={<><button className="btn primary" disabled={!rule.designationId} onClick={saveRule}>Save</button><span className="spacer" /><button className="btn" onClick={() => setRule(null)}>Cancel</button></>}>
        <div className="form">
          <div className="field"><label>Designation</label><select value={rule.designationId ?? ""} onChange={(e) => setRule({ ...rule, designationId: e.target.value ? +e.target.value : undefined })}><option value="">—</option>{(desig.data ?? []).map((d) => <option key={d.id} value={d.id}>{d.title}</option>)}</select></div>
          <div className="frow">
            <div className="field"><label>Authority</label><select value={rule.authority} onChange={(e) => setRule({ ...rule, authority: e.target.value })}><option>Approve</option><option>Sign</option><option>Concur</option></select></div>
            <div className="field"><label>Document type</label><select value={rule.documentTypeId ?? ""} onChange={(e) => setRule({ ...rule, documentTypeId: e.target.value ? +e.target.value : null })}><option value="">All types</option>{(types.data ?? []).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></div>
          </div>
          <div className="frow">
            <div className="field"><label>Effective from</label><input type="date" value={isoDate(rule.effectiveFrom)} onChange={(e) => setRule({ ...rule, effectiveFrom: e.target.value })} /></div>
            <div className="field"><label>Effective to</label><input type="date" value={isoDate(rule.effectiveTo)} onChange={(e) => setRule({ ...rule, effectiveTo: e.target.value || null })} /></div>
          </div>
          <div className="field"><label>Basis / note</label><input value={rule.note ?? ""} onChange={(e) => setRule({ ...rule, note: e.target.value })} placeholder="Delegation of powers notification no." /></div>
          <label className="tiny"><input type="checkbox" style={{ width: "auto" }} checked={rule.active ?? true} onChange={(e) => setRule({ ...rule, active: e.target.checked })} /> In force</label>
        </div>
      </Modal>}
    </div>
  );
}

function StageTable({ stages }: { stages: TemplateStageDto[] }) {
  return (
    <Table rows={stages.length} cols={["Step", "Stage", "Type", "Officer", "Target", ""]}>
      {stages.map((s) => <tr key={s.id}><td className="num">{s.step}</td><td>{s.name}</td><td>{s.type}</td>
        <td className="tiny">{s.assigneeMode === "User" ? s.userName : s.assigneeMode === "Designation" ? `Holder of ${s.designation}` : s.assigneeMode === "ReportingOfficer" ? "Initiator's reporting officer" : "Nominated by initiator"}{!s.initiatorMayChangeAssignee && " · fixed"}</td>
        <td className="tiny">{s.serviceHours ? `${s.serviceHours} h` : "type default"}</td><td>{s.mandatory && <Chip tone="plain">Mandatory</Chip>}</td></tr>)}
    </Table>
  );
}

type SL = { step: number; name: string; type: string; mandatory: boolean; assigneeMode: string; userId: string | null; designationId: number | null; serviceHours: number | null; initiatorMayChangeAssignee: boolean };

function TemplateForm({ t, types, desig, people, onClose, onSaved }: { t: TemplateDto | null; types: DocumentTypeDto[]; desig: DesignationDto[]; people: UserRefDto[]; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [h, setH] = useState({ code: t?.code ?? "", name: t?.name ?? "", documentTypeId: t?.documentTypeId ?? types[0]?.id ?? 0, allowCustomization: t?.allowCustomization ?? true, effectiveFrom: isoDate(t?.effectiveFrom) });
  const [rows, setRows] = useState<SL[]>(t?.stages.map((s) => ({ step: s.step, name: s.name, type: s.type, mandatory: s.mandatory, assigneeMode: s.assigneeMode, userId: s.userId, designationId: s.designationId, serviceHours: s.serviceHours, initiatorMayChangeAssignee: s.initiatorMayChangeAssignee })) ?? []);
  const set = (i: number, k: keyof SL, v: unknown) => setRows(rows.map((r, j) => j === i ? { ...r, [k]: v } : r));
  const save = async () => {
    try { await put("/api/workflow/templates", { id: t?.id ?? null, ...h, effectiveFrom: h.effectiveFrom || null, stages: rows }); toast(t?.status === "Active" ? "Published as a new version; the previous version is retired" : "Saved"); onSaved(); }
    catch (e) { toast(errText(e), true); }
  };
  return (
    <Modal open size="wide" onClose={onClose} title={t ? `${t.name} v${t.version}` : "New route template"} sub={t?.status === "Active" ? "Saving publishes the next version. Documents already submitted keep their own route." : "Drafts are not offered to initiators until activated."}
      footer={<><button className="btn primary" disabled={!h.code || !h.name || !rows.length} onClick={save}>{t?.status === "Active" ? "Publish new version" : "Save"}</button><span className="spacer" /><button className="btn" onClick={onClose}>Cancel</button></>}>
      <div className="frow">
        <div className="field"><label>Code</label><input value={h.code} disabled={!!t} onChange={(e) => setH({ ...h, code: e.target.value })} /></div>
        <div className="field"><label>Name</label><input value={h.name} onChange={(e) => setH({ ...h, name: e.target.value })} /></div>
        <div className="field"><label>Document type</label><select value={h.documentTypeId} onChange={(e) => setH({ ...h, documentTypeId: +e.target.value })}>{types.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></div>
        <div className="field"><label>Effective from</label><input type="date" value={h.effectiveFrom} onChange={(e) => setH({ ...h, effectiveFrom: e.target.value })} /></div>
        <div className="field"><label>&nbsp;</label><label className="tiny"><input type="checkbox" style={{ width: "auto" }} checked={h.allowCustomization} onChange={(e) => setH({ ...h, allowCustomization: e.target.checked })} /> Initiators may add or reorder optional stages</label></div>
      </div>
      <Table rows={rows.length} cols={["Step", "Name", "Type", "Officer", "", "Target h", "Mandatory", "Fixed", ""]}>
        {rows.map((r, i) => (
          <tr key={i}>
            <td><input type="number" min={1} value={r.step} onChange={(e) => set(i, "step", +e.target.value)} style={{ width: 56 }} /></td>
            <td><input value={r.name} onChange={(e) => set(i, "name", e.target.value)} /></td>
            <td><select value={r.type} onChange={(e) => set(i, "type", e.target.value)}>{STAGE_TYPES.map((x) => <option key={x}>{x}</option>)}</select></td>
            <td><select value={r.assigneeMode} onChange={(e) => set(i, "assigneeMode", e.target.value)}>{MODES.map((m) => <option key={m.k} value={m.k}>{m.l}</option>)}</select></td>
            <td>{r.assigneeMode === "User" ? <UserSelect users={people} value={r.userId} onChange={(v) => set(i, "userId", v)} /> : r.assigneeMode === "Designation" ?
              <select value={r.designationId ?? ""} onChange={(e) => set(i, "designationId", e.target.value ? +e.target.value : null)}><option value="">—</option>{desig.map((d) => <option key={d.id} value={d.id}>{d.title}</option>)}</select> : null}</td>
            <td><input type="number" value={r.serviceHours ?? ""} onChange={(e) => set(i, "serviceHours", e.target.value ? +e.target.value : null)} style={{ width: 64 }} /></td>
            <td className="c"><input type="checkbox" style={{ width: "auto" }} checked={r.mandatory} onChange={(e) => set(i, "mandatory", e.target.checked)} /></td>
            <td className="c"><input type="checkbox" style={{ width: "auto" }} checked={!r.initiatorMayChangeAssignee} onChange={(e) => set(i, "initiatorMayChangeAssignee", !e.target.checked)} /></td>
            <td><button className="btn small" onClick={() => setRows(rows.filter((_, j) => j !== i))}>Remove</button></td>
          </tr>
        ))}
      </Table>
      <div className="actions"><button className="btn small" onClick={() => setRows([...rows, { step: (rows.at(-1)?.step ?? 0) + 1, name: "", type: "Review", mandatory: false, assigneeMode: "Nominated", userId: null, designationId: null, serviceHours: null, initiatorMayChangeAssignee: true }])}>Add stage</button></div>
      <div className="tiny">Stages sharing a step run in parallel. “Fixed” stops initiators from changing the officer. Signature stages must come after every approval stage.</div>
    </Modal>
  );
}
