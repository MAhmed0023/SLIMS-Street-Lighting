// Dates dd-mm-yyyy, the reporting convention used across PERA and SLIMS.
export const int = (v: number | null | undefined) => (+(v ?? 0)).toLocaleString("en-PK");
export const date = (iso: string | null | undefined) => {
  if (!iso) return "—";
  const d = new Date(iso); if (Number.isNaN(d.getTime())) return iso;
  return `${String(d.getDate()).padStart(2, "0")}-${String(d.getMonth() + 1).padStart(2, "0")}-${d.getFullYear()}`;
};
export const dateTime = (iso: string | null | undefined) => {
  if (!iso) return "—";
  const d = new Date(iso); if (Number.isNaN(d.getTime())) return iso;
  return `${date(iso)} ${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
};
export const ago = (iso: string | null | undefined) => {
  if (!iso) return "—";
  const ms = Date.now() - new Date(iso).getTime();
  const future = ms < 0; const a = Math.abs(ms);
  const m = Math.floor(a / 60000); const h = Math.floor(m / 60); const d = Math.floor(h / 24);
  const t = m < 60 ? `${m} min` : h < 48 ? `${h} h` : d < 60 ? `${d} d` : date(iso);
  return m < 60 && m < 1 ? "just now" : future ? `in ${t}` : `${t} ago`;
};
export const hours = (h: number | null | undefined) => h == null ? "—" : h < 48 ? `${h.toFixed(h < 10 ? 1 : 0)} h` : `${(h / 24).toFixed(1)} d`;
export const today = () => new Date().toISOString().slice(0, 10);
export const addDays = (n: number) => { const d = new Date(); d.setDate(d.getDate() + n); return d.toISOString().slice(0, 10); };
export const isoDate = (iso: string | null | undefined) => iso ? iso.slice(0, 10) : "";
export const bytes = (n: number) => n < 1024 ? `${n} B` : n < 1048576 ? `${(n / 1024).toFixed(0)} KB` : `${(n / 1048576).toFixed(1)} MB`;
export const words = (s: string) => s.replace(/([a-z])([A-Z])/g, "$1 $2");

export const taskLabel = (s: string) => s === "InProgress" ? "In Progress" : s === "SubmittedForReview" ? "Submitted for Review" : s;
export const taskTone = (s: string) => {
  switch (s) {
    case "Completed": return "ok";
    case "InProgress": case "Assigned": return "info";
    case "SubmittedForReview": return "plain";
    case "Blocked": case "Returned": return "crit";
    case "Cancelled": case "Draft": return "muted";
    default: return "";
  }
};
export const docLabel = (s: string) => s === "InRoute" ? "In route" : s === "Completed" ? "Completed / on record" : s;
export const docTone = (s: string) => s === "Completed" ? "ok" : s === "InRoute" ? "info" : s === "Returned" ? "watch" : s === "Rejected" ? "crit" : "muted";
export const stageTone = (s: string) => s === "Done" ? "ok" : s === "Active" ? "info" : s === "Returned" ? "watch" : s === "Rejected" ? "crit" : "muted";
export const priorityTone = (p: string) => p === "Urgent" ? "crit" : p === "High" ? "watch" : p === "Low" ? "muted" : "";
export const ACTION_LABEL: Record<string, string> = {
  Acknowledge: "Acknowledge", Comment: "Comment", Recommend: "Recommend / complete review", Concur: "Concur", NotConcur: "Do not concur", Approve: "Approve",
  Reject: "Reject", Return: "Return for revision", Sign: "Sign", Dispatch: "Dispatch", Record: "Place on record",
};
export const STAGE_TYPES = ["Information", "Review", "Concurrence", "Approval", "Signature", "Dispatch"];
