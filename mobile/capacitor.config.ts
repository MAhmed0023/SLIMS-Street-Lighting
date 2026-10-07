import type { CapacitorConfig } from "@capacitor/cli";

// androidScheme https makes the app a secure context (camera and crypto.subtle work); CapacitorHttp sends API calls
// natively, so a plain-HTTP office server such as http://172.16.10.184:3001 is reachable without mixed-content errors.
const config: CapacitorConfig = {
  appId: "pk.gov.pera.slims.field",
  appName: "SLIMS Field",
  webDir: "dist",
  server: { androidScheme: "https" },
  android: { allowMixedContent: true },
  plugins: { CapacitorHttp: { enabled: true } },
};
export default config;
