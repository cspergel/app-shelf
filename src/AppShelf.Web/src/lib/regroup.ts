// Drag-to-group: thin typed wrappers over the C# bridge's `applyRegroup` / `renameGroup`,
// with a mock-mode fallback (plain browser). Planning stays in Core (RegroupPlanner) — the
// JS side only sends a HIGH-LEVEL drag intent and the C# bridge builds + applies the plan.

import { invoke, hasBridge } from "./bridge";
import { mockApplyRegroup, mockRenameGroup } from "./mock-data";
import type { AppView } from "./types";

/** A high-level drag target. Mirrors the Core DropTarget union the bridge maps it to. */
export type RegroupTarget =
  | { kind: "onApp"; appId: string } // dropped onto another card → group / join
  | { kind: "onGroup"; groupName: string } // dropped onto a group card → join
  | { kind: "onCanvas" } // dropped onto the standalone area → leave group
  | { kind: "atMemberIndex"; groupName: string; index: number }; // reorder within a group

/** What a dnd-kit draggable carries in its data: the app it represents. */
export type DragKind = { kind: "app"; app: AppView };

/** One member seed for the new-group dialog (id + name + smart-prefilled role). */
export interface GroupSeed {
  id: string;
  name: string;
  role: string;
}

/** What applyRegroup resolves with. When `needsNameDialog`, nothing was persisted yet — the
 * caller must show the name/role dialog and re-call with `groupName` (+ optional `roles`). */
export interface RegroupResult {
  applied: boolean;
  needsNameDialog: boolean;
  suggestedGroupName?: string;
  seeds?: GroupSeed[];
  /** Refreshed app list (live bridge only; mock callers re-read via refresh poll). */
  apps?: AppView[];
}

/**
 * Send a drag intent. On a NewGroup gesture the first call returns `needsNameDialog: true`
 * with a suggested name + seeds (nothing persisted); call again with `groupName` (+ `roles`)
 * to commit. All other gestures (join / leave / move / reorder) apply immediately.
 */
export async function applyRegroup(
  draggedId: string,
  target: RegroupTarget,
  groupName?: string,
  roles?: Record<string, string>,
): Promise<RegroupResult> {
  if (!hasBridge()) {
    const r = mockApplyRegroup(draggedId, target, groupName, roles);
    return {
      applied: r.applied,
      needsNameDialog: r.needsNameDialog,
      suggestedGroupName: r.suggestedGroupName,
      seeds: r.seeds,
    };
  }

  const args: Record<string, unknown> = { draggedId, target };
  if (groupName) args.groupName = groupName;
  if (roles) args.roles = roles;
  return await invoke<RegroupResult>("applyRegroup", args);
}

// ── Optimistic local apply ────────────────────────────────────────────────────
// Mirrors the group-membership field changes Core's RegroupPlanner makes, but on the
// LOCAL app list and WITHOUT any status re-probe — so a drop reflects instantly. The
// canonical plan still runs in Core via the bridge (fire-and-forget on the drop path);
// the next ~2s status poll reconciles truth. Statuses are preserved verbatim here so
// cards never flicker to "Stopped"/lose state while the structural change lands.

/** Coarse role guess from a framework string (mirrors mock-data.guessRole / Core's intent). */
function guessRole(framework: string | null): string {
  const f = (framework ?? "").toLowerCase();
  const fe = ["vite", "next", "react", "astro", "svelte", "angular", "storybook"];
  const be = ["fastapi", "flask", "uvicorn", "streamlit", "gradio", "django", "node"];
  if (fe.some((t) => f.includes(t))) return "frontend";
  if (be.some((t) => f.includes(t))) return "backend";
  return "other";
}

/**
 * Return a NEW apps array reflecting `target` applied to `draggedId`, preserving every
 * card's current status. Returns the input array unchanged for no-ops (caller can skip
 * the state update). Only group/role membership (and a light reorder) change — never status.
 *
 * NewGroup commits require `groupName` (the name the dialog produced); a NewGroup gesture
 * with no name returns the list unchanged (the visual update waits for the dialog).
 */
export function optimisticApply(
  apps: AppView[],
  draggedId: string,
  target: RegroupTarget,
  groupName?: string,
  roles?: Record<string, string>,
): AppView[] {
  const dragged = apps.find((a) => a.id === draggedId);
  if (!dragged) return apps;

  const setFields = (list: AppView[], id: string, group: string | null, role: string) =>
    list.map((a) => (a.id === id ? { ...a, group, role } : a));

  // Dissolve a one-member-left group (its lone member becomes standalone).
  const dissolveIfOrphaned = (list: AppView[], group: string | null): AppView[] => {
    if (!group) return list;
    const rest = list.filter((a) => a.group === group);
    if (rest.length === 1) return setFields(list, rest[0].id, null, rest[0].role || "");
    return list;
  };

  // Leave group → standalone.
  if (target.kind === "onCanvas") {
    if (!dragged.group) return apps;
    const src = dragged.group;
    let next = setFields(apps, draggedId, null, dragged.role || "");
    next = dissolveIfOrphaned(next, src);
    return next;
  }

  // Reorder within a role band: move the dragged member to the target slot among peers.
  if (target.kind === "atMemberIndex") {
    const group = target.groupName;
    if (dragged.group !== group) return apps;
    const peers = apps.filter((a) => a.group === group && a.role === dragged.role);
    const without = peers.filter((m) => m.id !== draggedId);
    const idx = Math.max(0, Math.min(target.index, without.length));
    without.splice(idx, 0, dragged);
    const order = new Map(without.map((m, i) => [m.id, i]));
    return [...apps].sort((a, b) => {
      const oa = order.get(a.id);
      const ob = order.get(b.id);
      if (oa == null || ob == null) return 0;
      return oa - ob;
    });
  }

  // Join an existing group (onGroup, or onApp where the target/dragged resolves to a group).
  let destGroup: string | null = null;
  let joiner = dragged;

  if (target.kind === "onGroup") {
    destGroup = target.groupName;
  } else if (target.kind === "onApp") {
    if (target.appId === draggedId) return apps;
    const tgt = apps.find((a) => a.id === target.appId);
    if (!tgt) return apps;
    if (tgt.group) {
      destGroup = tgt.group;
    } else if (dragged.group) {
      destGroup = dragged.group;
      joiner = tgt;
    } else {
      // Both standalone → NEW group. Only commit once the dialog supplied a name.
      if (!groupName) return apps;
      const name = groupName.trim();
      const draggedRole = roles?.[dragged.id] ?? guessRole(dragged.framework);
      const tgtRole = roles?.[tgt.id] ?? guessRole(tgt.framework);
      let next = setFields(apps, dragged.id, name, draggedRole);
      next = setFields(next, tgt.id, name, tgtRole);
      return next;
    }
  }

  if (!destGroup) return apps;
  if (joiner.group === destGroup) return apps;

  const src = joiner.group;
  let next = setFields(apps, joiner.id, destGroup, guessRole(joiner.framework));
  next = dissolveIfOrphaned(next, src ?? null);
  return next;
}

/** Rename a group (used by the new-group dialog / rename flow). */
export async function renameGroup(oldName: string, newName: string): Promise<void> {
  if (!hasBridge()) {
    mockRenameGroup(oldName, newName);
    return;
  }
  await invoke("renameGroup", { oldName, newName });
}
