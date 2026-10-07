import { useSyncExternalStore } from "react";

// Hash routing for a file:// page: "#/requisitions?status=Approved" is the path "/requisitions" with its own query string.
const subscribe = (cb: () => void) => { window.addEventListener("hashchange", cb); return () => window.removeEventListener("hashchange", cb); };
const hash = () => window.location.hash.replace(/^#/, "") || "/";

export function currentPath() { return hash().split("?")[0] || "/"; }
export function currentSearch() { const h = hash(); const i = h.indexOf("?"); return i < 0 ? "" : h.slice(i); }
(window as unknown as { __slimsSearch: () => string }).__slimsSearch = currentSearch;

export function navigate(to: string, replace = false) {
  const target = "#" + (to.startsWith("/") ? to : "/" + to);
  if (replace) window.location.replace(target); else window.location.hash = target;
  window.scrollTo(0, 0);
}

export function usePathname() { return useSyncExternalStore(subscribe, currentPath, currentPath); }
export function useHashLocation() { return useSyncExternalStore(subscribe, hash, hash); }

const router = { push: (to: string) => navigate(to), replace: (to: string) => navigate(to, true), back: () => history.back(), refresh: () => {}, prefetch: () => {} };
export function useRouter() { return router; }
export function useSearchParams() { return new URLSearchParams(useHashLocation().split("?")[1] ?? ""); }
