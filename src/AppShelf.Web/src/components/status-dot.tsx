import { cn } from "@/lib/utils";
import type { LaunchStatus } from "@/lib/types";

const STATUS_META: Record<
  LaunchStatus,
  { label: string; dot: string; text: string }
> = {
  Running: { label: "Running", dot: "bg-status-running", text: "text-status-running" },
  Starting: { label: "Starting", dot: "bg-status-starting", text: "text-status-starting" },
  Error: { label: "Error", dot: "bg-status-error", text: "text-status-error" },
  PortInUse: { label: "Port in use", dot: "bg-status-error", text: "text-status-error" },
  Stopped: { label: "Stopped", dot: "bg-status-stopped", text: "text-text-faint" },
};

export function statusMeta(status: LaunchStatus) {
  return STATUS_META[status] ?? STATUS_META.Stopped;
}

export function StatusDot({ status }: { status: LaunchStatus }) {
  const meta = statusMeta(status);
  return (
    <span className="relative flex h-[7px] w-[7px]">
      {status === "Running" && (
        <span
          className={cn(
            "absolute inline-flex h-full w-full rounded-full opacity-40",
            meta.dot,
            "animate-ping",
          )}
        />
      )}
      <span
        className={cn(
          "relative inline-flex h-[7px] w-[7px] rounded-full",
          meta.dot,
          status === "Starting" && "animate-pulse-dot",
        )}
      />
    </span>
  );
}
