import { useEffect, useRef } from "react";
import { createRoot, type Root } from "react-dom/client";
import App from "@mobile/App";
import phoneCss from "@mobile/styles.css?inline";

// The real SLIMS Field app, mounted in a shadow root so its styles and the portal's never meet.
// A transform on the screen makes the app's full-screen (position: fixed) camera stay inside the phone.
const css = phoneCss
  .replace(/:root\b/g, ":host")
  .replace(/html,\s*body,\s*#root\s*\{/g, ".phone-app, .phone-app > div {")
  .replace(/(^|\})\s*body\s*\{/g, "$1 .phone-app {");

let root: Root | null = null;
export function PhoneFrame({ open }: { open: boolean }) {
  const host = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!host.current || root) return;
    const shadow = host.current.attachShadow({ mode: "open" });
    const style = document.createElement("style"); style.textContent = css + "\n.phone-app{height:100%;overflow:auto;position:relative}";
    const mount = document.createElement("div"); mount.className = "phone-app";
    shadow.append(style, mount);
    root = createRoot(mount);
    root.render(<App />);
  }, []);
  return (
    <div className={`demo-phone ${open ? "open" : ""}`} aria-hidden={!open}>
      <div className="demo-phone-body">
        <div className="demo-phone-notch" />
        <div className="demo-phone-screen" ref={host} />
      </div>
    </div>
  );
}
