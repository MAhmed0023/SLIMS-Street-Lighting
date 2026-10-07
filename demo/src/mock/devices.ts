/* eslint-disable @typescript-eslint/no-explicit-any */
// Demo stand-ins for what a real deployment gets from the server and the phone's hardware:
//  - <img src="/api/..."> (QR labels, evidence photographs) resolved in the browser
//  - a camera that shows a street pole with a SLIMS label, so scanning and photographing work anywhere
//  - a GPS fix next to the site being worked on, which the demo bar can move away or mark as "mock"
import QRCode from "qrcode";
import { fileUrl } from "./server";

const W = window as any;

// ---------------------------------------------------------------- images and file links
const qrCache = new Map<string, string>();
async function resolveImg(img: HTMLImageElement) {
  const src = img.getAttribute("src") ?? "";
  if (!src.startsWith("/api/")) return;
  const u = new URL(src, "http://demo");
  if (u.pathname === "/api/items/qr.png") {
    const payload = u.searchParams.get("payload") ?? ""; const size = Number(u.searchParams.get("size") ?? 5);
    const key = payload + "|" + size;
    if (!qrCache.has(key)) qrCache.set(key, await QRCode.toDataURL(payload, { margin: 1, scale: Math.max(2, size), errorCorrectionLevel: "M" }));
    img.src = qrCache.get(key)!;
  } else if (u.pathname.startsWith("/api/files/")) {
    const url = fileUrl(u.pathname + u.search) ?? fileUrl(u.pathname);
    if (url) img.src = url;
  }
}
export function installImages() {
  const scan = (root: ParentNode) => root.querySelectorAll?.("img[src^='/api/']").forEach((i) => resolveImg(i as HTMLImageElement));
  new MutationObserver((ms) => {
    for (const m of ms) {
      if (m.type === "attributes" && m.target instanceof HTMLImageElement) resolveImg(m.target);
      m.addedNodes.forEach((n) => { if (n instanceof HTMLImageElement) resolveImg(n); else if (n instanceof Element) scan(n); });
    }
  }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ["src"] });
  scan(document);
  // links straight to the API (APK download, raw files) cannot be followed from a file on disk
  document.addEventListener("click", (e) => {
    const a = (e.target as Element)?.closest?.("a[href^='/api/']") as HTMLAnchorElement | null;
    if (!a) return;
    e.preventDefault();
    const href = a.getAttribute("href")!;
    const url = fileUrl(href);
    if (url) window.open(url, "_blank"); else W.__demoToast?.("On the live server this link downloads " + href.split("/").pop());
  }, true);
}

// ---------------------------------------------------------------- GPS
export type GpsMode = "site" | "away" | "mock";
W.__demoGps = { mode: "site" as GpsMode, lat: 31.5546, lng: 74.3572 };
export function setGpsTarget(lat: number, lng: number) { W.__demoGps.lat = lat; W.__demoGps.lng = lng; }
export function setGpsMode(m: GpsMode) { W.__demoGps.mode = m; W.__demoMock = m === "mock"; }
function fix(): GeolocationPosition {
  const g = W.__demoGps; const away = g.mode === "away" ? 0.0085 : 0;       // ≈ 900 m north-east
  const j = () => (Math.random() - 0.5) * 0.00012;
  const coords = { latitude: g.lat + away + j(), longitude: g.lng + away + j(), accuracy: 4 + Math.random() * 4, altitude: null, altitudeAccuracy: null, heading: null, speed: null };
  return { coords: { ...coords, toJSON: () => coords }, timestamp: Date.now(), toJSON: () => ({ coords, timestamp: Date.now() }) } as unknown as GeolocationPosition;
}
export function installGps() {
  const watches = new Map<number, number>(); let n = 1;
  const geo = {
    getCurrentPosition: (ok: PositionCallback) => setTimeout(() => ok(fix()), 300),
    watchPosition: (ok: PositionCallback) => { const id = n++; ok(fix()); watches.set(id, window.setInterval(() => ok(fix()), 1500)); return id; },
    clearWatch: (id: number) => { clearInterval(watches.get(id)); watches.delete(id); },
  };
  Object.defineProperty(navigator, "geolocation", { value: geo, configurable: true });
}

