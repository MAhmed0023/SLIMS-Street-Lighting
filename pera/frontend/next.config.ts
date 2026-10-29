import type { NextConfig } from "next";

// The browser only ever talks to the portal host. Next forwards /api to the .NET API server side,
// so the refresh cookie stays first-party on the portal domain and no CORS is involved.
//
//   portal   this Next server (port 3002 in development)
//   API      Pera.Api on 127.0.0.1:5092, behind IIS or Nginx in production
//
// API_URL is resolved when Next compiles. Moving the API means a rebuild.
const api = process.env.API_URL ?? "http://localhost:5092";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  output: "standalone",
  experimental: {
    // document uploads go through proxy.ts, which buffers request bodies (10 MB by default)
    proxyClientMaxBodySize: "210mb",
    proxyTimeout: 180_000,
  },
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${api}/api/:path*` }];
  },
};

export default nextConfig;
