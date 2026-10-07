// Rupee amounts with thousands separators, dates dd-mm-yyyy — the project's reporting conventions.
export const money = (v: number | null | undefined, dp = 0) => v === null || v === undefined || Number.isNaN(+v) ? "—" : (+v).toLocaleString("en-PK", { minimumFractionDigits: dp, maximumFractionDigits: dp });
export const qty = (v: number | null | undefined) => v === null || v === undefined || Number.isNaN(+v) ? "—" : (+v).toLocaleString("en-PK", { maximumFractionDigits: 3 });
export const pct = (v: number | null | undefined) => v === null || v === undefined || Number.isNaN(+v) ? "—" : `${(+v).toFixed(1)}%`;
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
  const m = Math.floor(ms / 60000); if (m < 60) return `${m} min ago`;
  const h = Math.floor(m / 60); if (h < 24) return `${h} h ago`;
  const d = Math.floor(h / 24); return d < 30 ? `${d} d ago` : date(iso);
};
export const today = () => new Date().toISOString().slice(0, 10);
export const coords = (lat?: number | null, lng?: number | null) => lat == null || lng == null ? "—" : `${lat.toFixed(6)}, ${lng.toFixed(6)}`;

/** Item status → chip class. Matches the palette in globals.css. */
export const statusTone = (s: string): string => {
  switch (s) {
    case "Installed": return "ok";
    case "Received": case "Reserved": return "info";
    case "Issued": case "AtSite": return "watch";
    case "PendingVerification": return "watch";
    case "OutOfOrder": case "Dismantled": case "Returned": return "crit";
    case "UnderRepair": case "WarrantyClaim": return "watch";
    case "Scrap": case "Disposed": case "Rejected": case "Cancelled": return "muted";
    default: return "";
  }
};
export const statusLabel = (s: string) => s.replace(/([a-z])([A-Z])/g, "$1 $2");
export const geoTone = (s: string) => s === "Confirmed" ? "ok" : s === "ApprovedException" ? "watch" : s === "Rejected" ? "muted" : "crit";
export const reqTone = (s: string) => s === "Approved" || s === "Issued" ? "ok" : s === "Submitted" ? "watch" : s === "Rejected" || s === "Cancelled" ? "muted" : s === "PartIssued" ? "info" : "";
