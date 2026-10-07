"use client";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/lib/auth";
import { get } from "@/lib/api";
import type { UserPublicDto } from "@/lib/types";
import { PasswordDialog } from "@/components/PasswordDialog";
import { errText } from "@/components/ui";

const EYE = <svg viewBox="0 0 24 24"><path d="M1.5 12S5.5 5 12 5s10.5 7 10.5 7-4 7-10.5 7S1.5 12 1.5 12z" /><circle cx="12" cy="12" r="3" /></svg>;
const EYE_OFF = <svg viewBox="0 0 24 24"><path d="M3 3l18 18M10.6 10.6A3 3 0 0013.4 13.4M9.9 5.2A10.9 10.9 0 0112 5c6.5 0 10.5 7 10.5 7a17.6 17.6 0 01-3.2 3.9M6.6 6.6C3.5 8.6 1.5 12 1.5 12s4 7 10.5 7c1.6 0 3-.3 4.3-.9" /></svg>;

export default function LoginPage() {
  const { user, meta, ready, login } = useAuth();
  const router = useRouter();
  const [panel, setPanel] = useState<"login" | "picker" | "change">("login");
  const [id, setId] = useState(""); const [pw, setPw] = useState(""); const [show, setShow] = useState(false);
  const [msg, setMsg] = useState(""); const [busy, setBusy] = useState(false);
  const [picker, setPicker] = useState<UserPublicDto[]>([]); const [pq, setPq] = useState("");

  useEffect(() => { if (ready && user && panel !== "change") router.replace("/dashboard"); }, [ready, user, router, panel]);
  useEffect(() => { if (panel === "picker" && picker.length === 0) get<UserPublicDto[]>("/api/auth/picker").then(setPicker).catch(() => setPicker([])); }, [panel, picker.length]);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true); setMsg("");
    try {
      const t = await login(id.trim(), pw);
      if (t.mustChangePassword) { setPanel("change"); return; }
      router.replace("/dashboard");
    } catch (err) { setMsg(errText(err)); setPw(""); } finally { setBusy(false); }
  };

  const card = (inner: React.ReactNode) => (
    <div className="login-wrap"><div className="login-card">
      <div className="login-logo">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src="/slims-logo.svg" alt="" />
        <div className="name">{meta?.app.name ?? "Street Lighting Store & Installation Manager"}</div>
        <div className="client">{meta?.app.client ?? "Government Street Lighting Project"}</div>
      </div>
      {inner}
      <div className="lfoot">Every scan, coordinate and photograph is time-stamped and audit-logged.</div>
    </div></div>
  );

  if (panel === "change" && user) return <PasswordDialog open forced onClose={() => router.replace("/dashboard")} />;

  if (panel === "picker") return card(<>
    <div className="lrow"><label>Choose a user</label><button type="button" className="linky" onClick={() => setPanel("login")}>← Back</button></div>
    <input type="search" placeholder="Search name, role, ID…" value={pq} onChange={(e) => setPq(e.target.value)} style={{ width: "100%" }} />
    <div className="lpicker">
      {Object.entries(picker.filter((u) => !pq || (u.name + u.id + u.roleLabel).toLowerCase().includes(pq.toLowerCase()))
        .reduce<Record<string, UserPublicDto[]>>((acc, u) => { (acc[u.roleLabel] ??= []).push(u); return acc; }, {}))
        .map(([g, arr]) => <div key={g}>
          <div className="grp">{g}</div>
          {arr.map((u) => <button key={u.id} type="button" onClick={() => { setId(u.id); setPanel("login"); }}>
            <span>{u.name}</span><small>{u.id}{!u.hasPassword ? " · no password issued" : ""}</small>
          </button>)}
        </div>)}
    </div>
    <div className="tiny lcenter">Picking a user only fills in the ID — the password is still required.</div>
  </>);

  return card(<>
    {meta?.firstRun && <div className="notice info">First run — no password exists yet. Sign in as <b>sysadmin</b>; the password you type becomes the administrator password.</div>}
    {msg && <div className="notice crit">{msg}</div>}
    <form onSubmit={submit} autoComplete="on" className="form">
      <div className="lfield">
        <div className="lrow"><label htmlFor="login-id">User ID or email</label>{meta?.showUserPicker !== false && <button type="button" className="linky" onClick={() => setPanel("picker")}>Choose a user</button>}</div>
        <input id="login-id" autoComplete="username" autoCapitalize="none" spellCheck={false} required placeholder="e.g. sk-cen" value={id} onChange={(e) => setId(e.target.value)} />
      </div>
      <div className="lfield">
        <div className="lrow"><label htmlFor="login-pw">Password</label></div>
        <div className="pwgroup">
          <input id="login-pw" type={show ? "text" : "password"} autoComplete="current-password" required={meta?.loginRequired !== false} placeholder={meta?.loginRequired === false ? "Not needed for accounts without a password" : "Enter your password"} value={pw} onChange={(e) => setPw(e.target.value)} />
          <button type="button" onClick={() => setShow((s) => !s)} aria-label="Show password">{show ? EYE_OFF : EYE}</button>
        </div>
      </div>
      <button type="submit" className="lbtn" disabled={busy}>{busy ? "Checking…" : "Log in"}</button>
    </form>
  </>);
}
