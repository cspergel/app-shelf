import { useCallback, useEffect, useRef, useState } from "react";
import { invoke, hasBridge } from "./bridge";
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
 * Exposes launch/stop actions that optimistically nudge status then refresh.
 */
export function useBridge() {
  const [state, setState] = useState<BridgeState>({
    apps: [],
    loading: true,
    error: null,
    connected: hasBridge(),
  });

  // Keep a ref so the poll closure always reads the latest apps without
  // re-subscribing the interval.
  const mountedRef = useRef(true);

  const refresh = useCallback(async () => {
    try {
      const apps = await invoke<AppView[]>("listApps");
      if (!mountedRef.current) return;
      setState((s) => ({
        ...s,
        apps: apps ?? [],
        loading: false,
        error: null,
      }));
    } catch (e) {
      if (!mountedRef.current) return;
      setState((s) => ({
        ...s,
        loading: false,
        error: e instanceof Error ? e.message : String(e),
      }));
    }
  }, []);

  useEffect(() => {
    mountedRef.current = true;
    if (!hasBridge()) {
      setState((s) => ({ ...s, loading: false, connected: false }));
      return;
    }
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
    [refresh, setStatus],
  );

  const stop = useCallback(
    async (id: string) => {
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
    [refresh],
  );

  return { ...state, refresh, launch, stop };
}
