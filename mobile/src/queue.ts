import { ApiError, api, type Installation, type Photo } from "./api";

/** A photograph kept on the phone until it is synchronised: the JPEG bytes and the fix taken at the moment of capture. */
export interface Shot { bytes: ArrayBuffer; sha256: string; lat: number; lng: number; accuracyM: number; capturedAt: string; isMock: boolean }
/** One installation waiting to go to the server (FR-FLD-012). The client transaction id makes a repeated
 * synchronisation return the first result instead of creating a second installation (FR-FLD-013). */
export interface Queued {
  clientTxnId: string; qr: string; itemCode: string; serial: number; siteCode: string; poleId: string; remarks: string; replacesQr: string | null;
  before: Shot; after: Shot; queuedAt: string; queuedOffline: boolean; status: "queued" | "failed"; error?: string; attempts: number;
}
export interface Synced { clientTxnId: string; number: string; itemCode: string; serial: number; siteCode: string; geoStatus: string; distanceM: number; syncedAt: string; capturedAt: string }

const DB = "slims-field", VERSION = 1;
function open(): Promise<IDBDatabase> {
  return new Promise((res, rej) => {
    const r = indexedDB.open(DB, VERSION);
    r.onupgradeneeded = () => { const d = r.result; d.createObjectStore("queue", { keyPath: "clientTxnId" }); d.createObjectStore("done", { keyPath: "clientTxnId" }); d.createObjectStore("cache"); };
    r.onsuccess = () => res(r.result); r.onerror = () => rej(r.error);
  });
}
async function tx<T>(store: string, mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T>): Promise<T> {
  const d = await open();
  return new Promise((res, rej) => { const t = d.transaction(store, mode); const q = fn(t.objectStore(store)); q.onsuccess = () => res(q.result); q.onerror = () => rej(q.error); });
}
export const queued = () => tx<Queued[]>("queue", "readonly", (s) => s.getAll() as IDBRequest<Queued[]>);
export const history = async () => (await tx<Synced[]>("done", "readonly", (s) => s.getAll() as IDBRequest<Synced[]>)).sort((a, b) => b.syncedAt.localeCompare(a.syncedAt)).slice(0, 60);
export const enqueue = (q: Queued) => tx("queue", "readwrite", (s) => s.put(q));
export const discard = (id: string) => tx("queue", "readwrite", (s) => s.delete(id));
export const cachePut = (key: string, v: unknown) => tx("cache", "readwrite", (s) => s.put(v, key));
export const cacheGet = <T,>(key: string) => tx<T | undefined>("cache", "readonly", (s) => s.get(key) as IDBRequest<T | undefined>);

export async function sha256Hex(buf: ArrayBuffer) {
  const h = await crypto.subtle.digest("SHA-256", buf);
  return Array.from(new Uint8Array(h)).map((b) => b.toString(16).padStart(2, "0")).join("");
}
export function toBase64(buf: ArrayBuffer) {
  const bytes = new Uint8Array(buf); let s = "";
  for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  return btoa(s);
}
export const toPhoto = (s: Shot): Photo => ({ base64: toBase64(s.bytes), sha256: s.sha256, lat: s.lat, lng: s.lng, accuracyM: s.accuracyM, capturedAt: s.capturedAt, isMock: s.isMock });

let running: Promise<{ sent: number; failed: number; offline: boolean }> | null = null;
/** Sends every queued installation. Business refusals are kept as "failed" with the server's reason; network errors stop the
 * run and leave the rest queued for the next attempt. */
export function syncNow(): Promise<{ sent: number; failed: number; offline: boolean }> {
  if (running) return running;
  running = (async () => {
    let sent = 0, failed = 0;
    for (const q of (await queued()).filter((x) => x.status === "queued").sort((a, b) => a.queuedAt.localeCompare(b.queuedAt))) {
      try {
        const late = Date.now() - new Date(q.after.capturedAt).getTime() > 5 * 60_000;
        const r = await api<Installation>("POST", "/api/mobile/installations", {
          clientTxnId: q.clientTxnId, qr: q.qr, poleId: q.poleId || null, remarks: q.remarks || null, replacesQr: q.replacesQr, offline: q.queuedOffline || late,
          queuedAt: q.queuedAt, deviceTime: new Date().toISOString(), before: toPhoto(q.before), after: toPhoto(q.after),
        });
        await tx("done", "readwrite", (s) => s.put({ clientTxnId: q.clientTxnId, number: r.number, itemCode: q.itemCode, serial: q.serial, siteCode: q.siteCode, geoStatus: r.geoStatus, distanceM: r.distanceM, syncedAt: new Date().toISOString(), capturedAt: q.after.capturedAt } satisfies Synced));
        await discard(q.clientTxnId); sent++;
      } catch (e) {
        if (e instanceof ApiError && e.status === 0) return { sent, failed, offline: true };
        if (e instanceof ApiError && [400, 403, 404, 409, 413, 422].includes(e.status)) { await enqueue({ ...q, status: "failed", error: e.message, attempts: q.attempts + 1 }); failed++; }
        else { await enqueue({ ...q, attempts: q.attempts + 1, error: e instanceof Error ? e.message : String(e) }); }
      }
    }
    return { sent, failed, offline: false };
  })().finally(() => { running = null; });
  return running;
}
