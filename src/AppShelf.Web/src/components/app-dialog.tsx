import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { FolderOpen, Sparkles, Star, X, Loader2 } from "lucide-react";
import {
  addApp,
  updateApp,
  pickFolder,
  detectStack,
  listGroups,
  reservedPorts,
  type AppFormPayload,
} from "@/lib/app-actions";
import { cn } from "@/lib/utils";
import type { AppView } from "@/lib/types";

interface AppDialogProps {
  /** Null = add mode; an app = edit mode (prefilled). */
  app: AppView | null;
  open: boolean;
  onClose: () => void;
  /** Called after a successful save with a toast message. Caller refreshes the grid. */
  onSaved: (message: string) => void;
}

/** Local form state — strings throughout (port stays a string until submit). */
interface FormState {
  name: string;
  dir: string;
  cmd: string;
  url: string;
  installCmd: string;
  framework: string;
  tags: string;
  group: string;
  role: string;
  open: string;
  favorite: boolean;
  port: string;
  portFixed: boolean;
}

const EMPTY: FormState = {
  name: "",
  dir: "",
  cmd: "",
  url: "",
  installCmd: "",
  framework: "",
  tags: "",
  group: "",
  role: "other",
  open: "browser",
  favorite: false,
  port: "",
  portFixed: false,
};

function fromApp(app: AppView): FormState {
  return {
    name: app.name,
    dir: app.dir ?? "",
    cmd: "", // not in AppView projection — left blank, user re-detects or types
    url: app.url,
    installCmd: "",
    framework: app.framework ?? "",
    tags: (app.tags ?? []).join(", "),
    group: app.group ?? "",
    role: app.role || "other",
    open: "browser",
    favorite: app.favorite,
    port: app.port != null ? String(app.port) : "",
    portFixed: false,
  };
}

/**
 * Add / Edit app modal in the Ostara design. One component for both modes: an `app`
 * prop prefills (edit), null = blank (add). Backdrop + Esc + outside-click close.
 * Browse → native folder pick → auto-detect stack (cmd/url/port/framework/install),
 * all user-overridable. Name is the only hard requirement; an app with no command is
 * treated as URL-only (mirrors the engine's IsUrlOnly).
 */
