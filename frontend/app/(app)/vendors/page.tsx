"use client";
import { useState } from "react";
import { get, post, put } from "@/lib/api";
import type { VendorDto } from "@/lib/types";
import { useAuth } from "@/lib/auth";
import { int } from "@/lib/format";
import { Card, Chip, ErrorBox, Loading, Modal, Table, errText, useAsync, useToast } from "@/components/ui";

export default function VendorsPage() {
  const { can } = useAuth();
  const [edit, setEdit] = useState<VendorDto | "new" | null>(null);
  const list = useAsync(() => get<VendorDto[]>("/api/vendors"), []);
  return (
    <>
      <div className="filters">
        <span className="spacer" />
        {can("procure") && <button className="btn primary" onClick={() => setEdit("new")}>New vendor</button>}
      </div>
      <Card flush title="Vendors" sub="Supply and warranty counterparties; failure analysis is reported by vendor">
        {list.loading ? <Loading /> : list.error ? <ErrorBox error={list.error} /> : (
          <Table rows={list.data!.length} empty="No vendors yet." cols={["Code", "Name", "Contact", "Phone", "Email", { t: "POs", c: "r" }, "Status", ""]}>
            {list.data!.map((v) => (
              <tr key={v.id} className="row-link" onClick={() => setEdit(v)}>
                <td className="mono">{v.code}</td>
                <td><b>{v.name}</b></td>
                <td className="tiny">{v.contact ?? "—"}</td>
                <td className="tiny">{v.phone ?? "—"}</td>
                <td className="tiny">{v.email ?? "—"}</td>
                <td className="r num">{int(v.purchaseOrders)}</td>
                <td><Chip tone={v.active ? "ok" : "muted"}>{v.active ? "Active" : "Inactive"}</Chip></td>
                <td><span className="linky">Edit</span></td>
              </tr>
            ))}
          </Table>
        )}
      </Card>
      {edit && <VendorModal v={edit === "new" ? null : edit} onClose={() => setEdit(null)} onDone={() => { setEdit(null); list.reload(); }} canEdit={can("procure")} />}
    </>
  );
}

function VendorModal({ v, onClose, onDone, canEdit }: { v: VendorDto | null; onClose: () => void; onDone: () => void; canEdit: boolean }) {
  const toast = useToast();
  const [f, setF] = useState({ code: v?.code ?? "", name: v?.name ?? "", contact: v?.contact ?? "", phone: v?.phone ?? "", email: v?.email ?? "", active: v?.active ?? true });
  const [busy, setBusy] = useState(false);
  const F = (k: keyof typeof f) => ({ value: String(f[k] ?? ""), onChange: (e: React.ChangeEvent<HTMLInputElement>) => setF({ ...f, [k]: e.target.value }), disabled: !canEdit });
  return (
    <Modal open onClose={onClose} size="narrow" title={v ? v.name : "New vendor"}
      footer={<>{canEdit && <button className="btn primary" disabled={busy || !f.code || !f.name} onClick={async () => {
        setBusy(true);
        try { if (v) await put(`/api/vendors/${v.id}`, f); else await post("/api/vendors", f); toast("Saved"); onDone(); }
        catch (e) { toast(errText(e), true); } finally { setBusy(false); }
      }}>Save</button>}<span className="spacer" /><button className="btn" onClick={onClose}>Close</button></>}>
      <div className="form">
        <div className="frow">
          <div className="field"><label>Vendor code</label><input {...F("code")} placeholder="V0231" /></div>
          <div className="field"><label>Status</label>
            <select value={f.active ? "1" : "0"} onChange={(e) => setF({ ...f, active: e.target.value === "1" })} disabled={!canEdit}>
              <option value="1">Active</option><option value="0">Inactive</option>
            </select></div>
        </div>
        <div className="field"><label>Name</label><input {...F("name")} placeholder="Pak Lighting Industries (Pvt) Ltd" /></div>
        <div className="frow">
          <div className="field"><label>Contact person</label><input {...F("contact")} /></div>
          <div className="field"><label>Phone</label><input {...F("phone")} /></div>
        </div>
        <div className="field"><label>Email</label><input type="email" {...F("email")} /></div>
      </div>
    </Modal>
  );
}
