import { useCallback, useState } from "react";
import {
  RefreshCw,
  ChevronRight,
  Settings,
  Skull,
  RotateCcw,
  ShieldX,
  CircleDot,
  Copy,
} from "lucide-react";
import { usePorts } from "@/lib/use-ports";
import { ConfirmDialog, type ConfirmRequest } from "./confirm-dialog";
import { Toast, type ToastState } from "./toast";
import { cn } from "@/lib/utils";
import type { PortRowView, PortTier } from "@/lib/types";

// ── Tier pill styling ────────────────────────────────────────────────────────
const TIER_PILL: Record<PortTier, { label: string; className: string }> = {
  Managed: {
    label: "Managed",
    className: "text-accent bg-accent-muted ring-1 ring-accent/20",
  },
  Registered: {
    label: "Registered",
    className: "text-text-secondary bg-surface-elevated/70 ring-1 ring-hairline",
  },
  LikelyOrphaned: {
    label: "Likely orphaned",
    className: "text-status-starting bg-status-starting/12 ring-1 ring-status-starting/25",
  },
  Unknown: {
    label: "Unknown",
    className: "text-text-faint bg-surface-elevated/40 ring-1 ring-hairline",
  },
};

function TierPill({ tier }: { tier: PortTier }) {
  const meta = TIER_PILL[tier] ?? TIER_PILL.Unknown;
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-pill px-2 py-0.5 text-2xs font-semibold tracking-[0.01em]",
        meta.className,
      )}
    >
      {meta.label}
    </span>
  );
}

