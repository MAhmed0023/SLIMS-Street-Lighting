"use client";
import { Suspense } from "react";
import { Shell } from "@/components/Shell";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  // pages read the query string (?id=, filters); the boundary keeps that client-side, as Next requires
  return <Shell><Suspense fallback={<div className="loading">Loading…</div>}>{children}</Suspense></Shell>;
}
