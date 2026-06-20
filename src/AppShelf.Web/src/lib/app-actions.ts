// Add / Edit / Remove app actions + the dialog's folder-pick and stack-detect helpers.
// Thin typed wrappers over the C# bridge with a mock-mode fallback (plain browser): each
// resolves with the same shape the live bridge returns, so the dialog is fully demoable
// without WebView2.

import { invoke, hasBridge } from "./bridge";
import {
  mockPickFolder,
  mockDetectStack,
  mockAddApp,
  mockUpdateApp,
  mockRemoveApp,
  mockSetFavorite,
  mockListGroups,
  mockReservedPorts,
} from "./mock-data";
import type { AppView } from "./types";

/** What detectStack resolves with (any field may be null when nothing was inferred). */
export interface DetectResult {
  framework: string | null;
  cmd: string | null;
  url: string | null;
  port: number | null;
  installCmd: string | null;
  /** The matched run-folder (e.g. a frontend/ subfolder), or null. */
  dir: string | null;
  detected: boolean;
}

/** The editable payload the dialog submits (id present only when editing). */
export interface AppFormPayload {
  id?: string;
  name: string;
  dir: string | null;
  cmd: string | null;
  url: string;
  installCmd: string | null;
  framework: string | null;
  tags: string[];
  group: string | null;
  role: string;
  open: string;
  favorite: boolean;
  /** Explicit port (string, validated C#-side); blank/null auto-allocates for local apps. */
  port: string | null;
  portFixed: boolean;
}

/** Open the native folder picker; resolves with the chosen path or null (cancelled). */
export async function pickFolder(current?: string | null): Promise<string | null> {
  if (!hasBridge()) return mockPickFolder();
  const result = await invoke<{ path: string | null }>("pickFolder", {
    current: current ?? "",
  });
  return result?.path ?? null;
}

/** Run the Core stack detector on a folder; resolves with the suggestion. */
export async function detectStack(dir: string): Promise<DetectResult> {
  if (!hasBridge()) return mockDetectStack(dir);
  return await invoke<DetectResult>("detectStack", { dir });
}

/** Create a new app; resolves with the created row (incl. generated id). */
export async function addApp(payload: AppFormPayload): Promise<AppView> {
  if (!hasBridge()) return mockAddApp(payloadToAppView(payload));
  return await invoke<AppView>("addApp", payloadToArgs(payload));
}

/** Apply edits to an existing app; resolves with the updated row. */
export async function updateApp(payload: AppFormPayload): Promise<AppView> {
  if (!hasBridge()) return mockUpdateApp(payloadToAppView(payload));
  return await invoke<AppView>("updateApp", payloadToArgs(payload));
}

/** Set the favorite flag for an app and persist it; resolves with the updated row. */
export async function setFavorite(id: string, favorite: boolean): Promise<AppView | null> {
  if (!hasBridge()) return mockSetFavorite(id, favorite);
  return await invoke<AppView>("setFavorite", { id, favorite });
}

/** Remove an app by id. */
export async function removeApp(id: string): Promise<void> {
  if (!hasBridge()) {
    mockRemoveApp(id);
    return;
  }
  await invoke("removeApp", { id });
}

/** Distinct existing group names for the dialog's datalist. */
export async function listGroups(): Promise<string[]> {
  if (!hasBridge()) return mockListGroups();
  return (await invoke<string[]>("listGroups")) ?? [];
}

/** Ports already reserved by other apps (for the dialog's port-conflict hint). */
export async function reservedPorts(excludeId?: string): Promise<number[]> {
  if (!hasBridge()) return mockReservedPorts(excludeId);
  return (await invoke<number[]>("reservedPorts", { excludeId: excludeId ?? "" })) ?? [];
}

// ── helpers ──────────────────────────────────────────────────────────────────

/** Strip empty optional fields to plain args for the bridge (camelCase passthrough). */
function payloadToArgs(p: AppFormPayload): Record<string, unknown> {
  return {
    id: p.id ?? "",
    name: p.name,
    dir: p.dir ?? "",
    cmd: p.cmd ?? "",
    url: p.url,
    installCmd: p.installCmd ?? "",
    framework: p.framework ?? "",
    tags: p.tags,
    group: p.group ?? "",
    role: p.role,
    open: p.open,
    favorite: p.favorite,
    port: p.port ?? "",
    portFixed: p.portFixed,
  };
}

/** Build an optimistic AppView for the mock path (no live status — assume Stopped). */
function payloadToAppView(p: AppFormPayload): AppView {
  const urlOnly = !p.cmd || p.cmd.trim().length === 0;
  return {
    id: p.id ?? "",
    name: p.name,
    url: p.url,
    framework: p.framework,
    favorite: p.favorite,
    group: p.group,
    role: p.role,
    port: p.port ? Number(p.port) : null,
    status: "Stopped",
    tags: p.tags,
    dir: urlOnly ? null : p.dir,
  };
}