// ── Port Doctor panel ────────────────────────────────────────────────────────
export function PortDoctor() {
  const { ports, loading, error, connected, refresh, killPort, reclaimPort, stopService } =
    usePorts();

  const [confirm, setConfirm] = useState<ConfirmRequest | null>(null);
  const [toast, setToast] = useState<ToastState | null>(null);
  const [pending, setPending] = useState<(() => void) | null>(null);

  const showToast = useCallback((t: ToastState) => setToast(t), []);

  // Open a confirm modal; on accept run the action.
  const ask = useCallback(
    (request: ConfirmRequest, action: () => void) => {
      setConfirm(request);
      setPending(() => action);
    },
    [setPending],
  );

  const onConfirm = useCallback(() => {
    setConfirm(null);
    pending?.();
    setPending(null);
  }, [pending, setPending]);

  const onCancel = useCallback(() => {
    setConfirm(null);
    setPending(null);
  }, [setPending]);

  // ── Action handlers ────────────────────────────────────────────────────────
  const doKill = useCallback(
    (row: PortRowView) => {
      const prompt: ConfirmRequest = {
        title: `Kill port ${row.port}`,
        danger: true,
        confirmLabel: "Kill",
        message: row.isService
          ? `${row.processName} (PID ${row.pid}) looks like a Windows service. ` +
            `Killing it needs administrator rights and will likely fail.\n\nContinue?`
          : `Kill this process tree and free port ${row.port}?\n\n` +
            `${row.processName} (PID ${row.pid})`,
      };
      ask(prompt, async () => {
        try {
          const r = await killPort(row.port);
          if (r.success) showToast({ kind: "success", message: `Port ${row.port} freed.` });
          else
            showToast({
              kind: "error",
              message: killFailureMessage(row, r.accessDenied),
            });
        } catch (e) {
          showToast({ kind: "error", message: messageOf(e) });
        } finally {
          void refresh();
        }
      });
    },
    [ask, killPort, refresh, showToast],
  );

  const doReclaim = useCallback(
    (row: PortRowView) => {
      if (!row.ownerAppId) return;
      const owner = row.ownerAppName ?? "the app";
      ask(
        {
          title: "Reclaim & relaunch",
          confirmLabel: "Reclaim",
          message: `Kill the orphan on port ${row.port} and relaunch ${owner}?`,
        },
        async () => {
          try {
            const r = await reclaimPort(row.ownerAppId!, row.port);
            if (!r.kill.success) {
              showToast({ kind: "error", message: killFailureMessage(row, r.kill.accessDenied) });
            } else if (r.launch?.status === "Error") {
              showToast({
                kind: "error",
                message: `Port ${row.port} freed, but ${owner} failed to start.\n\n${
                  r.launch.reason ?? "Unknown error."
                }`,
              });
            } else {
              showToast({ kind: "success", message: `Reclaimed port ${row.port} — ${owner} relaunched.` });
            }
          } catch (e) {
            showToast({ kind: "error", message: messageOf(e) });
          } finally {
            void refresh();
          }
        },
      );
    },
    [ask, reclaimPort, refresh, showToast],
  );

  const doStopService = useCallback(
    (row: PortRowView) => {
      if (!row.serviceName) return;
      ask(
        {
          title: `Stop service on port ${row.port}`,
          danger: true,
          confirmLabel: "Stop service",
          message:
            `Stop the Windows service '${row.serviceName}' to free port ${row.port}?\n\n` +
            `You will see a UAC elevation prompt. AppShelf itself does not run as administrator.`,
        },
        async () => {
          try {
            const r = await stopService(row.serviceName!, row.port);
            if (r.cancelled) {
              // User declined UAC — treat as a quiet choice, no toast.
            } else if (!r.started) {
              showToast({
                kind: "error",
                message: r.reason ?? `Could not stop '${row.serviceName}'.`,
              });
            } else if (!r.portFreed) {
              showToast({
                kind: "error",
                message: r.reason ?? `Service stopped but port ${row.port} is still in use.`,
              });
            } else {
              showToast({ kind: "success", message: `Service stopped — port ${row.port} freed.` });
            }
          } catch (e) {
            showToast({ kind: "error", message: messageOf(e) });
          } finally {
            void refresh();
          }
        },
      );
    },
    [ask, stopService, refresh, showToast],
  );

  const orphanCount = ports.filter((p) => p.tier === "LikelyOrphaned").length;

  return (
    <div className="flex h-full flex-col">
      {/* Sub-header — title, scan summary, refresh */}
      <div className="flex items-center gap-3 border-bottom-hairline px-5 py-3 shrink-0">
        <div className="flex items-center gap-2">
          <CircleDot className="h-4 w-4 text-accent" />
          <h2 className="text-md font-semibold text-text-primary tracking-[-0.01em]">
            Port Doctor
          </h2>
        </div>
        {ports.length > 0 && (
          <span className="text-xs text-text-faint">
            {ports.length} {ports.length === 1 ? "listener" : "listeners"}
            {orphanCount > 0 && (
              <span className="text-status-starting">
                {" · "}
                {orphanCount} likely orphaned
              </span>
            )}
          </span>
        )}
        <div className="flex-1" />
        <button
          onClick={() => void refresh()}
          title="Re-scan ports"
          className="inline-flex h-7 items-center gap-1.5 rounded-input px-3 text-xs font-medium text-text-secondary hover:bg-surface-card-hover hover:text-text-primary transition-all duration-[120ms]"
        >
          <RefreshCw className={cn("h-3.5 w-3.5", loading && "animate-spin")} />
          Re-scan
        </button>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-y-auto px-5 py-4">
        {loading && ports.length === 0 ? (
          <PortsSkeleton />
        ) : error ? (
          <div className="flex h-full min-h-[200px] flex-col items-center justify-center gap-2 text-center">
            <ShieldX className="h-7 w-7 text-status-error opacity-60" />
            <p className="text-base font-semibold text-status-error">Scan failed</p>
            <p className="max-w-xs text-xs text-text-secondary">{error}</p>
          </div>
        ) : ports.length === 0 ? (
          <div className="flex h-full min-h-[200px] flex-col items-center justify-center gap-2 text-center">
            <span className="text-3xl opacity-40 select-none">🟢</span>
            <p className="text-base font-semibold text-text-primary">No dev-server ports in use</p>
            <p className="max-w-xs text-xs text-text-secondary">
              Nothing is listening on your registered or common dev ports.
            </p>
          </div>
        ) : (
          <div className="overflow-hidden rounded-card border border-hairline bg-surface-card">
            {ports.map((row, i) => (
              <PortRow
                key={`${row.port}-${row.family}-${row.pid}`}
                row={row}
                first={i === 0}
                onKill={doKill}
                onReclaim={doReclaim}
                onStopService={doStopService}
                onToast={showToast}
              />
            ))}
          </div>
        )}
      </div>

      {!connected && (
        <p className="px-5 pb-2 text-2xs text-text-faint">
          Mock mode — actions are simulated. Live scan + kill run inside AppShelf.
        </p>
      )}

      <ConfirmDialog request={confirm} onConfirm={onConfirm} onCancel={onCancel} />
      <Toast toast={toast} onDismiss={() => setToast(null)} />
    </div>
  );
}

