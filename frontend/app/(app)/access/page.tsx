"use client";
import { useState } from "react";
import { get, post, put } from "@/lib/api";
import type { DeviceDto, RoleDto, SiteDto, StoreDto, UserDto, UserUpsertRequest } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { dateTime } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Notice, Table, errText, useAsync, useToast } from "@/components/ui";
import { openAuthed } from "@/components/Controls";

/** FR-SEC-009: phones bound to users. A replacement phone waits here for re-authorisation. */
function DevicesCard() {
  const toast = useToast();
  const list = useAsync(() => get<DeviceDto[]>("/api/mobile/devices"), []);
  const act = async (d: DeviceDto, path: "approve" | "revoke") => {
    if (path === "revoke" && !confirm(`Revoke ${d.userName}'s ${d.manufacturer ?? ""} ${d.model ?? "phone"}?`)) return;
    try { await post(`/api/mobile/devices/${d.id}/${path}`, path === "revoke" ? { reason: "Revoked by the administrator" } : {}); toast(path === "approve" ? "Phone approved; the previous one is retired" : "Phone revoked"); list.reload(); }
    catch (e) { toast(errText(e), true); }
  };
  const tone = (s: string) => s === "Active" ? "ok" : s === "AwaitingApproval" || s === "Setup" ? "watch" : "muted";
  return (
    <Card flush title="Field phones" sub="Evidence is accepted only from a user's active phone. A second phone needs your approval (FR-SEC-009).">
      {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
        <Table rows={list.data!.length} empty="No phones set up yet. Officers start from Settings ▸ Mobile Setup." cols={["User", "Phone", "Status", "Setup photo", "Last seen", ""]}>
          {list.data!.map((d) => (
            <tr key={d.id}>
              <td><div className="two-line"><b>{d.userName}</b><small>{d.roleLabel}</small></div></td>
              <td><div className="two-line"><b>{d.manufacturer} {d.model}</b><small>Android {d.osVersion ?? "?"} · app {d.appVersion ?? "?"}</small></div></td>
              <td><Chip tone={tone(d.status)}>{d.status === "AwaitingApproval" ? "Awaiting approval" : d.status}</Chip>{d.note && <div className="tiny">{d.note}</div>}</td>
              <td className="tiny">{d.setupCompletedAt ? <button className="linky" style={{ background: "none", border: 0, padding: 0, cursor: "pointer" }} onClick={() => d.setupPhotoUrl && openAuthed(d.setupPhotoUrl)}>{dateTime(d.setupCompletedAt)}</button> : "—"}</td>
              <td className="tiny">{d.lastSeenAt ? dateTime(d.lastSeenAt) : "—"}</td>
              <td><div className="actions">
                {d.status === "AwaitingApproval" && <button className="btn small primary" onClick={() => act(d, "approve")}>Approve</button>}
                {["Active", "Setup", "AwaitingApproval"].includes(d.status) && <button className="btn small" onClick={() => act(d, "revoke")}>Revoke</button>}
              </div></td>
            </tr>
          ))}
        </Table>
      )}
    </Card>
  );
}

