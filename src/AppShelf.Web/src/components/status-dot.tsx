import { cn } from "@/lib/utils";
import type { LaunchStatus } from "@/lib/types";

export interface StatusMeta {
  label: string;
  labelLong: string;
  dotClass: string;
  textClass: string;
  railClass: string;
}

const STATUS_META: Record<LaunchStatus, StatusMeta> = {
  Running: {
    label: "Running",
    labelLong: "Running",
    dotClass: "bg-status-running",
    textClass: "text-status-running",
    railClass: "bg-status-running",
  },
  Starting: {
    label: "Starting",
    labelLong: "Starting…",
    dotClass: "bg-status-starting",
    textClass: "text-status-starting",
    railClass: "bg-status-starting",
  },
  Error: {
    label: "Error",
    labelLong: "Error",
    dotClass: "bg-status-error",
    textClass: "text-status-error",
    railClass: "bg-status-error",
  },
  PortInUse: {
    label: "Port in use",
    labelLong: "Port in use",
    dotClass: "bg-status-port",
    textClass: "text-status-error",
    railClass: "bg-status-error",
  },
  StoppedUnexpectedly: {
    label: "Crashed",
    labelLong: "Stopped unexpectedly",
    dotClass: "bg-status-crash",
    textClass: "text-status-crash",
    railClass: "bg-status-crash",
  },
  Stopped: {
    label: "Stopped",
    labelLong: "Stopped",
    dotClass: "bg-status-stopped",
    textClass: "text-text-faint",
    railClass: "bg-status-stopped",
  },
};

export function statusMeta(status: LaunchStatus): StatusMeta {
  return STATUS_META[status] ?? STATUS_META.Stopped;
}

/** 6 px solid dot — no outer halo. Starting pulses opacity. Running breathes scale. */
export function StatusDot({ status, className }: { status: LaunchStatus; className?: string }) {
  const meta = statusMeta(status);
  return (
    <span
      className={cn(
        "inline-block h-[6px] w-[6px] rounded-full flex-shrink-0",
        meta.dotClass,
        status === "Starting" && "animate-pulse-dot",
        status === "Running" && "animate-breathe",
        className,
      )}
    />
  );
}

/** Full-height left rail — the primary structural status signal on a card. */
export function StatusRail({ status, className }: { status: LaunchStatus; className?: string }) {
  const meta = statusMeta(status);
  return (
    <span
      className={cn(
        "absolute left-0 top-0 bottom-0 w-[3px] rounded-l-card animate-rail-grow",
        meta.railClass,
        // Dim the rail for inactive states so it doesn't compete
        (status === "Stopped") && "opacity-25",
        className,
      )}
    />
  );
}
