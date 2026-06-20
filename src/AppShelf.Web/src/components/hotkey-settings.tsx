import { useCallback, useEffect, useRef, useState } from "react";
import { Keyboard, X, Loader2 } from "lucide-react";
import { invoke, hasBridge } from "@/lib/bridge";
import { cn } from "@/lib/utils";

interface HotkeySettingsProps {
  open: boolean;
  onClose: () => void;
  onToast: (kind: "success" | "error", message: string) => void;
}

interface GetHotkeyResult {
  combo: string | null;
  activeHotkey: string | null;
}

interface SetHotkeyResult {
  ok: boolean;
  activeHotkey: string | null;
}

const DEFAULT_LABEL = "Default (Alt+Space)";
const PROMPT_LABEL = "Press a shortcut…";

/**
 * Spotlight global-hotkey settings panel (web replacement for the deleted HotkeyWindow WPF
 * dialog). The capture box swallows keystrokes and shows the formatted combo; Save persists +
 * live-registers via the `setHotkey` bridge method. A rejected combo (already owned by another
 * app) shows an inline message and keeps the panel open. "Use default" clears the choice → the
 * built-in Alt+Space chain. Mirrors HotkeyWindow.xaml.cs behavior (require ≥1 modifier, swallow
 * lone modifier presses).
 */
