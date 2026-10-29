"use client";
import { useMemo, useState } from "react";
import { get, put } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { CategoryDto, DesignationDto, OrgUnitDto, UserRefDto } from "@/lib/types";
import { isoDate } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Table, Tabs, UserSelect, errText, useAsync, useToast } from "@/components/ui";

type T = "lines" | "units" | "designations" | "categories";

export default function HierarchyPage() {
  const toast = useToast();
  const { can } = useAuth();
  const edit = can("configure");
  const [tab, setTab] = useState<T>("lines");
  const people = useAsync(() => get<UserRefDto[]>("/api/org/directory"), []);
  const units = useAsync(() => get<OrgUnitDto[]>("/api/org/units"), []);
  const desig = useAsync(() => get<DesignationDto[]>("/api/org/designations"), []);
  const cats = useAsync(() => get<CategoryDto[]>("/api/org/categories"), []);
  const [unit, setUnit] = useState<Partial<OrgUnitDto> | null>(null);
  const [des, setDes] = useState<Partial<DesignationDto> | null>(null);
  const [cat, setCat] = useState("");

  // reporting lines as an indented tree, top officers first
  const tree = useMemo(() => {
    const all = people.data ?? []; const out: { u: UserRefDto; depth: number }[] = [];
    const walk = (id: string | null, depth: number) => all.filter((u) => u.reportsToId === id || (id === null && u.reportsToId && !all.some((x) => x.id === u.reportsToId))).forEach((u) => { out.push({ u, depth }); if (depth < 12) walk(u.id, depth + 1); });
    walk(null, 0);
    return out;
  }, [people.data]);

  const saveUnit = async () => {
    try { await put("/api/org/units", { id: unit!.id ?? null, code: unit!.code ?? "", name: unit!.name ?? "", kind: unit!.kind ?? "Section", parentId: unit!.parentId ?? null, headUserId: unit!.headUserId ?? null, effectiveFrom: unit!.effectiveFrom ?? null, effectiveTo: unit!.effectiveTo || null, active: unit!.active ?? true }); toast("Saved"); setUnit(null); units.reload(); }
    catch (e) { toast(errText(e), true); }
  };
  const saveDes = async () => {
    try { await put("/api/org/designations", { id: des!.id ?? null, code: des!.code ?? "", title: des!.title ?? "", rank: des!.rank ?? 50, active: des!.active ?? true }); toast("Saved"); setDes(null); desig.reload(); }
    catch (e) { toast(errText(e), true); }
  };
  const saveCat = async (c: Partial<CategoryDto>) => { try { await put("/api/org/categories", { id: c.id ?? 0, name: c.name, active: c.active ?? true }); setCat(""); cats.reload(); } catch (e) { toast(errText(e), true); } };

  return (
    <div className="grid">
      <div className="modal-tabs" style={{ borderRadius: 8 }}>
        <Tabs<T> value={tab} onChange={setTab} tabs={[{ key: "lines", label: "Reporting lines" }, { key: "units", label: "Offices, wings & sections" }, { key: "designations", label: "Designations" }, { key: "categories", label: "Task categories" }]} />
      </div>
      {tab === "lines" && <Card flush title="Reporting lines" sub="Set on each user under Users & Access. Visibility and escalation follow these lines.">
        {people.loading ? <Loading /> : <Table rows={tree.length} cols={["Officer", "Designation", "Unit"]}>
          {tree.map(({ u, depth }) => <tr key={u.id}><td>{Array.from({ length: depth }, (_, i) => <span key={i} className="tree-indent" />)}{depth > 0 ? "└ " : ""}<b>{u.name}</b> <span className="tiny mono">{u.id}</span></td><td>{u.designation}</td><td className="tiny">{u.orgUnit}</td></tr>)}
        </Table>}
      </Card>}
      {tab === "units" && <Card flush title="Offices, wings and sections" actions={edit && <button className="btn small primary" onClick={() => setUnit({ kind: "Section", active: true })}>New unit</button>}>
        {units.loading ? <Loading /> : units.error ? <ErrorBox error={units.error} /> : <Table rows={units.data!.length} cols={["Code", "Name", "Kind", "Parent", "Head", "Members", "Effective", "Status", ""]}>
          {units.data!.map((u) => <tr key={u.id}><td className="mono">{u.code}</td><td>{u.name}</td><td>{u.kind}</td><td className="tiny">{u.parentName ?? "—"}</td><td className="tiny">{u.headName ?? "—"}</td><td className="num">{u.members}</td>
            <td className="tiny">{isoDate(u.effectiveFrom)}{u.effectiveTo ? ` → ${isoDate(u.effectiveTo)}` : ""}</td><td><Chip tone={u.active ? "ok" : "muted"}>{u.active ? "Active" : "Inactive"}</Chip></td>
            <td>{edit && <button className="btn small" onClick={() => setUnit(u)}>Edit</button>}</td></tr>)}
        </Table>}
      </Card>}
      {tab === "designations" && <Card flush title="Designations" sub="Approval and signing powers are granted to designations in the authority matrix" actions={edit && <button className="btn small primary" onClick={() => setDes({ rank: 50, active: true })}>New designation</button>}>
        {desig.loading ? <Loading /> : <Table rows={desig.data?.length ?? 0} cols={["Rank", "Code", "Title", "Holders", "Status", ""]}>
          {(desig.data ?? []).map((d) => <tr key={d.id}><td className="num">{d.rank}</td><td className="mono">{d.code}</td><td>{d.title}</td><td className="num">{d.holders}</td><td><Chip tone={d.active ? "ok" : "muted"}>{d.active ? "Active" : "Inactive"}</Chip></td><td>{edit && <button className="btn small" onClick={() => setDes(d)}>Edit</button>}</td></tr>)}
        </Table>}
      </Card>}
      {tab === "categories" && <Card title="Task categories">
        <Table rows={cats.data?.length ?? 0} cols={["Category", "Status", ""]}>
          {(cats.data ?? []).map((c) => <tr key={c.id}><td>{c.name}</td><td><Chip tone={c.active ? "ok" : "muted"}>{c.active ? "Active" : "Inactive"}</Chip></td><td>{edit && <button className="btn small" onClick={() => saveCat({ ...c, active: !c.active })}>{c.active ? "Deactivate" : "Activate"}</button>}</td></tr>)}
        </Table>
        {edit && <div className="actions" style={{ marginTop: 10 }}><input value={cat} onChange={(e) => setCat(e.target.value)} placeholder="New category" /><button className="btn small primary" disabled={!cat.trim()} onClick={() => saveCat({ name: cat.trim() })}>Add</button></div>}
      </Card>}

      {unit && <Modal open onClose={() => setUnit(null)} title={unit.id ? `Edit ${unit.name}` : "New unit"} footer={<><button className="btn primary" onClick={saveUnit}>Save</button><span className="spacer" /><button className="btn" onClick={() => setUnit(null)}>Cancel</button></>}>
        <div className="form"><div className="frow">
          <div className="field"><label>Code</label><input value={unit.code ?? ""} onChange={(e) => setUnit({ ...unit, code: e.target.value })} /></div>
          <div className="field"><label>Name</label><input value={unit.name ?? ""} onChange={(e) => setUnit({ ...unit, name: e.target.value })} /></div>
          <div className="field"><label>Kind</label><select value={unit.kind} onChange={(e) => setUnit({ ...unit, kind: e.target.value })}><option>Office</option><option>Wing</option><option>Section</option></select></div>
        </div><div className="frow">
          <div className="field"><label>Parent</label><select value={unit.parentId ?? ""} onChange={(e) => setUnit({ ...unit, parentId: e.target.value ? +e.target.value : null })}><option value="">— top level —</option>{(units.data ?? []).filter((u) => u.id !== unit.id).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}</select></div>
          <div className="field"><label>Head</label><UserSelect users={people.data ?? []} value={unit.headUserId} onChange={(v) => setUnit({ ...unit, headUserId: v })} /></div>
          <div className="field"><label>Effective from</label><input type="date" value={isoDate(unit.effectiveFrom)} onChange={(e) => setUnit({ ...unit, effectiveFrom: e.target.value })} /></div>
          <div className="field"><label>Effective to</label><input type="date" value={isoDate(unit.effectiveTo)} onChange={(e) => setUnit({ ...unit, effectiveTo: e.target.value || null })} /></div>
          <div className="field"><label>Status</label><select value={unit.active === false ? "0" : "1"} onChange={(e) => setUnit({ ...unit, active: e.target.value === "1" })}><option value="1">Active</option><option value="0">Inactive</option></select></div>
        </div></div>
      </Modal>}
      {des && <Modal open size="narrow" onClose={() => setDes(null)} title={des.id ? `Edit ${des.title}` : "New designation"} footer={<><button className="btn primary" onClick={saveDes}>Save</button><span className="spacer" /><button className="btn" onClick={() => setDes(null)}>Cancel</button></>}>
        <div className="form"><div className="frow">
          <div className="field"><label>Code</label><input value={des.code ?? ""} onChange={(e) => setDes({ ...des, code: e.target.value })} /></div>
          <div className="field"><label>Rank (1 = most senior)</label><input type="number" value={des.rank ?? 50} onChange={(e) => setDes({ ...des, rank: +e.target.value })} /></div>
        </div>
          <div className="field"><label>Title</label><input value={des.title ?? ""} onChange={(e) => setDes({ ...des, title: e.target.value })} /></div>
          <div className="field"><label>Status</label><select value={des.active === false ? "0" : "1"} onChange={(e) => setDes({ ...des, active: e.target.value === "1" })}><option value="1">Active</option><option value="0">Inactive</option></select></div>
        </div>
      </Modal>}
    </div>
  );
}
