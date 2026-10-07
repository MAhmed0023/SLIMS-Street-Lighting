import { WebPlugin } from "@capacitor/core";
import type { DeviceInfo, FieldGuardPlugin, Fix, Perms } from "./native";

/** Browser stand-in used only while developing the screens on a PC. The APK uses the native plugin. */
export class FieldGuardWeb extends WebPlugin implements Pick<FieldGuardPlugin, "info" | "checkPermissions" | "requestPermissions" | "startWatch" | "stopWatch"> {
  private watch: number | null = null;

  async info(): Promise<DeviceInfo> {
    let uid = localStorage.getItem("slims.webuid");
    if (!uid) { uid = "web-" + crypto.randomUUID(); localStorage.setItem("slims.webuid", uid); }
    return { uid, model: "Browser", manufacturer: navigator.platform || "PC", osVersion: navigator.userAgent.slice(0, 30), autoTime: true };
  }
  async checkPermissions(): Promise<Perms> { return { location: "prompt", camera: "prompt" }; }
  async requestPermissions(): Promise<Perms> { return { location: "granted", camera: "granted" }; }
  async startWatch() {
    if (this.watch !== null) navigator.geolocation.clearWatch(this.watch);
    this.watch = navigator.geolocation.watchPosition((p) => {
      const f: Fix = { lat: p.coords.latitude, lng: p.coords.longitude, accuracy: p.coords.accuracy, time: p.timestamp, isMock: false, provider: "browser" };
      this.notifyListeners("fix", f);
    }, () => {}, { enableHighAccuracy: true, maximumAge: 3000 });
    return { gps: true };
  }
  async stopWatch() { if (this.watch !== null) navigator.geolocation.clearWatch(this.watch); this.watch = null; }
}