export function HotkeySettings({ open, onClose, onToast }: HotkeySettingsProps) {
  // The combo captured this session: a string, or null when "Use default" is chosen.
  const [captured, setCaptured] = useState<string | null>(null);
  const [hasCapture, setHasCapture] = useState(false);
  const [displayText, setDisplayText] = useState(PROMPT_LABEL);
  const [activeHotkey, setActiveHotkey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const captureRef = useRef<HTMLButtonElement>(null);

  // Load the current hotkey when the panel opens.
  useEffect(() => {
    if (!open) return;
    setCaptured(null);
    setHasCapture(false);
    setError(null);
    setBusy(false);

    if (!hasBridge()) {
      // Mock / plain-browser mode: nothing to read.
      setActiveHotkey("Alt+Space");
      setDisplayText("Alt+Space");
      return;
    }

    void invoke<GetHotkeyResult>("getHotkey")
      .then((res) => {
        setActiveHotkey(res.activeHotkey);
        setDisplayText(
          res.combo && res.combo.trim().length > 0 ? res.combo : (res.activeHotkey ?? PROMPT_LABEL),
        );
      })
      .catch((e) => {
        setError(e instanceof Error ? e.message : String(e));
        setDisplayText(PROMPT_LABEL);
      });

    const t = window.setTimeout(() => captureRef.current?.focus(), 30);
    return () => window.clearTimeout(t);
  }, [open]);

  // Capture a key combo. Swallows all keys (this is a capture surface, not a text field).
  // Requires at least one modifier; ignores lone modifier presses. Mirrors HotkeyWindow.
  const onCaptureKeyDown = useCallback((e: React.KeyboardEvent) => {
    e.preventDefault();
    e.stopPropagation();

    const key = e.key;
    if (isModifierKey(key)) return; // wait for a real key

    const hasModifier = e.ctrlKey || e.altKey || e.shiftKey || e.metaKey;
    if (!hasModifier) {
      setError("Add a modifier (Ctrl, Alt, Shift, or Win) to the key.");
      return;
    }

    const combo = formatCombo(e);
    setCaptured(combo);
    setHasCapture(true);
    setDisplayText(combo);
    setError(null);
  }, []);

  const useDefault = useCallback(() => {
    setCaptured(null); // null = revert to the built-in default chain
    setHasCapture(true);
    setDisplayText(DEFAULT_LABEL);
    setError(null);
  }, []);

  const handleSave = useCallback(async () => {
    // Nothing changed this session — treat Save as a no-op close.
    if (!hasCapture) {
      onClose();
      return;
    }

    if (!hasBridge()) {
      onToast("success", "Saved");
      onClose();
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const res = await invoke<SetHotkeyResult>("setHotkey", { combo: captured });
      if (res.ok) {
        setActiveHotkey(res.activeHotkey);
        onToast("success", "Shortcut saved");
        onClose();
      } else {
        setError("That shortcut is already in use by another app — try another.");
        setBusy(false);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      setBusy(false);
    }
  }, [hasCapture, captured, onClose, onToast]);

  // Esc closes (when not busy).
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape" && !busy) onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, busy, onClose]);

  if (!open) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/55 p-6 animate-fade-up"
      onClick={() => !busy && onClose()}
    >
      <div
        className="flex w-full max-w-md flex-col overflow-hidden rounded-card border border-hairline bg-surface-card shadow-card-hover"
        onClick={(e) => e.stopPropagation()}
      >
        {/* ── Header ───────────────────────────────────────────────────────── */}
        <div className="flex items-center justify-between border-b border-hairline px-5 py-3.5">
          <div className="flex items-center gap-2">
            <div className="flex h-6 w-6 items-center justify-center rounded-[6px] bg-accent-muted ring-1 ring-accent/20">
              <Keyboard className="h-3.5 w-3.5 text-accent" />
            </div>
            <h2 className="text-md font-semibold text-text-primary tracking-[-0.01em]">
              Spotlight shortcut
            </h2>
          </div>
          <button
            onClick={() => !busy && onClose()}
            className="text-text-faint transition-colors hover:text-text-secondary"
            title="Close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* ── Body ─────────────────────────────────────────────────────────── */}
        <div className="flex flex-col gap-3.5 px-5 py-4">
          <p className="text-xs leading-relaxed text-text-secondary">
            Choose the global shortcut that opens the Spotlight search overlay. Press a key
            combination including at least one modifier (Ctrl, Alt, Shift, or Win).
          </p>

          {/* Currently active */}
          <div className="flex items-center justify-between rounded-input border border-hairline bg-surface-elevated px-3 py-2">
            <span className="text-2xs font-medium uppercase tracking-[0.04em] text-text-faint">
              Currently active
            </span>
            <span className="text-xs font-mono text-text-primary">
              {activeHotkey ?? "none"}
            </span>
          </div>

          {/* Capture box */}
          <label className="flex flex-col gap-1">
            <span className="text-2xs font-medium uppercase tracking-[0.04em] text-text-faint">
              New shortcut
            </span>
            <button
              ref={captureRef}
              type="button"
              onKeyDown={onCaptureKeyDown}
              className={cn(
                "flex h-10 w-full items-center justify-center rounded-input border px-3",
                "text-sm font-mono text-text-primary outline-none transition-all duration-[120ms]",
                "border-hairline bg-surface-elevated",
                "focus:border-accent/50 focus:shadow-accent-glow/30",
              )}
            >
              {displayText}
            </button>
          </label>

          {error && (
            <p className="text-xs leading-relaxed text-status-error">{error}</p>
          )}
        </div>

        {/* ── Footer ───────────────────────────────────────────────────────── */}
        <div className="flex items-center justify-between border-t border-hairline px-5 py-3">
          <button
            onClick={useDefault}
            disabled={busy}
            className="btn-ghost-action text-text-secondary disabled:opacity-60"
          >
            Use default
          </button>
          <div className="flex items-center gap-2">
            <button
              onClick={() => !busy && onClose()}
              className="btn-ghost-action text-text-secondary"
            >
              Cancel
            </button>
            <button
              onClick={() => void handleSave()}
              disabled={busy}
              className={cn(
                "inline-flex items-center gap-1.5 rounded-[5px] px-3.5 py-1",
                "text-xs font-semibold tracking-[-0.01em] text-white",
                "bg-accent hover:bg-accent-hover shadow-[0_1px_2px_rgba(0,0,0,0.4)]",
                "transition-all duration-[120ms] ease-out-expo active:scale-[0.97]",
                "disabled:opacity-60 disabled:cursor-not-allowed",
              )}
            >
              {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              Save
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Capture helpers (mirror HotkeyCombo.Format / IsModifierKey) ──────────────────

/** Lone modifier key names from KeyboardEvent.key — ignored until a real key joins them. */
function isModifierKey(key: string): boolean {
  return key === "Control" || key === "Alt" || key === "Shift" || key === "Meta";
}

/**
 * Format a captured key event into a combo string like "Ctrl+J" or "Ctrl+Alt+Space".
 * Order matches the WPF HotkeyCombo convention: Ctrl, Alt, Shift, Win, then the key.
 */
function formatCombo(e: React.KeyboardEvent): string {
  const parts: string[] = [];
  if (e.ctrlKey) parts.push("Ctrl");
  if (e.altKey) parts.push("Alt");
  if (e.shiftKey) parts.push("Shift");
  if (e.metaKey) parts.push("Win");
  parts.push(formatKey(e.key, e.code));
  return parts.join("+");
}

/** Normalize a KeyboardEvent key/code to a display token (single letters uppercased, Space, etc.). */
function formatKey(key: string, code: string): string {
  if (key === " ") return "Space";
  if (key.length === 1) return key.toUpperCase();
  // Digit codes (e.g. "Digit5") → the bare digit so Ctrl+5 reads naturally.
  if (code.startsWith("Digit")) return code.slice(5);
  if (code.startsWith("Key")) return code.slice(3);
  return key;
}
