// Mirrors the JSON the C# bridge posts back. The bridge serializes AppShelf.Core
// records with camelCase property names and enums as strings.

export type LaunchStatus =
  | "Stopped"
  | "Starting"
  | "Running"
  | "Error"
  | "PortInUse";

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
}
