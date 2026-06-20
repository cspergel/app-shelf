import { useCallback, useEffect, useRef, useState } from "react";
import { invoke, hasBridge } from "./bridge";
import { getMockApps, mockLaunch, mockStop } from "./mock-data";
import { setFavorite as persistFavorite } from "./app-actions";
import type { AppView } from "./types";

const POLL_MS = 2000;

interface BridgeState {
  apps: AppView[];
  loading: boolean;
  error: string | null;
  /** False when running in a plain browser (no WebView2 host). */
  connected: boolean;
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

  const refresh = useCallback(async () => {
    if (!bridge) {
      // Mock path: re-read optimistic mock state on every poll tick
      if (mountedRef.current) {
        setState((s) => ({ ...s, apps: getMockApps(), loading: false }));
      }
      return;
    }
    try {
      const apps = await invoke<AppView[]>("listApps");
      if (!mountedRef.current) return;
      setState((s) => ({ ...s, apps: apps ?? [], loading: false, error: null }));
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

  return { ...state, refresh, launch, stop, toggleFavorite };
}
