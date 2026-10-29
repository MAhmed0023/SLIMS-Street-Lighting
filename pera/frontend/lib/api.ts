"use client";
import type { TokenResponse } from "./types";

// Access token lives in memory (and sessionStorage for reloads); the refresh token is an httpOnly cookie
// scoped to /api/auth that the browser sends on /api/auth/refresh.
let accessToken: string | null = null;
let refreshing: Promise<boolean> | null = null;

export function getToken() {
  if (accessToken) return accessToken;
  try { accessToken = sessionStorage.getItem("pera.token"); } catch { /* ignore */ }
  return accessToken;
}
export function setToken(t: string | null) {
  accessToken = t;
  try { if (t) sessionStorage.setItem("pera.token", t); else sessionStorage.removeItem("pera.token"); } catch { /* ignore */ }
}

export class ApiError extends Error {
  status: number; step?: string | null; detail?: unknown;
  constructor(status: number, message: string, step?: string | null, detail?: unknown) { super(message); this.status = status; this.step = step; this.detail = detail; }
}

async function refresh(): Promise<boolean> {
  if (!refreshing) {
    refreshing = (async () => {
      try {
        const r = await fetch("/api/auth/refresh", { method: "POST", credentials: "include" });
        if (!r.ok) { setToken(null); return false; }
        const t = (await r.json()) as TokenResponse; setToken(t.accessToken); return true;
      } catch { setToken(null); return false; } finally { setTimeout(() => { refreshing = null; }, 0); }
    })();
  }
  return refreshing;
}

export async function api<T>(method: string, url: string, body?: unknown, opts: { form?: FormData; retry?: boolean; raw?: boolean } = {}): Promise<T> {
  const headers: Record<string, string> = {};
  const token = getToken(); if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined && !opts.form) headers["content-type"] = "application/json";
  const r = await fetch(url, { method, headers, body: opts.form ?? (body !== undefined ? JSON.stringify(body) : undefined), credentials: "include" });
  if (r.status === 401 && opts.retry !== false && !url.startsWith("/api/auth/login") && !url.startsWith("/api/auth/refresh")) {
    if (await refresh()) return api<T>(method, url, body, { ...opts, retry: false });
  }
  if (opts.raw) return r as unknown as T;
  const text = await r.text();
  let data: unknown = null; try { data = text ? JSON.parse(text) : null; } catch { data = null; }
  if (!r.ok) {
    const e = data as { error?: string; step?: string; detail?: unknown } | null;
    throw new ApiError(r.status, e?.error || `${r.status} ${r.statusText}`, e?.step, e?.detail);
  }
  return data as T;
}

export const get = <T,>(url: string) => api<T>("GET", url);
export const post = <T,>(url: string, body?: unknown) => api<T>("POST", url, body);
export const put = <T,>(url: string, body?: unknown) => api<T>("PUT", url, body);
export const del = <T,>(url: string) => api<T>("DELETE", url);
export const upload = <T,>(url: string, form: FormData) => api<T>("POST", url, undefined, { form });

export function qs(params: Record<string, string | number | boolean | undefined | null>) {
  const p = new URLSearchParams();
  Object.entries(params).forEach(([k, v]) => { if (v !== undefined && v !== null && v !== "") p.set(k, String(v)); });
  const s = p.toString(); return s ? `?${s}` : "";
}

/** Files need the bearer token, which a plain link cannot send: fetch, then hand the browser an object URL. */
export async function fetchBlob(url: string): Promise<Blob> {
  const r = await api<Response>("GET", url, undefined, { raw: true });
  if (!r.ok) { let m = `${r.status}`; try { m = (await r.json()).error ?? m; } catch { /* not json */ } throw new ApiError(r.status, m); }
  return r.blob();
}
export async function openFile(url: string) {
  const w = window.open("", "_blank");
  try { const b = await fetchBlob(url); const u = URL.createObjectURL(b); if (w) w.location.href = u; else window.open(u, "_blank"); }
  catch (e) { w?.close(); throw e; }
}
export async function downloadFile(url: string, name: string) {
  const b = await fetchBlob(url);
  const u = URL.createObjectURL(b);
  const a = document.createElement("a"); a.href = u; a.download = name; document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(u), 10000);
}
