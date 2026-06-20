import { useEffect } from "react";
import { CheckCircle2, XCircle, X } from "lucide-react";
import { cn } from "@/lib/utils";

export interface ToastState {
  kind: "success" | "error";
  message: string;
}

interface ToastProps {
  toast: ToastState | null;
  onDismiss: () => void;
}

/** Bottom-center inline result message in the Ostara design. Auto-dismisses. */
export function Toast({ toast, onDismiss }: ToastProps) {
  useEffect(() => {
    if (!toast) return;
    // Errors linger longer (the user needs to read the reason).
    const ms = toast.kind === "error" ? 6000 : 3000;
    const id = window.setTimeout(onDismiss, ms);
    return () => window.clearTimeout(id);
  }, [toast, onDismiss]);

  if (!toast) return null;

  const success = toast.kind === "success";

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-5 z-50 flex justify-center px-6">
      <div
        className={cn(
          "pointer-events-auto flex max-w-md items-start gap-2.5 rounded-card",
          "border bg-surface-elevated px-4 py-3 shadow-card-hover animate-fade-up",
          success ? "border-status-running/30" : "border-status-error/30",
        )}
      >
        {success ? (
          <CheckCircle2 className="mt-px h-4 w-4 shrink-0 text-status-running" />
        ) : (
          <XCircle className="mt-px h-4 w-4 shrink-0 text-status-error" />
        )}
        <p className="whitespace-pre-line text-xs leading-relaxed text-text-primary">
          {toast.message}
        </p>
        <button
          onClick={onDismiss}
          className="mt-px shrink-0 text-text-faint transition-colors hover:text-text-secondary"
          title="Dismiss"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  );
}
