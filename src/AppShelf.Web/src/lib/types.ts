// Mirrors the JSON the C# bridge posts back (AppShelfBridge.AppView). The bridge
// serializes with camelCase property names and sends `status` as a string: either a
// Core LaunchStatus enum name or "StoppedUnexpectedly" (the GUI-only crashed state,
// emitted when a managed app's process exited unexpectedly). `tags` is always present
// from the live bridge (possibly empty); the mock data path sets it too.

export type LaunchStatus =
  | "Stopped"
  | "Starting"
  | "Running"
  | "Error"
  | "PortInUse"
  | "StoppedUnexpectedly";

/** One row in the card grid: an AppEntry projection + its live status. */
export interface AppView {
  id: string;
  name: string;
  url: string;
  framework: string | null;
  favorite: boolean;
  group: string | null;
  role: string;
  port: number | null;
  status: LaunchStatus;
  tags?: string[];
}

// ── Port Doctor ──────────────────────────────────────────────────────────────
// Mirrors AppShelfBridge.PortRowView (camelCase). One row per scanned listener,
// with the full ancestry chain (the CLI DTO drops ancestry; the GUI keeps it).

/** How confident we are about a live port's ownership. Mirrors Core PortTier. */
export type PortTier = "Managed" | "Registered" | "LikelyOrphaned" | "Unknown";

/** One node in a process's parent chain. */
export interface AncestorView {
  pid: number;
  processName: string;
  alive: boolean;
  commandLine: string | null;
  exeDir: string | null;
}

/** One scanned listener: evidence + ownership mapping + tier. */
export interface PortRowView {
  port: number;
  family: "ipv4" | "ipv6";
  tier: PortTier;
  pid: number;
  processName: string;
  isService: boolean;
  serviceName: string | null;
  parentAlive: boolean;
  ownerAppId: string | null;
  ownerAppName: string | null;
  exePath: string | null;
  commandLine: string | null;
  exeDir: string | null;
  startedAt: string | null;
  ancestry: AncestorView[];
}

/** Result shapes the bridge actions resolve with (mirror the C# anonymous objects). */
export interface KillResult {
  success: boolean;
  reason: string | null;
  accessDenied: boolean;
}
export interface ReclaimResult {
  kill: KillResult;
  launch: { status: string; reason: string | null } | null;
}
export interface ServiceStopResult {
  started: boolean;
  cancelled: boolean;
  portFreed: boolean;
  reason: string | null;
}
