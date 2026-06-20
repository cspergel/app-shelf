import { useEffect } from "react";
import { AlertTriangle } from "lucide-react";
import { cn } from "@/lib/utils";

export interface ConfirmRequest {
  title: string;
  /** Body text. Newlines render as line breaks. */
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Destructive actions get a red confirm button. */
  danger?: boolean;
}

interface ConfirmDialogProps {
  request: ConfirmRequest | null;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * In-app confirm modal in the Ostara design — replaces window.confirm (which is
 * ugly and unreliable inside WebView2). Renders nothing when request is null.
 * Esc cancels, Enter confirms.
 */
export function ConfirmDialog({ request, onConfirm, onCancel }: ConfirmDialogProps) {
  useEffect(() => {
    if (!request) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel();
      else if (e.key === "Enter") onConfirm();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [request, onConfirm, onCancel]);

  if (!request) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-6 animate-fade-up"
      onClick={onCancel}
    >
      <div
        className={cn(
          "w-full max-w-md rounded-card border border-hairline bg-surface-card",
          "shadow-card-hover overflow-hidden",
        )}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start gap-3 px-5 pt-5">
          {request.danger && (
            <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-input bg-status-error/15">
              <AlertTriangle className="h-3.5 w-3.5 text-status-error" />
            </div>
          )}
          <div className="min-w-0 flex-1">
            <h2 className="text-md font-semibold text-text-primary tracking-[-0.01em]">
              {request.title}
            </h2>
            <p className="mt-2 whitespace-pre-line text-xs leading-relaxed text-text-secondary">
              {request.message}
            </p>
          </div>
        </div>

        <div className="mt-5 flex items-center justify-end gap-2 border-top-hairline px-5 py-3">
          <button
            onClick={onCancel}
            className="btn-ghost-action text-text-secondary"
          >
            {request.cancelLabel ?? "Cancel"}
          </button>
          <button
            onClick={onConfirm}
            className={cn(
              "inline-flex items-center gap-1.5 px-3.5 py-1 rounded-[5px]",
              "text-xs font-semibold tracking-[-0.01em] text-white",
              "transition-all duration-[120ms] ease-out-expo active:scale-[0.97]",
              "shadow-[0_1px_2px_rgba(0,0,0,0.4)]",
              request.danger
                ? "bg-status-error/85 hover:bg-status-error"
                : "bg-accent hover:bg-accent-hover",
            )}
          >
            {request.confirmLabel ?? "Confirm"}
          </button>
        </div>
      </div>
    </div>
  );
}