// ── One port row (collapsible) ───────────────────────────────────────────────
function PortRow({
  row,
  first,
  onKill,
  onReclaim,
  onStopService,
  onToast,
}: {
  row: PortRowView;
  first: boolean;
  onKill: (r: PortRowView) => void;
  onReclaim: (r: PortRowView) => void;
  onStopService: (r: PortRowView) => void;
  onToast: (t: ToastState) => void;
}) {
  const [expanded, setExpanded] = useState(false);

  const canReclaim = row.tier === "LikelyOrphaned" && !!row.ownerAppId;
  const canStopService = row.isService && !!row.serviceName;

  return (
    <div className={cn(!first && "border-top-hairline")}>
      {/* Primary row — click anywhere (except buttons) toggles ancestry */}
      <div
        className="group flex cursor-pointer items-center gap-3 px-4 py-2.5 transition-colors duration-[120ms] hover:bg-surface-card-hover"
        onClick={() => setExpanded((e) => !e)}
      >
        {/* Expand chevron */}
        <ChevronRight
          className={cn(
            "h-3.5 w-3.5 shrink-0 text-text-faint transition-transform duration-[120ms]",
            expanded && "rotate-90",
          )}
        />

        {/* Port (mono) + family */}
        <div className="flex w-[88px] shrink-0 items-baseline gap-1.5">
          <span className="font-mono text-sm font-medium text-text-primary tracking-[0.01em]">
            {row.port}
          </span>
          <span className="text-2xs uppercase text-text-faint">{row.family}</span>
        </div>

        {/* Process name + pid */}
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <span className="truncate text-sm text-text-secondary">{row.processName}</span>
          <span className="shrink-0 font-mono text-2xs text-text-faint">pid {row.pid}</span>
          {row.isService && (
            <span
              className="inline-flex shrink-0 items-center gap-1 rounded-chip bg-surface-elevated/70 px-1.5 py-0.5 text-2xs font-medium text-text-secondary ring-1 ring-hairline"
              title="Backed by a Windows service"
            >
              <Settings className="h-2.5 w-2.5" />
              Service
            </span>
          )}
        </div>

        {/* Owner app */}
        {row.ownerAppName && (
          <span className="hidden shrink-0 truncate text-xs text-text-faint sm:inline max-w-[140px]">
            {row.ownerAppName}
          </span>
        )}

        {/* Parent-alive indicator */}
        <span
          className="shrink-0"
          title={row.parentAlive ? "Launcher process alive" : "Launcher process dead"}
        >
          <span
            className={cn(
              "inline-block h-[6px] w-[6px] rounded-full",
              row.parentAlive ? "bg-status-running" : "bg-status-error",
            )}
          />
        </span>

        {/* Tier pill */}
        <div className="shrink-0">
          <TierPill tier={row.tier} />
        </div>

        {/* Actions — stop click propagation so they don't toggle expand */}
        <div
          className="flex shrink-0 items-center gap-0.5"
          onClick={(e) => e.stopPropagation()}
        >
          {canReclaim && (
            <button
              onClick={() => onReclaim(row)}
              title="Kill the orphan and relaunch the owning app"
              className="inline-flex items-center gap-1 rounded-[5px] px-2 py-1 text-xs font-medium text-accent transition-colors duration-[120ms] hover:bg-accent-muted active:scale-[0.97]"
            >
              <RotateCcw className="h-3 w-3" />
              Reclaim
            </button>
          )}
          {canStopService && (
            <button
              onClick={() => onStopService(row)}
              title="Stop the Windows service (needs UAC elevation)"
              className="inline-flex items-center gap-1 rounded-[5px] px-2 py-1 text-xs font-medium text-text-secondary transition-colors duration-[120ms] hover:bg-surface-elevated active:scale-[0.97]"
            >
              <Settings className="h-3 w-3" />
              Stop service
            </button>
          )}
          <button
            onClick={() => onKill(row)}
            title="Kill the process tree and free the port"
            className="inline-flex items-center gap-1 rounded-[5px] px-2 py-1 text-xs font-medium text-status-error/80 transition-colors duration-[120ms] hover:bg-status-error/10 hover:text-status-error active:scale-[0.97]"
          >
            <Skull className="h-3 w-3" />
            Kill
          </button>
        </div>
      </div>

      {/* Ancestry expand-row */}
      {expanded && <AncestryPanel row={row} onToast={onToast} />}
    </div>
  );
}

