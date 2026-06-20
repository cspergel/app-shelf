import { useCallback, useEffect, useRef, useState } from "react";
import { invoke, hasBridge } from "./bridge";
import { getMockApps, mockLaunch, mockStop } from "./mock-data";
import { setFavorite as persistFavorite } from "./app-actions";
import { applyRegroup, optimisticApply, type RegroupTarget } from "./regroup";
import type { AppView } from "./types";

const POLL_MS = 2000;
// After a drop ends, suppress polling for this many ms to let dnd-kit finish settling.
const POST_DROP_SUPPRESS_MS = 1000;

interface BridgeState {
  apps: AppView[];
  loading: boolean;
  error: string | null;
  /** False when running in a plain browser (no WebView2 host). */
  connected: boolean;
}

/** Compare two app lists by the fields that matter for rendering. Returns true if anything changed. */
function appsChanged(prev: AppView[], next: AppView[]): boolean {
  if (prev.length !== next.length) return true;
  for (let i = 0; i < prev.length; i++) {
    const p = prev[i];
    const n = next[i];
    if (
      p.id !== n.id ||
      p.status !== n.status ||
      p.group !== n.group ||
      p.favorite !== n.favorite ||
      p.name !== n.name ||
      p.url !== n.url ||
      p.role !== n.role
    ) return true;
    // A crashed app's log tail arriving (or its length changing) must trigger a re-render so the
    // log panel can auto-surface. -1 sentinel makes null-vs-present a detectable transition.
    if ((p.logTail?.length ?? -1) !== (n.logTail?.length ?? -1)) return true;
  }
  return false;
}

/**
 * Loads apps via the C# bridge and polls live status every ~2s.
 * When window.chrome.webview is absent (plain browser / dev / screenshot),
 * falls back to realistic mock data and no-op actions with optimistic state.
 */
