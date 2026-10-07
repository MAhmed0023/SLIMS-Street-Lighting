import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The app is bundled into the APK and served by Capacitor from https://localhost; nothing is loaded from the network.
export default defineConfig({
  plugins: [react()], base: "./", build: { outDir: "dist", target: "es2020", sourcemap: false },
  // development only: run the screens in a desktop browser against a local API (set SLIMS_API)
  server: { proxy: { "/api": process.env.SLIMS_API ?? "http://127.0.0.1:5191" } },
});
