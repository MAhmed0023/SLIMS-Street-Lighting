"use client";
import { useState } from "react";
import { post } from "@/lib/api";
import { useAuth } from "@/lib/auth";
import { errText, useToast } from "./ui";

/** Change-password panel (also the forced change after a temporary password). */
export function PasswordDialog({ open, onClose, forced }: { open: boolean; onClose: () => void; forced?: boolean }) {
  const { user, reload } = useAuth();
  const toast = useToast();
  const [old, setOld] = useState(""); const [pw, setPw] = useState(""); const [pw2, setPw2] = useState(""); const [msg, setMsg] = useState("");
  if (!open || !user) return null;
  const needOld = user.hasPassword && !user.mustChangePassword && !forced;
  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setMsg("");
    if (pw !== pw2) { setMsg("The two passwords do not match."); return; }
    try {
      await post("/api/auth/change-password", { old: needOld ? old : null, new: pw });
      toast("Password saved"); setOld(""); setPw(""); setPw2(""); await reload(); onClose();
    } catch (err) { setMsg(errText(err)); }
  };
  return (
    <div className="login-wrap"><div className="login-card">
      <div className="lrow"><label>{forced || !user.hasPassword ? "Set a new password" : "Change password"}</label><span className="tiny">{user.name}</span></div>
      {forced && <div className="notice info">A temporary password was issued to this account. Choose your own password to continue.</div>}
      {msg && <div className="notice crit">{msg}</div>}
      <form onSubmit={submit} autoComplete="on" className="form">
        {needOld && <div className="lfield"><div className="lrow"><label htmlFor="cp-old">Current password</label></div><input id="cp-old" type="password" autoComplete="current-password" value={old} onChange={(e) => setOld(e.target.value)} required /></div>}
        <div className="lfield"><div className="lrow"><label htmlFor="cp-new">New password</label><span className="tiny">min 8, letters + digits</span></div><input id="cp-new" type="password" autoComplete="new-password" value={pw} onChange={(e) => setPw(e.target.value)} required /></div>
        <div className="lfield"><div className="lrow"><label htmlFor="cp-2">Repeat new password</label></div><input id="cp-2" type="password" autoComplete="new-password" value={pw2} onChange={(e) => setPw2(e.target.value)} required /></div>
        <button type="submit" className="lbtn">Save password</button>
        {!forced && <div className="lcenter"><button type="button" className="linky" onClick={onClose}>Cancel</button></div>}
      </form>
    </div></div>
  );
}