export function AppDialog({ app, open, onClose, onSaved }: AppDialogProps) {
  const isEdit = app !== null;
  const [form, setForm] = useState<FormState>(EMPTY);
  const [error, setError] = useState<string | null>(null);
  const [detectHint, setDetectHint] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [detecting, setDetecting] = useState(false);
  const [groups, setGroups] = useState<string[]>([]);
  const [takenPorts, setTakenPorts] = useState<number[]>([]);
  const nameRef = useRef<HTMLInputElement>(null);

  // (Re)initialize whenever the dialog opens or the target app changes.
  useEffect(() => {
    if (!open) return;
    setForm(app ? fromApp(app) : EMPTY);
    setError(null);
    setDetectHint(null);
    setBusy(false);
    setDetecting(false);
    void listGroups().then(setGroups).catch(() => setGroups([]));
    void reservedPorts(app?.id).then(setTakenPorts).catch(() => setTakenPorts([]));
    // Focus the name field shortly after mount.
    const t = window.setTimeout(() => nameRef.current?.focus(), 30);
    return () => window.clearTimeout(t);
  }, [open, app]);

  const set = useCallback(<K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((f) => ({ ...f, [key]: value }));
  }, []);

  // URL-only when there's no command (mirrors the engine). Dir/cmd/install/port hidden.
  const urlOnly = form.cmd.trim().length === 0;

  // Port-collision hint (non-blocking; C# is authoritative on save).
  const portCollision = useMemo(() => {
    const p = Number(form.port);
    return form.port.trim() !== "" && Number.isInteger(p) && takenPorts.includes(p);
  }, [form.port, takenPorts]);

  const handleBrowse = useCallback(async () => {
    setError(null);
    try {
      const path = await pickFolder(form.dir || null);
      if (!path) return; // cancelled
      setForm((f) => ({ ...f, dir: path, name: f.name || baseName(path) }));
      await runDetect(path);
    } catch (e) {
      setError(messageOf(e));
    }
  }, [form.dir]);

  const runDetect = useCallback(async (dir: string) => {
    if (!dir.trim()) {
      setError("Choose a folder first.");
      return;
    }
    setDetecting(true);
    setError(null);
    setDetectHint(null);
    try {
      const d = await detectStack(dir);
      if (!d.detected) {
        setDetectHint(
          "No known framework detected — enter the command and URL yourself (or leave the command blank for a URL-only launcher).",
        );
        return;
      }
      setForm((f) => ({
        ...f,
        cmd: d.cmd ?? f.cmd,
        url: d.url ?? f.url,
        installCmd: f.installCmd || (d.installCmd ?? ""),
        framework: d.framework ?? f.framework,
        port: d.port != null ? String(d.port) : f.port,
        // Run from the matched subfolder (e.g. frontend/) when detection found one.
        dir: d.dir && d.dir !== dir ? d.dir : f.dir,
      }));
      const where = d.dir && d.dir !== dir ? ` in ${baseName(d.dir)}/` : "";
      setDetectHint(`Detected ${d.framework}${where} — fields autofilled (edit as needed).`);
    } catch (e) {
      setError(messageOf(e));
    } finally {
      setDetecting(false);
    }
  }, []);

  const handleSave = useCallback(async () => {
    setError(null);
    if (form.name.trim().length === 0) {
      setError("Name is required.");
      nameRef.current?.focus();
      return;
    }
    const payload: AppFormPayload = {
      id: app?.id,
      name: form.name.trim(),
      dir: urlOnly ? null : form.dir.trim(),
      cmd: urlOnly ? null : form.cmd.trim(),
      url: form.url.trim(),
      installCmd: urlOnly ? null : form.installCmd.trim() || null,
      framework: form.framework.trim() || null,
      tags: form.tags
        .split(",")
        .map((t) => t.trim())
        .filter((t) => t.length > 0),
      group: form.group.trim() || null,
      role: form.role,
      open: form.open,
      favorite: form.favorite,
      port: urlOnly ? null : form.port.trim() || null,
      portFixed: form.portFixed,
    };

    setBusy(true);
    try {
      const saved = isEdit ? await updateApp(payload) : await addApp(payload);
      onSaved(isEdit ? `Saved "${saved.name}"` : `Added "${saved.name}"`);
      onClose();
    } catch (e) {
      setError(messageOf(e));
      setBusy(false);
    }
  }, [app, form, isEdit, urlOnly, onClose, onSaved]);

  // Esc closes (when not busy). Enter does NOT submit — the form is long and Enter in a
  // text field would be surprising; the Save button is the explicit action.
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
        className="flex max-h-[88vh] w-full max-w-lg flex-col overflow-hidden rounded-card border border-hairline bg-surface-card shadow-card-hover"
        onClick={(e) => e.stopPropagation()}
      >
        {/* ── Header ───────────────────────────────────────────────────────── */}
        <div className="flex items-center justify-between border-b border-hairline px-5 py-3.5">
          <h2 className="text-md font-semibold text-text-primary tracking-[-0.01em]">
            {isEdit ? "Edit app" : "Add app"}
          </h2>
          <button
            onClick={() => !busy && onClose()}
            className="text-text-faint transition-colors hover:text-text-secondary"
            title="Close"
          >
            <X className="h-4 w-4" />
          </button>
        </div>

        {/* ── Scrollable body ──────────────────────────────────────────────── */}
        <div className="flex-1 overflow-y-auto px-5 py-4">
          <div className="flex flex-col gap-3.5">
            <Field label="Name" required>
              <input
                ref={nameRef}
                value={form.name}
                onChange={(e) => set("name", e.target.value)}
                placeholder="My Project"
                className={inputClass}
              />
            </Field>

            {/* Folder + Browse / Detect — hidden for URL-only apps */}
            {!urlOnly && (
              <Field
                label="Folder"
                hint="The directory the command runs in."
              >
                <div className="flex gap-2">
                  <input
                    value={form.dir}
                    onChange={(e) => set("dir", e.target.value)}
                    placeholder="C:\dev\my-project"
                    className={cn(inputClass, "flex-1 font-mono")}
                  />
                  <button
                    onClick={() => void handleBrowse()}
                    className={ghostBtnClass}
                    title="Browse for a folder"
                  >
                    <FolderOpen className="h-3.5 w-3.5" />
                    Browse
                  </button>
                  <button
                    onClick={() => void runDetect(form.dir)}
                    disabled={detecting}
                    className={ghostBtnClass}
                    title="Detect framework, command and port"
                  >
                    {detecting ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    ) : (
                      <Sparkles className="h-3.5 w-3.5" />
                    )}
                    Detect
                  </button>
                </div>
              </Field>
            )}

            {detectHint && (
              <p className="-mt-1.5 flex items-start gap-1.5 text-2xs text-accent">
                <Sparkles className="mt-px h-3 w-3 shrink-0" />
                {detectHint}
              </p>
            )}

            <Field
              label="Command"
              hint="Leave blank for a URL-only launcher (just opens the URL)."
            >
              <input
                value={form.cmd}
                onChange={(e) => set("cmd", e.target.value)}
                placeholder="npm run dev"
                className={cn(inputClass, "font-mono")}
              />
            </Field>

            <Field label="URL" required={urlOnly}>
              <input
                value={form.url}
                onChange={(e) => set("url", e.target.value)}
                placeholder="http://localhost:5173"
                className={cn(inputClass, "font-mono")}
              />
            </Field>

            {!urlOnly && (
              <Field label="Install command" hint="Optional — e.g. for one-click setup.">
                <input
                  value={form.installCmd}
                  onChange={(e) => set("installCmd", e.target.value)}
                  placeholder="npm install"
                  className={cn(inputClass, "font-mono")}
                />
              </Field>
            )}

            <div className="grid grid-cols-2 gap-3">
              <Field label="Framework">
                <input
                  value={form.framework}
                  onChange={(e) => set("framework", e.target.value)}
                  placeholder="Vite · React"
                  className={inputClass}
                />
              </Field>

              {!urlOnly && (
                <Field
                  label="Port"
                  hint={
                    portCollision
                      ? undefined
                      : "Blank = auto-assign a free port."
                  }
                >
                  <input
                    value={form.port}
                    onChange={(e) => set("port", e.target.value)}
                    placeholder="auto"
                    inputMode="numeric"
                    className={cn(
                      inputClass,
                      "font-mono",
                      portCollision && "border-status-error/50",
                    )}
                  />
                  {portCollision && (
                    <p className="mt-1 text-2xs text-status-error">
                      Port {form.port} is already reserved by another app.
                    </p>
                  )}
                </Field>
              )}
            </div>

            <Field label="Tags" hint="Comma-separated (e.g. #personal, #ml).">
              <input
                value={form.tags}
                onChange={(e) => set("tags", e.target.value)}
                placeholder="#personal, #web"
                className={inputClass}
              />
            </Field>

            <div className="grid grid-cols-2 gap-3">
              <Field
                label="Group"
                hint="Apps sharing a name show as one card."
              >
                <input
                  value={form.group}
                  onChange={(e) => set("group", e.target.value)}
                  placeholder="(none)"
                  list="appshelf-groups"
                  className={inputClass}
                />
                <datalist id="appshelf-groups">
                  {groups.map((g) => (
                    <option key={g} value={g} />
                  ))}
                </datalist>
              </Field>

              <Field label="Role">
                <select
                  value={form.role}
                  onChange={(e) => set("role", e.target.value)}
                  className={selectClass}
                >
                  <option value="other">other</option>
                  <option value="backend">backend</option>
                  <option value="frontend">frontend</option>
                </select>
              </Field>
            </div>

            <div className="grid grid-cols-2 gap-3">
              <Field label="Open target">
                <select
                  value={form.open}
                  onChange={(e) => set("open", e.target.value)}
                  className={selectClass}
                >
                  <option value="browser">browser</option>
                  <option value="webview" disabled>
                    webview (coming soon)
                  </option>
                </select>
              </Field>

              <Field label="Favorite">
                <button
                  type="button"
                  onClick={() => set("favorite", !form.favorite)}
                  className={cn(
                    "flex h-[30px] items-center gap-2 rounded-input border px-3 text-xs font-medium transition-all duration-[120ms]",
                    form.favorite
                      ? "border-[#E5C04B]/40 bg-[#E5C04B]/10 text-[#E5C04B]"
                      : "border-hairline bg-surface-elevated text-text-faint hover:text-text-secondary",
                  )}
                >
                  <Star
                    className="h-3.5 w-3.5"
                    fill={form.favorite ? "currentColor" : "none"}
                  />
                  {form.favorite ? "Favorited" : "Not favorited"}
                </button>
              </Field>
            </div>
          </div>
        </div>

        {/* ── Footer: error + actions ──────────────────────────────────────── */}
        <div className="border-t border-hairline px-5 py-3">
          {error && (
            <p className="mb-2.5 text-xs leading-relaxed text-status-error">{error}</p>
          )}
          <div className="flex items-center justify-end gap-2">
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
              {isEdit ? "Save changes" : "Add app"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Small presentational helpers ──────────────────────────────────────────────

const inputClass =
  "h-[30px] w-full rounded-input border border-hairline bg-surface-elevated px-2.5 text-xs text-text-primary placeholder:text-text-faint outline-none transition-all duration-[120ms] focus:border-accent/40 focus:bg-surface-elevated";

const selectClass =
  "h-[30px] w-full rounded-input border border-hairline bg-surface-elevated px-2 text-xs text-text-primary outline-none transition-all duration-[120ms] focus:border-accent/40";

const ghostBtnClass =
  "inline-flex shrink-0 items-center gap-1.5 rounded-input border border-hairline bg-surface-elevated px-2.5 text-xs font-medium text-text-secondary transition-colors duration-[120ms] hover:text-text-primary hover:bg-surface-card-hover disabled:opacity-60";

function Field({
  label,
  hint,
  required,
  children,
}: {
  label: string;
  hint?: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <label className="flex flex-col gap-1">
      <span className="flex items-center gap-1 text-2xs font-medium uppercase tracking-[0.04em] text-text-faint">
        {label}
        {required && <span className="text-status-error">*</span>}
      </span>
      {children}
      {hint && <span className="text-2xs text-text-faint">{hint}</span>}
    </label>
  );
}

function baseName(path: string): string {
  const parts = path.replace(/[\\/]+$/, "").split(/[\\/]/);
  return parts[parts.length - 1] || path;
}

function messageOf(e: unknown): string {
  return e instanceof Error ? e.message : String(e);
}
