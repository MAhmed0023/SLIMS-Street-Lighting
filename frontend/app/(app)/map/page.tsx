"use client";
import "leaflet/dist/leaflet.css";
import { useEffect, useRef, useState } from "react";
import type { Map as LMap, LayerGroup } from "leaflet";
import { get, qs } from "@/lib/api";
import type { MapDto, SiteDto } from "@/lib/types";
import { date, dateTime } from "@/lib/format";
import { Card, ErrorBox, Loading, useAsync } from "@/components/ui";

const MARKER: Record<string, string> = { installed: "#2e8b57", pending: "#e0a33f", exception: "#c0392b", planned: "#8a8f98" };
const POINT: Record<string, string> = { Confirmed: "#2e8b57", ApprovedException: "#1f6f9f", PendingVerification: "#c0392b", MissingEvidence: "#6b3fa0" };
const esc = (s: string) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]!));

/** FR-RPT-009: installations and pending sites on a map, filterable by zone, site and period. OpenStreetMap tiles. */
export default function MapPage() {
  const [zone, setZone] = useState(""); const [siteId, setSiteId] = useState(""); const [from, setFrom] = useState(""); const [to, setTo] = useState("");
  const [points, setPoints] = useState(true);
  const sites = useAsync(() => get<SiteDto[]>("/api/sites"), []);
  const data = useAsync(() => get<MapDto>(`/api/explore/map${qs({ zone, siteId, from, to })}`), [zone, siteId, from, to]);
  const box = useRef<HTMLDivElement>(null);
  const map = useRef<LMap | null>(null);
  const layer = useRef<LayerGroup | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    let alive = true;
    (async () => {
      const L = await import("leaflet");
      if (!alive || !box.current || map.current) return;
      map.current = L.map(box.current, { zoomControl: true }).setView([31.52, 74.35], 12);
      L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", { maxZoom: 19, attribution: "© OpenStreetMap contributors" }).addTo(map.current);
      layer.current = L.layerGroup().addTo(map.current);
      setReady(true);
    })();
    return () => { alive = false; map.current?.remove(); map.current = null; };
  }, []);

  useEffect(() => {
    if (!ready || !data.data) return;
    (async () => {
      const L = await import("leaflet");
      const g = layer.current!; g.clearLayers();
      const bounds: [number, number][] = [];
      for (const s of data.data!.sites) {
        L.circle([s.lat, s.lng], { radius: s.geofenceM, color: MARKER[s.marker], weight: 1, fillOpacity: .06 }).addTo(g);
        L.circleMarker([s.lat, s.lng], { radius: 9, color: "#fff", weight: 2, fillColor: MARKER[s.marker], fillOpacity: 1 })
          .bindPopup(`<b>${esc(s.code)}</b><br>${esc(s.name)}<br>${esc(s.zone)} · ${esc(s.division)}<br>Issued ${s.issued} · installed ${s.installed} · pending ${s.pending}${s.exceptions ? ` · <b style="color:#c0392b">${s.exceptions} exception(s)</b>` : ""}<br>Last installation: ${s.lastInstallation ? date(s.lastInstallation) : "none"}`)
          .addTo(g);
        bounds.push([s.lat, s.lng]);
      }
      if (points) for (const p of data.data!.installations) {
        L.circleMarker([p.lat, p.lng], { radius: 5, color: POINT[p.geoStatus] ?? "#555", weight: 1, fillColor: POINT[p.geoStatus] ?? "#555", fillOpacity: .85 })
          .bindPopup(`<b>${esc(p.number)}</b><br>${esc(p.itemCode)} #${p.serial}<br>${esc(p.geoStatus)} · ${Math.round(p.distanceM)} m from ${esc(p.siteCode)}<br>${esc(p.officer)} · ${dateTime(p.capturedAt)}`)
          .addTo(g);
        bounds.push([p.lat, p.lng]);
      }
      if (bounds.length) map.current!.fitBounds(bounds, { padding: [30, 30], maxZoom: 16 });
    })();
  }, [ready, data.data, points]);

  return (
    <>
      <div className="filters">
        <select value={zone} onChange={(e) => { setZone(e.target.value); setSiteId(""); }}><option value="">All zones</option>{(data.data?.zones ?? []).map((z) => <option key={z}>{z}</option>)}</select>
        <select value={siteId} onChange={(e) => setSiteId(e.target.value)}><option value="">All sites</option>{(sites.data ?? []).filter((s) => !zone || s.zone === zone).map((s) => <option key={s.id} value={s.id}>{s.code}</option>)}</select>
        <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} aria-label="From" />
        <input type="date" value={to} onChange={(e) => setTo(e.target.value)} aria-label="To" />
        <label className="tiny"><input type="checkbox" checked={points} onChange={(e) => setPoints(e.target.checked)} /> Installation points</label>
      </div>
      <Card flush title="Sites and installations" sub={data.data ? `${data.data.sites.length} site(s) · ${data.data.installations.length} installation(s) in the period` : undefined}>
        {data.error ? <ErrorBox error={data.error} /> : null}
        {data.loading && !data.data && <Loading what="map data" />}
        <div ref={box} className="mapbox" />
        <div className="map-legend" style={{ padding: "8px 12px" }}>
          <span><i style={{ background: MARKER.installed }} />Site — installed</span><span><i style={{ background: MARKER.pending }} />Site — issued, pending installation</span>
          <span><i style={{ background: MARKER.exception }} />Site — open GPS exception</span><span><i style={{ background: MARKER.planned }} />Site — nothing issued</span>
          <span><i style={{ background: POINT.Confirmed }} />Installation confirmed</span><span><i style={{ background: POINT.ApprovedException }} />Exception approved</span><span><i style={{ background: POINT.PendingVerification }} />Pending verification</span>
        </div>
      </Card>
    </>
  );
}
