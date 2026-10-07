import { installFetch, snapshotTakenAt } from "./mock/server";
import { installCamera, installGps, installImages, setGpsMode, type GpsMode } from "./mock/devices";
installFetch(); installGps(); installCamera();

import { StrictMode, useEffect, useState, type ComponentType } from "react";
import { createRoot } from "react-dom/client";
import "@/app/globals.css";
import "./demo.css";
import { AuthProvider, useAuth } from "@/lib/auth";
import { ToastProvider } from "@/components/ui";
import { Shell } from "@/components/Shell";
import { navigate, usePathname } from "./shims/next-navigation";
import { PhoneFrame } from "./phone";

import LoginPage from "@/app/login/page";
import Dashboard from "@/app/(app)/dashboard/page";
import Access from "@/app/(app)/access/page";
import Adjustments from "@/app/(app)/adjustments/page";
import Amendments from "@/app/(app)/amendments/page";
import Articles from "@/app/(app)/articles/page";
import Audit from "@/app/(app)/audit/page";
import Counts from "@/app/(app)/counts/page";
import Explore from "@/app/(app)/explore/page";
import Field from "@/app/(app)/field/page";
import Inward from "@/app/(app)/inward/page";
import Issue from "@/app/(app)/issue/page";
import MapPage from "@/app/(app)/map/page";
import MobileSetup from "@/app/(app)/mobile-setup/page";
import Notifications from "@/app/(app)/notifications/page";
import Parameters from "@/app/(app)/parameters/page";
import PurchaseOrders from "@/app/(app)/purchase-orders/page";
import Putaway from "@/app/(app)/putaway/page";
import Reports from "@/app/(app)/reports/page";
import Requisitions from "@/app/(app)/requisitions/page";
import Returns from "@/app/(app)/returns/page";
import Sites from "@/app/(app)/sites/page";
import Stock from "@/app/(app)/stock/page";
import Stores from "@/app/(app)/stores/page";
import Transfers from "@/app/(app)/transfers/page";
import Vendors from "@/app/(app)/vendors/page";

const PAGES: Record<string, ComponentType> = {
  dashboard: Dashboard, access: Access, adjustments: Adjustments, amendments: Amendments, articles: Articles, audit: Audit, counts: Counts, explore: Explore,
  field: Field, inward: Inward, issue: Issue, map: MapPage, "mobile-setup": MobileSetup, notifications: Notifications, parameters: Parameters,
  "purchase-orders": PurchaseOrders, putaway: Putaway, reports: Reports, requisitions: Requisitions, returns: Returns, sites: Sites, stock: Stock,
  stores: Stores, transfers: Transfers, vendors: Vendors,
};

function Router() {
  const path = usePathname();
  const { user, ready } = useAuth();
  useEffect(() => { if (ready && path === "/") navigate(user ? "/dashboard" : "/login", true); }, [ready, user, path]);
  if (path === "/login") return <LoginPage />;
  const Page = PAGES[path.replace(/^\//, "").split("/")[0]];
  if (!Page) return <div className="loading">Loading…</div>;
  return <Shell><Page key={path} /></Shell>;
}

function DemoBar() {
  const [phone, setPhone] = useState(() => { try { return localStorage.getItem("slims.demo.phone") === "1"; } catch { return false; } });
  const [gps, setGps] = useState<GpsMode>("site");
  const [msg, setMsg] = useState<string | null>(null);
  const [help, setHelp] = useState(false);
  useEffect(() => { try { localStorage.setItem("slims.demo.phone", phone ? "1" : "0"); } catch { /* private window */ } }, [phone]);
  useEffect(() => { (window as unknown as { __demoToast: (m: string) => void }).__demoToast = (m) => { setMsg(m); setTimeout(() => setMsg(null), 4000); }; }, []);
  const reset = async () => {
    if (!confirm("Start the demo again from the original data? Everything done in this tab is cleared.")) return;
    try { sessionStorage.clear(); for (const k of Object.keys(localStorage)) if (/^(slims|CapacitorStorage)/.test(k)) localStorage.removeItem(k); indexedDB.deleteDatabase("slims-field"); } catch { /* ignore */ }
    location.hash = "#/login"; location.reload();
  };
  return (
    <>
      <PhoneFrame open={phone} />
      <div className="demo-bar">
        <b>DEMO</b>
        <span className="demo-sub">Data snapshot of {new Date(snapshotTakenAt).toLocaleDateString("en-GB")} · changes stay in this tab</span>
        <button onClick={() => setPhone((p) => !p)} className={phone ? "on" : ""}>📱 SLIMS Field app</button>
        {phone && <label>Phone GPS <select value={gps} onChange={(e) => { const m = e.target.value as GpsMode; setGps(m); setGpsMode(m); }}>
          <option value="site">at the site</option><option value="away">900 m away (exception)</option><option value="mock">mock location app</option>
        </select></label>}
        <button onClick={() => setHelp((h) => !h)}>Guide</button>
        <button onClick={reset}>Reset</button>
      </div>
      {help && <div className="demo-help" onClick={() => setHelp(false)}>
        <h3>How to walk the demo</h3>
        <ol>
          <li><b>Sign in as anyone</b> from the list: each role sees its own menus and permissions. Sign out from the name at the top right to switch.</li>
          <li><b>Store:</b> <i>sk-cen</i> receives against a PO by quantity (Goods Receipt) and issues against approved requisitions (Issue to Site).</li>
          <li><b>Requisition:</b> <i>sup-central</i> raises one; <i>sic-cen</i> approves it (nobody can approve their own).</li>
          <li><b>Field:</b> open <b>📱 SLIMS Field app</b>, tap <i>Scan setup code</i> (the demo camera shows the code), do the checks and the setup photograph, then install an item from the work list with BEFORE and AFTER photographs. Set <i>Phone GPS</i> to "900 m away" to raise an exception, or "mock location" to see it refused.</li>
          <li><b>Supervisor</b> (<i>sup-central</i>) verifies exceptions in Field Installation; <b>PD</b> (<i>pd</i>) sees the dashboard, map, explore drill-down and reports.</li>
          <li>Settings ▸ Parameters, Users &amp; Access, Mobile Setup and the audit trail work as on the live system. E-mail/SMS are not sent from the demo.</li>
        </ol>
        <p>Click anywhere to close.</p>
      </div>}
      {msg && <div className="demo-toast">{msg}</div>}
    </>
  );
}

function Root() {
  useEffect(() => { installImages(); }, []);
  return (
    <AuthProvider>
      <ToastProvider>
        <Router />
        <DemoBar />
      </ToastProvider>
    </AuthProvider>
  );
}

if (!location.hash) location.hash = "#/";
createRoot(document.getElementById("root")!).render(<StrictMode><Root /></StrictMode>);
