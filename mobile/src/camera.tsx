import { useEffect, useRef, useState } from "react";
import jsQR from "jsqr";

/** Full-screen live camera. There is no gallery or file picker anywhere in the app: a photograph can only come from here
 * (FR-FLD-007). In scan mode it reads QR codes from the same live video. */
export function LiveCamera({ mode, title, hint, onPhoto, onCode, onClose, overlay }: {
  mode: "photo" | "scan"; title: string; hint?: string; overlay?: React.ReactNode;
  onPhoto?: (jpeg: Blob) => void; onCode?: (text: string) => void; onClose: () => void;
}) {
  const video = useRef<HTMLVideoElement>(null);
  const [err, setErr] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const done = useRef(false);

  useEffect(() => {
    let stream: MediaStream | null = null; let timer = 0; let alive = true;
    (async () => {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: false, video: { facingMode: { ideal: "environment" }, width: { ideal: 1920 }, height: { ideal: 1080 } } });
        if (!alive) { stream.getTracks().forEach((t) => t.stop()); return; }
        const v = video.current!; v.srcObject = stream; await v.play();
        if (mode === "scan") {
          const c = document.createElement("canvas"); const ctx = c.getContext("2d", { willReadFrequently: true })!;
          const tick = () => {
            if (!alive || done.current) return;
            if (v.videoWidth) {
              const w = 720, h = Math.round((v.videoHeight / v.videoWidth) * w); c.width = w; c.height = h;
              ctx.drawImage(v, 0, 0, w, h);
              const code = jsQR(ctx.getImageData(0, 0, w, h).data, w, h, { inversionAttempts: "dontInvert" });
              if (code?.data) { done.current = true; navigator.vibrate?.(60); onCode?.(code.data); return; }
            }
            timer = window.setTimeout(tick, 140);
          };
          tick();
        }
      } catch (e) { setErr(e instanceof Error ? e.message : "The camera could not be opened. Allow camera access for SLIMS Field in the phone settings."); }
    })();
    return () => { alive = false; clearTimeout(timer); stream?.getTracks().forEach((t) => t.stop()); };
  }, [mode, onCode]);

  const shoot = async () => {
    const v = video.current; if (!v || !v.videoWidth) return;
    setBusy(true);
    const c = document.createElement("canvas"); c.width = v.videoWidth; c.height = v.videoHeight;
    c.getContext("2d")!.drawImage(v, 0, 0);
    const blob = await new Promise<Blob | null>((res) => c.toBlob(res, "image/jpeg", 0.85));
    setBusy(false);
    if (blob) { navigator.vibrate?.(40); onPhoto?.(blob); }
  };

  return (
    <div className="cam">
      <video ref={video} playsInline muted />
      <div className="cam-top"><button className="ghost" onClick={onClose}>✕</button><b>{title}</b><span /></div>
      {mode === "scan" && <div className="cam-frame" />}
      {overlay && <div className="cam-overlay">{overlay}</div>}
      {err && <div className="cam-err">{err}</div>}
      <div className="cam-bottom">
        {hint && <div className="cam-hint">{hint}</div>}
        {mode === "photo" && <button className="shutter" disabled={busy || !!err} onClick={shoot} aria-label="Take photograph" />}
      </div>
    </div>
  );
}
