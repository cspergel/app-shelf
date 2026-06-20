// Per-app quick dev actions (the card ⋯ menu). Thin wrappers over the C# bridge with a
// mock-mode fallback: when there's no WebView2 host, dir/url actions no-op with an optimistic
// "would …" success so the menu is demoable in a plain browser. Copy actions always work for
// real (clipboard). Every wrapper resolves with a { ok, reason } the caller toasts.

import { invoke, hasBridge } from "./bridge";
import type { AppView, QuickActionResult } from "./types";

const OK: QuickActionResult = { ok: true, reason: null };

/** Run a bridge quick action, or optimistically no-op in mock mode with a "would …" message. */
async function run(
  method: string,
  app: AppView,
  mockMessage: string,
): Promise<QuickActionResult & { mock?: boolean }> {
  if (!hasBridge()) {
    return { ok: true, reason: mockMessage, mock: true };
  }
  try {
    const result = await invoke<QuickActionResult>(method, { id: app.id });
    return result ?? OK;
  } catch (e) {
    return { ok: false, reason: e instanceof Error ? e.message : String(e) };
  }
}

export const openTerminal = (app: AppView) =>
  run("openTerminal", app, `Would open a terminal in ${app.dir}`);

export const openEditor = (app: AppView) =>
  run("openEditor", app, `Would open ${app.dir} in your editor`);

export const openFolder = (app: AppView) =>
  run("openFolder", app, `Would open ${app.dir} in Explorer`);

export const openEnv = (app: AppView) =>
  run("openEnv", app, `Would open ${app.dir}\\.env`);

export const openDocs = (app: AppView) =>
  run("openDocs", app, `Would open docs for ${app.name}`);

/** Copy text to the clipboard (works for real even in mock mode). */
export async function copyToClipboard(text: string): Promise<QuickActionResult> {
  try {
    await navigator.clipboard.writeText(text);
    return OK;
  } catch (e) {
    return { ok: false, reason: e instanceof Error ? e.message : "Clipboard unavailable" };
  }
}
