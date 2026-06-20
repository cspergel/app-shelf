import { useEffect, useRef, useState } from "react";
import { X } from "lucide-react";
import { invoke, hasBridge } from "@/lib/bridge";

interface LogPanelProps {
  appId: string;
  appName: string;
  onClose: () => void;
}

const POLL_MS = 2000;
// Auto-scroll only when the user is already near the bottom, so reading scrollback isn't yanked.
const NEAR_BOTTOM_PX = 20;

/**
 * Per-app log panel: a fixed drawer anchored to the bottom of the window that tails the app's
 * captured stdout/stderr. Polls `getLogTail` every ~2s while mounted; auto-scrolls to the newest
 * line when already at the bottom. In mock mode (plain browser, no WebView2) there is no managed
 * process, so it shows the empty state.
 */
export function LogPanel({ appId, appName, onClose }: LogPanelProps) {
  const [lines, setLines] = useState<string[]>([]);
  const mountedRef = useRef(true);
  const scrollRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    mountedRef.current = true;

    // No bridge (mock mode): nothing to tail — leave the empty state in place.
    if (!hasBridge()) {
      return () => {
        mountedRef.current = false;
      };
    }

    let cancelled = false;
    const poll = async () => {
      try {
        const result = await invoke<string[]>("getLogTail", { id: appId });
        if (cancelled || !mountedRef.current) return;
        setLines(result ?? []);
      } catch {
        // A dropped/timed-out tail poll is harmless — keep the last lines, try again next tick.
      }
    };

    void poll();
    const id = window.setInterval(() => void poll(), POLL_MS);
    return () => {
      cancelled = true;
      mountedRef.current = false;
      window.clearInterval(id);
    };
  }, [appId]);

  // Auto-scroll to the newest line, but only when the user is already near the bottom.
  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const nearBottom =
      el.scrollTop + el.clientHeight >= el.scrollHeight - NEAR_BOTTOM_PX;
    if (nearBottom) el.scrollTop = el.scrollHeight;
  }, [lines]);

  return (
    <div className="fixed inset-x-0 bottom-0 z-40 h-[240px] border-t border-hairline bg-surface-window shadow-card-hover animate-slide-up">
      {/* Header */}
      <div className="flex items-center gap-2 border-b border-hairline px-4 py-2">
        <span className="truncate text-xs font-semibold text-text-primary">
          {appName} — Logs
        </span>
        <div className="flex-1" />
        <button
          onClick={onClose}
          title="Close logs"
          className="shrink-0 text-text-faint transition-colors hover:text-text-secondary"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Body */}
      <div
        ref={scrollRef}
        className="h-[calc(240px-37px)] overflow-y-auto px-4 py-2.5"
      >
        {lines.length === 0 ? (
          <p className="text-xs text-text-faint">No output captured yet.</p>
        ) : (
          <pre className="whitespace-pre-wrap break-all font-mono text-xs leading-relaxed text-text-secondary">
            {lines.join("\n")}
          </pre>
        )}
      </div>
    </div>
  );
}
