import { useCallback, useMemo, useState } from "react";
import { Search, Plus, RefreshCw, Layers, LayoutGrid, Activity } from "lucide-react";
import { useBridge } from "@/lib/use-bridge";
import { AppCard } from "@/components/app-card";
import { GroupCard } from "@/components/group-card";
import { PortDoctor } from "@/components/port-doctor";
import { Toast, type ToastState } from "@/components/toast";
import { cn } from "@/lib/utils";
import type { AppView } from "@/lib/types";

type Tab = "apps" | "ports";

// ── Group aggregation ──────────────────────────────────────────────────────
interface GroupedView {
  type: "group";
  name: string;
  members: AppView[];
}
interface StandaloneView {
  type: "standalone";
  app: AppView;
}
type CardEntry = GroupedView | StandaloneView;

function buildCardEntries(apps: AppView[]): CardEntry[] {
  const groups = new Map<string, AppView[]>();
  const standalones: AppView[] = [];

  for (const app of apps) {
    if (app.group) {
      const existing = groups.get(app.group) ?? [];
      existing.push(app);
      groups.set(app.group, existing);
    } else {
      standalones.push(app);
    }
  }

  const entries: CardEntry[] = [];

  // Groups first (sorted by name)
  for (const [name, members] of [...groups.entries()].sort(([a], [b]) => a.localeCompare(b))) {
    entries.push({ type: "group", name, members });
  }

  // Then standalones (favorites first, then alphabetical)
  const sorted = [...standalones].sort((a, b) => {
    if (a.favorite && !b.favorite) return -1;
    if (!a.favorite && b.favorite) return 1;
    return a.name.localeCompare(b.name);
  });
  for (const app of sorted) {
    entries.push({ type: "standalone", app });
  }

  return entries;
}

// ── Root app ───────────────────────────────────────────────────────────────
export default function App() {
  const { apps, loading, error, connected, refresh, launch, stop } = useBridge();
  const [query, setQuery] = useState("");
  const [tab, setTab] = useState<Tab>("apps");
  const [toast, setToast] = useState<ToastState | null>(null);

  const onToast = useCallback(
    (kind: "success" | "error", message: string) => setToast({ kind, message }),
    [],
  );

  // Stats for the header badge
  const runningCount = apps.filter((a) => a.status === "Running").length;
  const totalCount = apps.length;

  // Filter then group
  const entries = useMemo<CardEntry[]>(() => {
    const q = query.trim().toLowerCase();
    const filtered = q
      ? apps.filter(
          (a) =>
            a.name.toLowerCase().includes(q) ||
            (a.framework ?? "").toLowerCase().includes(q) ||
            (a.group ?? "").toLowerCase().includes(q) ||
            (a.tags ?? []).some((t) => t.toLowerCase().includes(q)),
        )
      : apps;
    return buildCardEntries(filtered);
  }, [apps, query]);

  return (
    <div className="flex h-full flex-col bg-surface-window">

      {/* ── HEADER / TOP BAR ──────────────────────────────────────────────── */}
      <header className="flex items-center gap-3 border-b border-hairline bg-surface-window px-5 py-3 shrink-0">

        {/* Brand mark */}
        <div className="flex items-center gap-2 shrink-0">
          <div className="flex h-6 w-6 items-center justify-center rounded-[6px] bg-accent-muted ring-1 ring-accent/20">
            <Layers className="h-3.5 w-3.5 text-accent" />
          </div>
          <span className="text-md font-semibold text-text-primary tracking-[-0.015em]">
            AppShelf
          </span>
        </div>

        {/* Mock mode badge — shown only in plain browser, not in WebView2 */}
        {!connected && (
          <span className="text-2xs font-medium px-1.5 py-0.5 rounded-pill bg-surface-elevated/60 text-text-faint border border-hairline shrink-0">
            mock
          </span>
        )}

        {/* Running count pill — apps tab only */}
        {tab === "apps" && totalCount > 0 && (
          <div className="flex items-center gap-1.5 shrink-0">
            <span className="h-px w-3 bg-hairline" />
            <span
              className={cn(
                "text-xs font-medium px-2 py-0.5 rounded-pill",
                runningCount > 0
                  ? "text-status-running bg-status-running/10"
                  : "text-text-faint bg-surface-elevated/60",
              )}
            >
              {runningCount > 0
                ? `${runningCount} running`
                : `${totalCount} apps`}
            </span>
          </div>
        )}

        {/* ── Tab switch: Apps | Ports ─────────────────────────────────────── */}
        <div className="ml-2 flex shrink-0 items-center gap-0.5 rounded-input bg-surface-elevated/60 p-0.5">
          <TabButton active={tab === "apps"} onClick={() => setTab("apps")} icon={<LayoutGrid className="h-3 w-3" />}>
            Apps
          </TabButton>
          <TabButton active={tab === "ports"} onClick={() => setTab("ports")} icon={<Activity className="h-3 w-3" />}>
            Ports
          </TabButton>
        </div>

        {/* Search — apps tab only */}
        {tab === "apps" && (
          <div className="relative flex-1 max-w-sm ml-3">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3 w-3 -translate-y-1/2 text-text-faint" />
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search apps or #tags…"
              className={cn(
                "h-7 w-full rounded-input border border-hairline bg-surface-elevated",
                "pl-7 pr-3 text-xs text-text-primary placeholder:text-text-faint",
                "outline-none transition-all duration-[120ms]",
                "focus:border-accent/40 focus:shadow-accent-glow/30 focus:bg-surface-elevated",
              )}
            />
            {query && (
              <button
                onClick={() => setQuery("")}
                className="absolute right-2 top-1/2 -translate-y-1/2 text-text-faint hover:text-text-secondary text-xs"
              >
                ✕
              </button>
            )}
          </div>
        )}

        {/* Spacer */}
        <div className="flex-1" />

        {/* Refresh + Add — apps tab only (Ports has its own re-scan) */}
        {tab === "apps" && (
          <>
            <button
              onClick={() => void refresh()}
              title="Refresh"
              className="h-7 w-7 flex items-center justify-center rounded-input text-text-faint hover:text-text-secondary hover:bg-surface-card-hover transition-all duration-[120ms]"
            >
              <RefreshCw className="h-3.5 w-3.5" />
            </button>

            <button
              title="Add app"
              className={cn(
                "inline-flex items-center gap-1.5 h-7 px-3 rounded-input",
                "text-xs font-semibold text-white tracking-[-0.01em]",
                "bg-accent hover:bg-accent-hover shadow-[0_1px_2px_rgba(0,0,0,0.4)]",
                "transition-all duration-[120ms] active:scale-[0.97]",
              )}
            >
              <Plus className="h-3.5 w-3.5" />
              Add
            </button>
          </>
        )}
      </header>

      {/* ── BODY ──────────────────────────────────────────────────────────── */}
      {tab === "ports" ? (
        <PortDoctor />
      ) : (
        <main className="flex-1 overflow-y-auto px-5 py-5">
          {loading ? (
            <LoadingSkeleton />
          ) : error ? (
            <EmptyState icon="⚠️" title="Couldn't load apps" hint={error} danger />
          ) : entries.length === 0 ? (
            <EmptyState
              icon={query ? "🔍" : "📦"}
              title={query ? "No matches" : "No apps registered yet"}
              hint={
                query
                  ? `No apps matching "${query}". Try a different search.`
                  : "Add a project with the + button or via the appshelf CLI."
              }
            />
          ) : (
            <CardGrid entries={entries} onLaunch={launch} onStop={stop} onToast={onToast} />
          )}
        </main>
      )}

      {/* Quick-action result toast (success + friendly failures) */}
      <Toast toast={toast} onDismiss={() => setToast(null)} />
    </div>
  );
}

