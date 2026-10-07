import { registerPlugin, type PluginListenerHandle, type PermissionState } from "@capacitor/core";
import { FieldGuardWeb } from "./native-web";

/** One position fix. isMock is Android's own flag for a position supplied by a mock-location app. */
export interface Fix { lat: number; lng: number; accuracy: number; time: number; isMock: boolean; provider: string }
export interface DeviceInfo { uid: string; model: string; manufacturer: string; osVersion: string; autoTime: boolean }
export interface Perms { location: PermissionState; camera: PermissionState }

/** Native side: android/app/src/main/java/pk/gov/pera/slims/field/FieldGuardPlugin.java */
export interface FieldGuardPlugin {
  info(): Promise<DeviceInfo>;
  checkPermissions(): Promise<Perms>;
  requestPermissions(): Promise<Perms>;
  startWatch(): Promise<{ gps: boolean }>;
  stopWatch(): Promise<void>;
  addListener(event: "fix", cb: (f: Fix) => void): Promise<PluginListenerHandle>;
}

// the browser stand-in is bundled (not lazy-loaded), so it also works with the network off
export const FieldGuard = registerPlugin<FieldGuardPlugin>("FieldGuard", { web: () => new FieldGuardWeb() });
