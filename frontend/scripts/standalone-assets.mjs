// `next build` with output: "standalone" writes a self-contained server to .next/standalone but
// deliberately leaves out the static assets, expecting the deployment step to copy them in.
// Doing it here means `npm start` and the packaged copy both run the same, complete server.
import { cp, access } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const standalone = join(root, ".next", "standalone");
const exists = async (p) => { try { await access(p); return true; } catch { return false; } };

if (!(await exists(standalone))) {
  console.error("No .next/standalone — is output: \"standalone\" still set in next.config.ts?");
  process.exit(1);
}
await cp(join(root, ".next", "static"), join(standalone, ".next", "static"), { recursive: true });
if (await exists(join(root, "public"))) await cp(join(root, "public"), join(standalone, "public"), { recursive: true });
console.log("standalone server ready — static assets and public/ copied in");