export function useBridge() {
  const bridge = hasBridge();

  const [state, setState] = useState<BridgeState>({
    apps: bridge ? [] : getMockApps(),
    loading: bridge,
    error: null,
    connected: bridge,
  });

  const mountedRef = useRef(true);
  // Latest apps, readable inside callbacks without re-creating them (used by the drop path
  // to decide locally whether a gesture forms a brand-new group vs. joins an existing one).
  const appsRef = useRef(state.apps);
  appsRef.current = state.apps;

  // Drag suppression: set true while a drag is active; cleared POST_DROP_SUPPRESS_MS after drop.
  const draggingRef = useRef(false);
  const postDropTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const refresh = useCallback(async () => {
    if (!bridge) {
      // Mock path: re-read optimistic mock state on every poll tick
      if (mountedRef.current) {
        setState((s) => ({ ...s, apps: getMockApps(), loading: false }));
      }
      return;
    }
    // Suppress poll during drag or post-drop cooldown.
    if (draggingRef.current) {
      return;
    }
    try {
      const apps = await invoke<AppView[]>("listApps");
      if (!mountedRef.current) return;
      const incoming = apps ?? [];
      // Diff-based update: only replace the apps array (and trigger re-render) if something
      // actually changed. Unchanged polls preserve object identity → no card re-renders.
      const changed = appsChanged(appsRef.current, incoming);
      if (changed) {
        setState((s) => ({ ...s, apps: incoming, loading: false, error: null }));
      } else {
        // Still clear loading/error even if apps are the same.
        setState((s) => s.loading || s.error ? { ...s, loading: false, error: null } : s);
      }
    } catch (e) {
      if (!mountedRef.current) return;
      setState((s) => ({
        ...s,
        loading: false,
        error: e instanceof Error ? e.message : String(e),
      }));
    }
  }, [bridge]);

  useEffect(() => {
    mountedRef.current = true;
    void refresh();
    const id = window.setInterval(() => void refresh(), POLL_MS);
    return () => {
      mountedRef.current = false;
      window.clearInterval(id);
    };
  }, [refresh]);

  /** Call from the DnD layer: true = drag started, false = drag ended (triggers post-drop cooldown). */
  const setDragging = useCallback((active: boolean) => {
    if (active) {
      // Clear any pending post-drop timer when a new drag starts.
      if (postDropTimerRef.current !== null) {
        clearTimeout(postDropTimerRef.current);
        postDropTimerRef.current = null;
      }
      draggingRef.current = true;
    } else {
      // Drag ended: start the post-drop cooldown.
      postDropTimerRef.current = setTimeout(() => {
        draggingRef.current = false;
        postDropTimerRef.current = null;
      }, POST_DROP_SUPPRESS_MS);
    }
  }, []);

  const setStatus = useCallback((id: string, status: AppView["status"]) => {
    setState((s) => ({
      ...s,
      apps: s.apps.map((a) => (a.id === id ? { ...a, status } : a)),
    }));
  }, []);

  const launch = useCallback(
    async (id: string) => {
      setStatus(id, "Starting");
      if (!bridge) {
        mockLaunch(id);
        return;
      }
      try {
        await invoke("launch", { id });
      } catch (e) {
        setState((s) => ({
          ...s,
          error: e instanceof Error ? e.message : String(e),
        }));
      } finally {
        void refresh();
      }
    },
    [bridge, refresh, setStatus],
  );

  const stop = useCallback(
    async (id: string) => {
      if (!bridge) {
        mockStop(id);
        setState((s) => ({
          ...s,
          apps: s.apps.map((a) => (a.id === id ? { ...a, status: "Stopped" } : a)),
        }));
        return;
      }
      try {
        await invoke("stop", { id });
      } catch (e) {
        setState((s) => ({
          ...s,
          error: e instanceof Error ? e.message : String(e),
        }));
      } finally {
        void refresh();
      }
    },
    [bridge, refresh],
  );

  // Toggle the favorite flag: optimistic in-place update (no jarring rebuild), persist via
  // the bridge (or mock), then refresh to reconcile. On failure, refresh restores truth.
  const toggleFavorite = useCallback(
    async (id: string, favorite: boolean) => {
      setState((s) => ({
        ...s,
        apps: s.apps.map((a) => (a.id === id ? { ...a, favorite } : a)),
      }));
      try {
        await persistFavorite(id, favorite);
      } catch (e) {
        setState((s) => ({
          ...s,
          error: e instanceof Error ? e.message : String(e),
        }));
      } finally {
        void refresh();
      }
    },
    [refresh],
  );

  // Drag-to-group with an INSTANT optimistic update: reorder/regroup the local list in
  // place immediately (statuses preserved — no port re-probe), then persist via the bridge
  // in the BACKGROUND. We deliberately do NOT trigger the heavy `refresh()` (per-app TCP
  // status probes) on the drop path — that synchronous scan was the post-drop lag. The
  // periodic ~2s poll reconciles the structural change and refreshes statuses afterward.
  //
  // For a brand-new group (both cards standalone, no name yet) the gesture needs a name dialog,
  // so the FIRST applyRegroup is awaited to learn whether a dialog is required. Every OTHER
  // gesture — join an existing group, pull a standalone into a group, leave, move, reorder, or a
  // dialog-confirmed new group — is resolvable locally, so it applies optimistically and persists
  // in the background with NO await and NO heavy refresh on the drop path.
  const regroupOptimistic = useCallback(
    async (
      draggedId: string,
      target: RegroupTarget,
      groupName?: string,
      roles?: Record<string, string>,
    ): Promise<{ needsNameDialog: boolean; suggestedGroupName?: string; seeds?: { id: string; name: string; role: string }[] }> => {
      // A new-group gesture is ONLY when dropping a standalone card onto another standalone card
      // with no name supplied yet. Anything involving an existing group is a plain join — handled
      // locally and instantly below (no bridge round-trip before the visual update).
      const apps = appsRef.current;
      const dragged = apps.find((a) => a.id === draggedId);
      const tgt = target.kind === "onApp" ? apps.find((a) => a.id === target.appId) : undefined;
      const isPotentialNewGroup =
        target.kind === "onApp" &&
        !groupName &&
        !!dragged && !dragged.group &&
        !!tgt && !tgt.group;
      if (isPotentialNewGroup) {
        const res = await applyRegroup(draggedId, target, groupName, roles);
        if (res.needsNameDialog) {
          return {
            needsNameDialog: true,
            suggestedGroupName: res.suggestedGroupName,
            seeds: res.seeds,
          };
        }
        // Not a new group after all (merge / join existing) — Core already applied it.
        // Reflect it optimistically and let the poll confirm; no extra refresh.
        if (res.applied) {
          setState((s) => ({ ...s, apps: optimisticApply(s.apps, draggedId, target, groupName, roles) }));
        }
        return { needsNameDialog: false };
      }

      // All other gestures (join / leave / move / reorder / confirmed new group):
      // update local state instantly, then persist in the background. No heavy refresh.
      setState((s) => ({ ...s, apps: optimisticApply(s.apps, draggedId, target, groupName, roles) }));
      void applyRegroup(draggedId, target, groupName, roles).catch((e) => {
        // Persist failed — surface the error; the next poll will restore canonical truth.
        if (!mountedRef.current) return;
        setState((s) => ({ ...s, error: e instanceof Error ? e.message : String(e) }));
      });
      return { needsNameDialog: false };
    },
    [],
  );

  return { ...state, refresh, launch, stop, toggleFavorite, regroupOptimistic, setDragging };
}
