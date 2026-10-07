import { defineConfig, type Plugin } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import { fileURLToPath } from "node:url";
import { readFileSync } from "node:fs";

// The demo builds the real portal pages (../frontend) and the real SLIMS Field app (../mobile) into ONE html file.
// Next.js is replaced by two tiny shims (hash routing), and every /api call is answered in the browser from a snapshot.
const here = (p: string) => fileURLToPath(new URL(p, import.meta.url));
const frontend = here("../frontend/");
const mobile = here("../mobile/");

/** Small source rewrites that only matter outside Next.js: query strings live in the hash, and the logo is inlined. */
function demoRewrites(): Plugin {
  const logo = "data:image/svg+xml;base64," + readFileSync(here("../frontend/public/slims-logo.svg")).toString("base64");
  return {
    name: "slims-demo-rewrites",
    enforce: "pre",
    transform(code, id) {
      const f = id.replace(/\\/g, "/");
      if (f.startsWith(mobile.replace(/\\/g, "/")) && f.endsWith("App.tsx"))
        return { code: code.replace('src="./icon.png"', `src="${"data:image/png;base64," + readFileSync(here("../mobile/public/icon.png")).toString("base64")}"`), map: null };
      if (f.startsWith(mobile.replace(/\\/g, "/")) && f.endsWith("native-web.ts"))
        return { code: code.replace("isMock: false", "isMock: !!(window as unknown as { __demoMock?: boolean }).__demoMock"), map: null };
      if (!f.startsWith(frontend.replace(/\\/g, "/"))) return null;
      let out = code.replace(/window\.location\.search/g, "window.__slimsSearch()").replace(/"\/slims-logo\.svg"/g, JSON.stringify(logo));
      if (/"use client";?/.test(out)) out = out.replace(/^"use client";?/m, "");
      return out === code ? null : { code: out, map: null };
    },
  };
}

export default defineConfig({
  root: here("."),
  base: "./",
  plugins: [demoRewrites(), react(), viteSingleFile({ removeViteModuleLoader: true })],
  resolve: {
    alias: [
      { find: /^@\/(.*)$/, replacement: frontend + "$1" },
      { find: /^next\/link$/, replacement: here("src/shims/next-link.tsx") },
      { find: /^next\/navigation$/, replacement: here("src/shims/next-navigation.ts") },
      { find: /^@mobile\/(.*)$/, replacement: mobile + "src/$1" },
    ],
    dedupe: ["react", "react-dom", "leaflet", "jsqr", "@capacitor/core", "@capacitor/preferences", "@capacitor/app", "@capacitor/network"],
  },
  server: { fs: { allow: [here(".."), frontend, mobile] } },
  build: { outDir: "dist", target: "es2020", assetsInlineLimit: 100_000_000, chunkSizeWarningLimit: 100_000, cssCodeSplit: false, sourcemap: false },
});
