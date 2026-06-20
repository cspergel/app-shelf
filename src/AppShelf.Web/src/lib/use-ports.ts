import { useCallback, useEffect, useRef, useState } from "react";
import { invoke, hasBridge } from "./bridge";
import {
  getMockPorts,
  mockKillPort,
  mockReclaimPort,
  mockStopService,
} from "./mock-data";
import type {
  PortRowView,
  KillResult,
  ReclaimResult,
  ServiceStopResult,
} from "./types";

interface PortsState {
  ports: PortRowView[];
  loading: boolean;
  error: string | null;
  /** False when running in a plain browser (no WebView2 host). */
  connected: boolean;
}

/**
 * Loads the Port Doctor scan via the C# bridge. Unlike the apps poll this is
 * manual-refresh by default (a scan walks TCP tables + WMI, heavier than a
 * status poll) — the view exposes refresh() and re-scans after each action.
 * When window.chrome.webview is absent, falls back to mock data + no-op actions.
 */
export function usePorts() {
  const bridge = hasBridge();

  const [state, setState] = useState<PortsState>({
    ports: bridge ? [] : getMockPorts(),
    loading: bridge,
    error: null,
    connected: bridge,
  });

  const mountedRef = useRef(true);

  const refresh = useCallback(async () => {
    if (!bridge) {
      if (mountedRef.current) {
        setState((s) => ({ ...s, ports: getMockPorts(), loading: false }));
      }
      return;
    }
    setState((s) => ({ ...s, loading: true }));
    try {
      const ports = await invoke<PortRowView[]>("scanPorts");
      if (!mountedRef.current) return;
      setState((s) => ({ ...s, ports: ports ?? [], loading: false, error: null }));
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
    return () => {
      mountedRef.current = false;
    };
  }, [refresh]);

  const killPort = useCallback(
    async (port: number): Promise<KillResult> => {
      if (!bridge) return mockKillPort(port);
      return invoke<KillResult>("killPort", { port });
    },
    [bridge],
  );

  const reclaimPort = useCallback(
    async (ownerAppId: string, port: number): Promise<ReclaimResult> => {
      if (!bridge) return mockReclaimPort(ownerAppId, port);
      return invoke<ReclaimResult>("reclaimPort", { ownerAppId, port });
    },
    [bridge],
  );

  const stopService = useCallback(
    async (serviceName: string, port: number): Promise<ServiceStopResult> => {
      if (!bridge) return mockStopService(serviceName, port);
      return invoke<ServiceStopResult>("stopService", { serviceName, port });
    },
    [bridge],
  );

  return { ...state, refresh, killPort, reclaimPort, stopService };
}