// ── Ancestry chain panel ─────────────────────────────────────────────────────
function AncestryPanel({ row, onToast }: { row: PortRowView; onToast: (t: ToastState) => void }) {
  // Root node (the port owner) followed by its parent chain — mirrors the WPF row.
  const nodes = [
    {
      pid: row.pid,
      processName: row.processName,
      alive: true,
      commandLine: row.commandLine,
      exeDir: row.exeDir,
      root: true,
    },
    ...row.ancestry.map((a) => ({ ...a, root: false })),
  ];

  // Build the same plaintext the panel shows, for the Copy button.
  const ancestryText = (): string => {
    const lines: string[] = [];
    lines.push(`Port ${row.port} (${row.family}) — ${row.processName} pid ${row.pid}`);
    lines.push(
      `Started: ${row.startedAt ? new Date(row.startedAt).toLocaleString() : "unknown"}`,
    );
    if (row.exePath) lines.push(`Exe: ${row.exePath}`);
    lines.push("");
    lines.push("Process ancestry:");
    nodes.forEach((n, i) => {
      const marker = i === 0 ? "•" : "  ↳";
      const dead = n.alive ? "" : " [dead]";
      lines.push(`${marker} ${n.processName} pid ${n.pid}${dead}`);
      if (n.commandLine) lines.push(`    cmd: ${n.commandLine}`);
      if (n.exeDir) lines.push(`    exe dir: ${n.exeDir}`);
    });
    return lines.join("\n");
  };

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(ancestryText());
      onToast({ kind: "success", message: "Copied" });
    } catch (e) {
      onToast({ kind: "error", message: messageOf(e) });
    }
  };

  return (
    <div
      className="animate-slide-down select-text border-top-hairline bg-surface-inset/40 px-5 py-3"
      style={{ userSelect: "text", WebkitUserSelect: "text" }}
    >
      <div className="mb-2 flex items-center gap-3 text-2xs text-text-faint">
        <span>
          Started:{" "}
          <span className="font-mono text-text-secondary">
            {row.startedAt ? new Date(row.startedAt).toLocaleString() : "unknown"}
          </span>
        </span>
        {row.exePath && (
          <span className="truncate">
            Exe: <span className="font-mono text-text-secondary">{row.exePath}</span>
          </span>
        )}
        <div className="flex-1" />
        <button
          onClick={copy}
          title="Copy the full ancestry chain to the clipboard"
          className="inline-flex shrink-0 items-center gap-1 rounded-[5px] px-2 py-1 text-2xs font-medium text-text-secondary transition-colors duration-[120ms] hover:bg-surface-card-hover hover:text-text-primary active:scale-[0.97] select-none"
        >
          <Copy className="h-3 w-3" />
          Copy
        </button>
      </div>

      <div className="text-2xs font-medium uppercase tracking-[0.04em] text-text-faint mb-1.5 select-none">
        Process ancestry
      </div>
      <div className="space-y-1.5">
        {nodes.map((n, i) => (
          <div key={`${n.pid}-${i}`} className="flex gap-2">
            {/* Tree connector */}
            <span className="select-none pt-px font-mono text-2xs text-text-faint">
              {i === 0 ? "•" : "↳"}
            </span>
            <div className="min-w-0 flex-1">
              <div className="flex items-baseline gap-1.5">
                <span
                  className={cn(
                    "font-mono text-xs",
                    n.alive ? "text-text-primary" : "text-text-faint line-through",
                  )}
                >
                  {n.processName}
                </span>
                <span className="font-mono text-2xs text-text-faint">pid {n.pid}</span>
                {!n.alive && (
                  <span className="text-2xs font-semibold text-status-error">[dead]</span>
                )}
              </div>
              {n.commandLine && (
                <p className="mt-0.5 truncate font-mono text-2xs text-text-faint">
                  cmd: {n.commandLine}
                </p>
              )}
              {n.exeDir && (
                <p className="truncate font-mono text-2xs text-text-faint">
                  exe dir: {n.exeDir}
                </p>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Skeleton ─────────────────────────────────────────────────────────────────
function PortsSkeleton() {
  return (
    <div className="overflow-hidden rounded-card border border-hairline bg-surface-card">
      {Array.from({ length: 4 }).map((_, i) => (
        <div
          key={i}
          className={cn("h-[44px] animate-pulse bg-surface-card-hover/40", i > 0 && "border-top-hairline")}
          style={{ animationDelay: `${i * 60}ms` }}
        />
      ))}
    </div>
  );
}

// ── Helpers ──────────────────────────────────────────────────────────────────
function killFailureMessage(row: PortRowView, accessDenied: boolean): string {
  if (accessDenied && row.isService)
    return (
      `Couldn't free port ${row.port}: ${row.processName} (PID ${row.pid}) is a Windows ` +
      `service and can't be stopped without administrator rights.`
    );
  if (accessDenied)
    return (
      `Couldn't free port ${row.port}: access denied stopping ${row.processName} (PID ${row.pid}). ` +
      `It may be a protected or elevated process.`
    );
  return `Couldn't free port ${row.port}: ${row.processName} (PID ${row.pid}) could not be stopped.`;
}

function messageOf(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
