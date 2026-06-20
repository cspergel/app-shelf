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
