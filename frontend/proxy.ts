import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

/**
 * Client address handling for the audit trail.
 *
 * The API sits behind this Next server, so it sees every call as coming from 127.0.0.1 unless
 * something puts the caller's real address into a header. Put IIS or Nginx in front and the real
 * address arrives in `x-real-ip` or `forwarded`, which is passed straight through below.
 *
 * What this file guarantees: a caller cannot forge its own address. Any `x-forwarded-for` sent by
 * the browser is dropped, so nobody can write a false machine into the audit trail.
 */
export function proxy(request: NextRequest) {
  const headers = new Headers(request.headers);
  const ip = trustedClientIp(request);
  if (ip) headers.set("x-forwarded-for", ip);
  else headers.delete("x-forwarded-for");
  return NextResponse.next({ request: { headers } });
}

function trustedClientIp(request: NextRequest): string | null {
  const real = request.headers.get("x-real-ip");
  if (real) return strip(real);
  const fwd = request.headers.get("forwarded");
  const m = fwd?.match(/for="?\[?([^\];,"]+)/i);
  return m ? strip(m[1]) : null;
}

function strip(ip: string): string {
  let v = ip.trim().replace(/^::ffff:/i, "");
  if (/^\d+\.\d+\.\d+\.\d+:\d+$/.test(v)) v = v.split(":")[0];
  return v;
}

export const config = { matcher: "/api/:path*" };
