"use client";
import { useState } from "react";
import { get, post, put } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import type { DesignationDto, OrgUnitDto, RoleDto, UserDto } from "@/lib/types";
import { dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";

const blank = { id: "", name: "", email: "", employeeId: "", phone: "", roleKey: "officer", designationId: null as number | null, orgUnitId: null as number | null, reportsToId: null as string | null, active: true };

export default function AccessPage() {
  const toast = useToast();
  const { user: me } = useAuth();
  const users = useAsync(() => get<UserDto[]>("/api/users"), []);
  const roles = useAsync(() => get<RoleDto[]>("/api/users/roles"), []);
  const desig = useAsync(() => get<DesignationDto[]>("/api/org/designations"), []);
  const units = useAsync(() => get<OrgUnitDto[]>("/api/org/units"), []);
  const [edit, setEdit] = useState<typeof blank | null>(null); const [isNew, setIsNew] = useState(false);
  const [pw, setPw] = useState<UserDto | null>(null); const [temp, setTemp] = useState("");
  const [q, setQ] = useState("");
  const save = async () => {
    try { await put("/api/users", { ...edit, email: edit!.email || null, employeeId: edit!.employeeId || null, phone: edit!.phone || null }); toast("Saved"); setEdit(null); users.reload(); }
    catch (e) { toast(errText(e), true); }
  };
  const setPassword = async () => {
    try { await post(`/api/users/${pw!.id}/password`, { password: temp }); toast(`Temporary password issued to ${pw!.name}; they choose their own at first sign-in`); setPw(null); setTemp(""); users.reload(); }
    catch (e) { toast(errText(e), true); }
  };
  const unlock = async (u: UserDto) => { try { await post(`/api/users/${u.id}/unlock`); toast("Unlocked"); users.reload(); } catch (e) { toast(errText(e), true); } };
  const rows = (users.data ?? []).filter((u) => !q || (u.id + u.name + (u.designation ?? "") + (u.orgUnit ?? "")).toLowerCase().includes(q.toLowerCase()));
  return (
    <>
      <div className="filters">
        <input type="search" placeholder="Search users" value={q} onChange={(e) => setQ(e.target.value)} />
        <span className="spacer" /><button className="btn primary" onClick={() => { setEdit({ ...blank }); setIsNew(true); }}>New user</button>
      </div>
      <Notice kind="info"><span>The reporting officer decides default routing, who can see an officer&apos;s work and where overdue items escalate. Approval and signing power comes from the designation in the authority matrix, never from the role.</span></Notice>
      <Card flush title="Users" sub="Accounts are deactivated, never deleted, so history keeps the actor">
        {users.loading ? <Loading /> : users.error ? <ErrorBox error={users.error} /> : (
          <Table rows={rows.length} cols={["User", "Role", "Designation · unit", "Reports to", "Sign-in", "Status", ""]}>
            {rows.map((u) => (
              <tr key={u.id}>
                <td><div className="two-line"><b>{u.name}</b><small className="mono">{u.id}{u.email ? ` · ${u.email}` : ""}</small></div></td>
                <td>{u.roleLabel}</td>
                <td><div className="two-line"><b>{u.designation ?? "—"}</b><small>{u.orgUnit ?? "—"}</small></div></td>
                <td className="tiny">{u.reportsTo ?? "—"}</td>
                <td className="tiny">{u.hasPassword ? (u.mustChangePassword ? "temporary password" : `last ${dateTime(u.lastLogin)}`) : "no password"}{u.lockedUntil && new Date(u.lockedUntil) > new Date() && <div className="err">locked</div>}</td>
                <td><Chip tone={u.active ? "ok" : "muted"}>{u.active ? "Active" : "Inactive"}</Chip></td>
                <td><div className="row-actions">
                  <button className="btn small" onClick={() => { setEdit({ id: u.id, name: u.name, email: u.email ?? "", employeeId: u.employeeId ?? "", phone: u.phone ?? "", roleKey: u.roleKey, designationId: u.designationId, orgUnitId: u.orgUnitId, reportsToId: u.reportsToId, active: u.active }); setIsNew(false); }}>Edit</button>
                  <button className="btn small" onClick={() => setPw(u)}>Password</button>
                  {u.lockedUntil && new Date(u.lockedUntil) > new Date() && <button className="btn small" onClick={() => unlock(u)}>Unlock</button>}
                </div></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      {edit && <Modal open onClose={() => setEdit(null)} title={isNew ? "New user" : `Edit ${edit.name}`} footer={<><button className="btn primary" onClick={save}>Save</button><span className="spacer" /><button className="btn" onClick={() => setEdit(null)}>Cancel</button></>}>
        <div className="form">
          <div className="frow">
            <div className="field"><label>User ID</label><input value={edit.id} disabled={!isNew} onChange={(e) => setEdit({ ...edit, id: e.target.value })} /></div>
            <div className="field"><label>Name</label><input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></div>
            <div className="field"><label>Email</label><input value={edit.email} onChange={(e) => setEdit({ ...edit, email: e.target.value })} /></div>
          </div>
          <div className="frow">
            <div className="field"><label>Role</label><select value={edit.roleKey} onChange={(e) => setEdit({ ...edit, roleKey: e.target.value })}>{(roles.data ?? []).map((r) => <option key={r.key} value={r.key}>{r.label}</option>)}</select>
              <span className="hint">{roles.data?.find((r) => r.key === edit.roleKey)?.perms.join(", ")}</span></div>
            <div className="field"><label>Designation</label><select value={edit.designationId ?? ""} onChange={(e) => setEdit({ ...edit, designationId: e.target.value ? +e.target.value : null })}><option value="">—</option>{(desig.data ?? []).map((d) => <option key={d.id} value={d.id}>{d.title}</option>)}</select></div>
            <div className="field"><label>Office / wing / section</label><select value={edit.orgUnitId ?? ""} onChange={(e) => setEdit({ ...edit, orgUnitId: e.target.value ? +e.target.value : null })}><option value="">—</option>{(units.data ?? []).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}</select></div>
            <div className="field"><label>Reporting officer</label><select value={edit.reportsToId ?? ""} onChange={(e) => setEdit({ ...edit, reportsToId: e.target.value || null })}><option value="">— none (top of hierarchy) —</option>{(users.data ?? []).filter((u) => u.id !== edit.id && u.active).map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}</select></div>
          </div>
          <div className="frow">
            <div className="field"><label>Employee ID</label><input value={edit.employeeId} onChange={(e) => setEdit({ ...edit, employeeId: e.target.value })} /></div>
            <div className="field"><label>Phone</label><input value={edit.phone} onChange={(e) => setEdit({ ...edit, phone: e.target.value })} /></div>
            <div className="field"><label>Status</label><select value={edit.active ? "1" : "0"} disabled={edit.id === me?.id} onChange={(e) => setEdit({ ...edit, active: e.target.value === "1" })}><option value="1">Active</option><option value="0">Inactive</option></select></div>
          </div>
        </div>
      </Modal>}
      {pw && <Modal open onClose={() => setPw(null)} size="narrow" title={`Issue a temporary password — ${pw.name}`} footer={<><button className="btn primary" disabled={temp.length < 8} onClick={setPassword}>Issue</button><span className="spacer" /><button className="btn" onClick={() => setPw(null)}>Cancel</button></>}>
        <div className="field"><label>Temporary password (8+ characters, letters and digits)</label><input value={temp} onChange={(e) => setTemp(e.target.value)} autoComplete="off" /></div>
        <div className="tiny">The user must replace it at first sign-in. Issuing it is audit-logged.</div>
      </Modal>}
    </>
  );
}
