import { useEffect, useMemo, useState } from "react";
import { Layers } from "lucide-react";
import { cn } from "@/lib/utils";
import type { GroupSeed } from "@/lib/regroup";

export interface GroupNameRequest {
  suggestedName: string;
  seeds: GroupSeed[];
}

interface GroupNameDialogProps {
  request: GroupNameRequest | null;
  onConfirm: (name: string, roles: Record<string, string>) => void;
  onCancel: () => void;
}

const ROLES = ["frontend", "backend", "other"] as const;

/**
 * New-group name/role dialog — shown when a drag forms a brand-new group (Core's NewGroup
 * plan). Smart-prefilled like the WPF dialog: the group name is suggested from the two apps
 * and each member's role is pre-guessed. Ostara styling, matches ConfirmDialog. Esc cancels,
 * Enter confirms.
 */
export function GroupNameDialog({ request, onConfirm, onCancel }: GroupNameDialogProps) {
  const [name, setName] = useState("");
  const [roles, setRoles] = useState<Record<string, string>>({});

  // Seed name + roles whenever a new request arrives.
  useEffect(() => {
    if (!request) return;
    setName(request.suggestedName);
    setRoles(Object.fromEntries(request.seeds.map((s) => [s.id, s.role || "other"])));
  }, [request]);

  const canConfirm = useMemo(() => name.trim().length > 0, [name]);

  useEffect(() => {
    if (!request) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onCancel();
      else if (e.key === "Enter" && canConfirm) onConfirm(name.trim(), roles);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [request, name, roles, canConfirm, onConfirm, onCancel]);

  if (!request) return null;

  return (
    <div
      className="fixed inset-0 z-[60] flex items-center justify-center bg-black/55 p-6 animate-fade-up"
      onClick={onCancel}
    >
      <div
        className="w-full max-w-md overflow-hidden rounded-card border border-hairline bg-surface-card shadow-card-hover"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-start gap-3 px-5 pt-5">
          <div className="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-input bg-accent-muted ring-1 ring-accent/20">
            <Layers className="h-3.5 w-3.5 text-accent" />
          </div>
          <div className="min-w-0 flex-1">
            <h2 className="text-md font-semibold text-text-primary tracking-[-0.01em]">
              New group
            </h2>
            <p className="mt-1 text-xs leading-relaxed text-text-secondary">
              Group these apps together. Set a name and each app's role.
            </p>
          </div>
        </div>

        {/* Group name */}
        <div className="px-5 pt-4">
          <label className="mb-1 block text-2xs font-medium uppercase tracking-wide text-text-faint">
            Group name
          </label>
          <input
            autoFocus
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Group name"
            className={cn(
              "h-8 w-full rounded-input border border-hairline bg-surface-elevated px-2.5",
              "text-xs text-text-primary placeholder:text-text-faint outline-none",
              "transition-all duration-[120ms] focus:border-accent/40 focus:bg-surface-elevated",
            )}
          />
        </div>

        {/* Member roles */}
        <div className="px-5 pt-4">
          <label className="mb-1.5 block text-2xs font-medium uppercase tracking-wide text-text-faint">
            Roles
          </label>
          <div className="flex flex-col gap-1.5">
            {request.seeds.map((seed) => (
              <div
                key={seed.id}
                className="flex items-center gap-2 rounded-input border border-hairline bg-surface-elevated/40 px-2.5 py-1.5"
              >
                <span className="flex-1 truncate text-xs font-medium text-text-secondary">
                  {seed.name}
                </span>
                <div className="flex shrink-0 items-center gap-0.5 rounded-input bg-surface-elevated/70 p-0.5">
                  {ROLES.map((r) => (
                    <button
                      key={r}
                      onClick={() => setRoles((m) => ({ ...m, [seed.id]: r }))}
                      className={cn(
                        "rounded-[4px] px-2 py-0.5 text-2xs font-medium capitalize transition-all duration-[120ms]",
                        (roles[seed.id] ?? "other") === r
                          ? "bg-accent text-white shadow-[0_1px_2px_rgba(0,0,0,0.3)]"
                          : "text-text-faint hover:text-text-secondary",
                      )}
                    >
                      {r}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Footer */}
        <div className="mt-5 flex items-center justify-end gap-2 border-top-hairline px-5 py-3">
          <button onClick={onCancel} className="btn-ghost-action text-text-secondary">
            Cancel
          </button>
          <button
            onClick={() => canConfirm && onConfirm(name.trim(), roles)}
            disabled={!canConfirm}
            className={cn(
              "inline-flex items-center gap-1.5 rounded-[5px] px-3.5 py-1",
              "text-xs font-semibold tracking-[-0.01em] text-white",
              "shadow-[0_1px_2px_rgba(0,0,0,0.4)] transition-all duration-[120ms] ease-out-expo",
              "active:scale-[0.97]",
              canConfirm
                ? "bg-accent hover:bg-accent-hover"
                : "cursor-not-allowed bg-accent/40",
            )}
          >
            Create group
          </button>
        </div>
      </div>
    </div>
  );
}
