import type { NextConfig } from "next";

// The browser only ever talks to the portal host. Next forwards /api to the .NET API server side,
// so the refresh cookie stays first-party on the portal domain and no CORS is involved.
//
//   portal   this Next server (port 3001 in development)
//   API      Slims.Api on 127.0.0.1:5091, behind IIS or Nginx in production
//
// API_URL is resolved when Next compiles — see .env.production. Moving the API means a rebuild.
const api = process.env.API_URL ?? "http://localhost:5091";

const nextConfig: NextConfig = {
  reactStrictMode: true,
  output: "standalone",
  experimental: {
    // Field evidence arrives as JSON with two base64 photographs. proxy.ts buffers request bodies, by default
    // only up to 10 MB, and rewrites time out after 30 s — too little for a slow mobile connection.
    proxyClientMaxBodySize: "64mb",
    proxyTimeout: 180_000,
  },
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${api}/api/:path*` }];
  },
};

export default nextConfig;