// ---------------------------------------------------------------- camera
W.__demoCameraQr = W.__demoCameraQr ?? "";
export function installCamera() {
  const canvas = document.createElement("canvas"); canvas.width = 960; canvas.height = 720;
  const ctx = canvas.getContext("2d")!;
  const qrCanvas = document.createElement("canvas"); let lastQr = "";
  let t = 0; let timer = 0;
  const draw = async () => {
    t++;
    const w = canvas.width, h = canvas.height;
    const sky = ctx.createLinearGradient(0, 0, 0, h * 0.6); sky.addColorStop(0, "#7fb0dc"); sky.addColorStop(1, "#d8e6f0");
    ctx.fillStyle = sky; ctx.fillRect(0, 0, w, h);
    ctx.fillStyle = "#6c6c72"; ctx.beginPath(); ctx.moveTo(0, h); ctx.lineTo(w, h); ctx.lineTo(w * 0.62, h * 0.55); ctx.lineTo(w * 0.38, h * 0.55); ctx.fill();
    ctx.fillStyle = "#6f9a5c"; ctx.fillRect(0, h * 0.55, w * 0.36, h * 0.08);
    for (let k = 0; k < 7; k++) { ctx.fillStyle = `hsl(${30 + k * 9},20%,${58 + (k % 3) * 6}%)`; ctx.fillRect(w * 0.66 + k * 48, h * 0.55 - 90 - (k * 37) % 120, 44, 90 + (k * 37) % 120); }
    const sway = Math.sin(t / 9) * 2;
    ctx.fillStyle = "#8d8f96"; ctx.fillRect(w * 0.26 + sway, h * 0.12, 16, h * 0.74);
    ctx.fillRect(w * 0.26 + sway, h * 0.13, 170, 10);
    ctx.fillStyle = "#44464c"; ctx.fillRect(w * 0.26 + 150 + sway, h * 0.12, 80, 24);
    // the SLIMS label on the pole: a QR the app can read
    const qr = W.__demoCameraQr as string;
    if (qr) {
      if (qr !== lastQr) { lastQr = qr; await QRCode.toCanvas(qrCanvas, qr, { margin: 2, width: 300, errorCorrectionLevel: "M" }); }
      ctx.fillStyle = "#fff"; ctx.fillRect(w * 0.44 - 8, h * 0.2 - 8, 316, 346);
      ctx.drawImage(qrCanvas, w * 0.44, h * 0.2);
      ctx.fillStyle = "#333"; ctx.font = "bold 18px system-ui"; ctx.fillText(qr.startsWith("{") ? "SLIMS set-up code" : "SLIMS · " + (qr.split("|")[1] ?? ""), w * 0.44, h * 0.2 + 326);
    }
    ctx.fillStyle = "rgba(0,0,0,.45)"; ctx.fillRect(0, h - 34, w, 34);
    ctx.fillStyle = "#fff"; ctx.font = "16px system-ui"; ctx.fillText("DEMO CAMERA · " + new Date().toLocaleTimeString(), 14, h - 11);
  };
  const start = () => { if (!timer) timer = window.setInterval(draw, 120); draw(); };
  const md = navigator.mediaDevices ?? ({} as MediaDevices);
  const fake = async () => { start(); return canvas.captureStream(10); };
  try { Object.defineProperty(md, "getUserMedia", { value: fake, configurable: true }); } catch { (md as any).getUserMedia = fake; }
  if (!navigator.mediaDevices) Object.defineProperty(navigator, "mediaDevices", { value: md, configurable: true });
}