// ── Tab button (segmented control) ───────────────────────────────────────────
function TabButton({
  active,
  onClick,
  icon,
  children,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-1.5 rounded-[5px] px-2.5 h-6 text-xs font-medium",
        "transition-all duration-[120ms]",
        active
          ? "bg-surface-card text-text-primary shadow-[0_1px_2px_rgba(0,0,0,0.3)]"
          : "text-text-faint hover:text-text-secondary",
      )}
    >
      {icon}
      {children}
    </button>
  );
}

// ── Card grid ──────────────────────────────────────────────────────────────
function CardGrid({
  entries,
  onLaunch,
  onStop,
  onToast,
}: {
  entries: CardEntry[];
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
  onToast: (kind: "success" | "error", message: string) => void;
}) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(300px,1fr))] gap-3">
      {entries.map((entry, i) => {
        const delay = Math.min(i * 35, 200);
        const style = { animationDelay: `${delay}ms` };

        if (entry.type === "group") {
          return (
            <GroupCard
              key={`group-${entry.name}`}
              name={entry.name}
              members={entry.members}
              onLaunch={onLaunch}
              onStop={onStop}
              onToast={onToast}
              style={style}
            />
          );
        }

        return (
          <AppCard
            key={entry.app.id}
            app={entry.app}
            onLaunch={onLaunch}
            onStop={onStop}
            onToast={onToast}
            style={style}
          />
        );
      })}
    </div>
  );
}

// ── Empty state ────────────────────────────────────────────────────────────
function EmptyState({
  icon,
  title,
  hint,
  danger,
}: {
  icon?: string;
  title: string;
  hint?: string;
  danger?: boolean;
}) {
  return (
    <div className="flex h-full min-h-[280px] flex-col items-center justify-center text-center gap-3">
      {icon && <span className="text-3xl opacity-40 select-none">{icon}</span>}
      <div>
        <p
          className={cn(
            "text-base font-semibold",
            danger ? "text-status-error" : "text-text-primary",
          )}
        >
          {title}
        </p>
        {hint && (
          <p className="mt-1.5 max-w-xs text-xs text-text-secondary leading-relaxed">
            {hint}
          </p>
        )}
      </div>
    </div>
  );
}

// ── Loading skeleton ───────────────────────────────────────────────────────
function LoadingSkeleton() {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(300px,1fr))] gap-3">
      {Array.from({ length: 4 }).map((_, i) => (
        <div
          key={i}
          className="rounded-card border border-hairline bg-surface-card h-[120px] animate-pulse"
          style={{ animationDelay: `${i * 60}ms` }}
        />
      ))}
    </div>
  );
}