export default function AccessPage() {
  const { can, config } = useAuth();
  const [edit, setEdit] = useState<UserDto | "new" | null>(null);
  const users = useAsync(() => get<UserDto[]>("/api/users"), []);
  const roles = useAsync(() => get<RoleDto[]>("/api/users/roles"), []);
  if (!can("configure")) return <Notice kind="crit">Only the System Administrator manages users and access.</Notice>;

  return (
    <>
      <div className="filters">
        <span className="spacer" />
        <button className="btn primary" onClick={() => setEdit("new")}>New user</button>
      </div>

      <Notice kind="info"><span>
        Access is enforced in the API, not by hiding buttons: permission first, then the zone, stores and sites assigned to the account.
        Sign-in is individual — shared accounts are not permitted (FR-SE-01).
      </span></Notice>

      <Card flush title="Users" sub="Role, scope and password state">
        {users.loading ? <Loading /> : users.error ? <ErrorBox error={users.error} /> : (
          <Table rows={users.data!.length} empty="No users." cols={["User ID", "Name", "Role", "Scope", "Password", "Last sign-in", "Status", ""]}>
            {users.data!.map((u) => (
              <tr key={u.id} className="row-link" onClick={() => setEdit(u)}>
                <td className="mono">{u.id}</td>
                <td><div className="two-line"><b>{u.name}</b><small>{u.designation ?? ""}</small></div></td>
                <td>{u.roleLabel}</td>
                <td className="tiny">{u.scopeDescription}</td>
                <td>{u.hasPassword ? (u.mustChangePassword ? <Chip tone="watch">temporary</Chip> : <Chip tone="ok">set</Chip>) : <Chip tone="crit">not issued</Chip>}
                  {u.lockedUntil && <div className="tiny err">locked to {dateTime(u.lockedUntil)}</div>}</td>
                <td className="tiny">{u.lastLogin ? dateTime(u.lastLogin) : "never"}</td>
                <td><Chip tone={u.active ? "ok" : "muted"}>{u.active ? "Active" : "Inactive"}</Chip></td>
                <td><span className="linky">Edit</span></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>

      <DevicesCard />

      <Card flush title="Roles and permissions" sub="What each role may do; checked on the server for every write">
        {roles.loading ? <Loading /> : (
          <Table rows={roles.data?.length ?? 0} cols={["Role", "Key", "Permissions"]}>
            {(roles.data ?? []).map((r) => (
              <tr key={r.key}>
                <td><b>{r.label}</b></td>
                <td className="mono tiny">{r.key}</td>
                <td>{r.perms.map((p) => <span key={p} className="tag" style={{ marginRight: 4 }}>{p}</span>)}</td>
              </tr>
            ))}
          </Table>
        )}
      </Card>

      {edit && <UserModal u={edit === "new" ? null : edit} roles={roles.data ?? []} zones={config?.zones ?? []} onClose={() => setEdit(null)} onDone={() => { setEdit(null); users.reload(); }} />}
    </>
  );
}

function UserModal({ u, roles, zones, onClose, onDone }: { u: UserDto | null; roles: RoleDto[]; zones: string[]; onClose: () => void; onDone: () => void }) {
  const toast = useToast();
  const stores = useAsync(() => get<StoreDto[]>("/api/stores"), []);
  const sites = useAsync(() => get<SiteDto[]>("/api/sites"), []);
  const [f, setF] = useState<UserUpsertRequest>({
    id: u?.id ?? "", name: u?.name ?? "", email: u?.email ?? "", employeeId: u?.employeeId ?? "", designation: u?.designation ?? "", phone: u?.phone ?? "",
    roleKey: u?.roleKey ?? "officer", zone: u?.zone ?? "", storeIds: u?.storeIds ?? [], siteIds: u?.siteIds ?? [], active: u?.active ?? true,
  });
  const [pw, setPw] = useState("");
  const [busy, setBusy] = useState(false);
  const F = (k: keyof UserUpsertRequest) => ({ value: String(f[k] ?? ""), onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value }) });

  return (
    <Modal open onClose={onClose} title={u ? u.name : "New user"} sub={u ? u.id : "Individual account — no shared logins"}
      footer={<><button className="btn primary" disabled={busy || !f.id || !f.name} onClick={async () => {
        setBusy(true);
        try { await put("/api/users", f); toast("Saved"); onDone(); } catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Save</button>
        {u && <>
          <button className="btn" disabled={busy || pw.length < 8} onClick={async () => {
            try { await post(`/api/users/${u.id}/password`, { password: pw }); toast("Temporary password issued — the user must change it at next sign-in"); setPw(""); onDone(); }
            catch (e) { toast(errText(e), true); }
          }}>Issue temporary password</button>
          {u.lockedUntil && <button className="btn" onClick={async () => {
            try { await post(`/api/users/${u.id}/unlock`, undefined); toast("Account unlocked"); onDone(); } catch (e) { toast(errText(e), true); }
          }}>Unlock</button>}
        </>}
        <span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>User ID</label><input {...F("id")} disabled={!!u} placeholder="sk-cen" /></div>
          <div className="field"><label>Role</label>
            <select value={f.roleKey} onChange={(e) => setF({ ...f, roleKey: e.target.value })}>
              {roles.map((r) => <option key={r.key} value={r.key}>{r.label}</option>)}
            </select></div>
          <div className="field"><label>Status</label>
            <select value={f.active ? "1" : "0"} onChange={(e) => setF({ ...f, active: e.target.value === "1" })}>
              <option value="1">Active</option><option value="0">Inactive</option>
            </select></div>
        </div>
        <div className="frow">
          <div className="field"><label>Full name</label><input {...F("name")} /></div>
          <div className="field"><label>Designation</label><input {...F("designation")} /></div>
          <div className="field"><label>Employee ID</label><input {...F("employeeId")} /></div>
        </div>
        <div className="frow">
          <div className="field"><label>Official email</label><input type="email" {...F("email")} /></div>
          <div className="field"><label>Mobile</label><input {...F("phone")} /></div>
          <div className="field"><label>Zone</label>
            <select value={f.zone ?? ""} onChange={(e) => setF({ ...f, zone: e.target.value })}>
              <option value="">All zones</option>{zones.map((z) => <option key={z}>{z}</option>)}
            </select></div>
        </div>

        <div className="fsec">
          <h4>Narrow the scope further (optional)</h4>
          <div className="frow">
            <div className="field"><label>Stores</label>
              <select multiple size={4} value={(f.storeIds ?? []).map(String)}
                onChange={(e) => setF({ ...f, storeIds: Array.from(e.target.selectedOptions).map((o) => +o.value) })}>
                {(stores.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
              </select>
              <span className="hint">Nothing selected = every store in the zone</span></div>
            <div className="field"><label>Sites</label>
              <select multiple size={4} value={(f.siteIds ?? []).map(String)}
                onChange={(e) => setF({ ...f, siteIds: Array.from(e.target.selectedOptions).map((o) => +o.value) })}>
                {(sites.data ?? []).map((s) => <option key={s.id} value={s.id}>{s.code} — {s.name}</option>)}
              </select>
              <span className="hint">Nothing selected = every site in the zone</span></div>
          </div>
        </div>

        {u && <div className="fsec">
          <h4>Password</h4>
          <Notice kind="info"><span>
            Passwords are stored as PBKDF2-SHA256 with 120,000 iterations. Issuing a temporary password forces a change at next sign-in
            and ends any active session.
          </span></Notice>
          <div className="field"><label>Temporary password</label>
            <input type="text" value={pw} onChange={(e) => setPw(e.target.value)} placeholder="min 8 characters, letters and digits" /></div>
        </div>}
      </div>
    </Modal>
  );
}
