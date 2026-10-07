import { Preferences } from "@capacitor/preferences";

export const APP_VERSION = "1.0.0";

// ---- DTOs (mirror backend/src/Slims.Application/Dtos) -------------------------------------------------------
export interface Device { id: string; userId: string; userName: string; roleLabel: string; model?: string; manufacturer?: string; status: string; setupCompletedAt?: string | null; lastSeenAt?: string | null }
export interface User { id: string; name: string; roleKey: string; roleLabel: string; permissions: Record<string, boolean> }
export interface Geo { defaultGeofenceM: number; toleranceM: number; minAccuracyM: number; maxClockDriftMinutes: number }
export interface FieldCfg { mobileSetupRequired: boolean; enrolmentMinutes: number; setupMaxAccuracyM: number; offlineRetentionDays: number }
export interface Session { accessToken: string; expiresAt: string; user: User; device: Device; geo: Geo; field: FieldCfg; serverTime: string }
export interface Site { id: number; code: string; name: string; zone: string; lat: number; lng: number; geofenceM: number; status: string }
export interface Item { id: string; qrCode: string; itemCode: string; description: string; serial: number; status: string; siteId?: number | null; siteCode?: string | null; siteName?: string | null; qty: number; uom: string }
export interface Evidence { url: string; stampedUrl?: string | null; sha256?: string | null }
export interface Installation { id: number; number: string; qrCode: string; itemCode: string; siteId: number; siteCode: string; siteName: string; siteLat: number; siteLng: number; geofenceM: number; geoStatus: string; distanceM: number; justification?: string | null; officer: string; capturedAt: string; fvRequestNote?: string | null; fvRequestedBy?: string | null; itemId: string }
export interface Issue { id: number; number: string; gatePassNo: string; siteId: number; siteCode: string; siteName: string; storeCode: string; issuedAt: string; itemCount: number }
export interface Bootstrap { user: User; device: Device; geo: Geo; field: FieldCfg; sites: Site[]; workList: Item[]; toAcknowledge: Issue[]; recapture: Installation[]; verificationTasks: Installation[]; failureReasons: string[]; unread: number; serverTime: string }
export interface SetupCheck { ok: boolean; checks: { check: string; ok: boolean; detail: string }[]; device: Device }
export interface Photo { base64: string; sha256: string; lat: number; lng: number; accuracyM: number; capturedAt: string; isMock: boolean }
export interface Notice { id: number; code: string; title: string; body: string; createdAt: string; readAt?: string | null; level: number }

export class ApiError extends Error {
  status: number; step?: string | null;
  constructor(status: number, message: string, step?: string | null) { super(message); this.status = status; this.step = step; }
}

// ---- stored pairing -------------------------------------------------------------------------------------------
let server = ""; let deviceToken = ""; let access: { token: string; until: number } | null = null; let last: Session | null = null;

export async function loadPairing() {
  server = (await Preferences.get({ key: "server" })).value ?? "";
  deviceToken = (await Preferences.get({ key: "deviceToken" })).value ?? "";
  return { server, paired: !!(server && deviceToken) };
}
export async function savePairing(s: string, token: string) {
  server = s.replace(/\/+$/, ""); deviceToken = token; access = null;
  await Preferences.set({ key: "server", value: server }); await Preferences.set({ key: "deviceToken", value: token });
}
export async function forgetPairing() {
  server = ""; deviceToken = ""; access = null; last = null;
  await Preferences.remove({ key: "server" }); await Preferences.remove({ key: "deviceToken" });
}
export const serverUrl = () => server;
export const lastSession = () => last;

async function raw(method: string, url: string, body?: unknown, token?: string): Promise<unknown> {
  const headers: Record<string, string> = { Accept: "application/json" };
  if (body !== undefined) headers["Content-Type"] = "application/json";
  if (token) headers.Authorization = "Bearer " + token;
  let r: Response;
  try { r = await fetch(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) }); }
  catch { throw new ApiError(0, "No connection to the SLIMS server."); }
  const text = await r.text();
  let data: unknown = null; try { data = text ? JSON.parse(text) : null; } catch { data = null; }
  if (!r.ok) { const e = data as { error?: string; step?: string } | null; throw new ApiError(r.status, e?.error || `Server answered ${r.status}`, e?.step); }
  return data;
}

/** Called by the setup screen with what the setup QR contained. */
export async function enrol(s: string, code: string, info: { uid: string; model: string; manufacturer: string; osVersion: string }) {
  const base = s.replace(/\/+$/, "");
  const r = await raw("POST", base + "/api/mobile/enrol", { code, deviceUid: info.uid, model: info.model, manufacturer: info.manufacturer, osVersion: info.osVersion, appVersion: APP_VERSION }) as { deviceToken: string; userName: string; status: string; replacesAnotherPhone: boolean };
  await savePairing(base, r.deviceToken);
  return r;
}

/** Exchanges the device token for a short-lived access token bound to this phone. */
export async function session(force = false): Promise<Session> {
  if (!force && access && access.until > Date.now() + 60_000 && last) return last;
  const s = await raw("POST", server + "/api/mobile/session", { deviceToken, appVersion: APP_VERSION }) as Session;
  access = { token: s.accessToken, until: new Date(s.expiresAt).getTime() }; last = s;
  return s;
}

export async function api<T>(method: string, path: string, body?: unknown): Promise<T> {
  await session();
  try { return await raw(method, server + path, body, access!.token) as T; }
  catch (e) {
    if (e instanceof ApiError && e.status === 401) { await session(true); return await raw(method, server + path, body, access!.token) as T; }
    throw e;
  }
}
export const imageUrl = (path: string) => server + path;
