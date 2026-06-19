import { useCallback, useEffect, useMemo, useState } from "react";
import { Search, Plus, RefreshCw, Layers, Sun, Moon } from "lucide-react";
import { useBridge } from "@/lib/use-bridge";
import { AppCard } from "@/components/app-card";
import { GroupCard } from "@/components/group-card";
import { cn } from "@/lib/utils";
import type { AppView } from "@/lib/types";

// ── Theme toggle hook ──────────────────────────────────────────────────────
type AppTheme = "light" | "dark";

function useTheme(): [AppTheme, () => void] {
  const [theme, setTheme] = useState<AppTheme>(() => {
    // Read from html[data-theme] which was set before React mounts (main.tsx)
    const current = document.documentElement.dataset.theme;
    return current === "light" ? "light" : "dark";
  });

  const toggle = useCallback(() => {
    setTheme((prev) => {
      const next: AppTheme = prev === "dark" ? "light" : "dark";
      document.documentElement.dataset.theme = next;
      localStorage.setItem("appshelf-theme", next);
      return next;
    });
  }, []);

  // Keep html[data-theme] in sync on mount (handles edge-case where localStorage
  // and the bootstrap script diverged — e.g. SSR or test environments)
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  return [theme, toggle];
}

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
  const [theme, toggleTheme] = useTheme();

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

        {/* Running count pill */}
        {totalCount > 0 && (
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

        {/* Search — center, flex-1, capped width */}
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

        {/* Spacer */}
        <div className="flex-1" />

        {/* Refresh */}
        <button
          onClick={() => void refresh()}
          title="Refresh"
          className="h-7 w-7 flex items-center justify-center rounded-input text-text-faint hover:text-text-secondary hover:bg-surface-card-hover transition-all duration-[120ms]"
        >
          <RefreshCw className="h-3.5 w-3.5" />
        </button>

        {/* Theme toggle — sun/moon, tasteful, no label */}
        <button
          onClick={toggleTheme}
          title={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
          className={cn(
            "h-7 w-7 flex items-center justify-center rounded-input transition-all duration-[120ms]",
            "text-text-faint hover:text-text-secondary hover:bg-surface-card-hover",
          )}
        >
          {theme === "dark" ? (
            <Sun className="h-3.5 w-3.5" />
          ) : (
            <Moon className="h-3.5 w-3.5" />
          )}
        </button>

        {/* Add app — primary affordance in header */}
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
      </header>

      {/* ── BODY ──────────────────────────────────────────────────────────── */}
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
          <CardGrid entries={entries} onLaunch={launch} onStop={stop} />
        )}
      </main>
    </div>
  );
}

// ── Card grid ──────────────────────────────────────────────────────────────
function CardGrid({
  entries,
  onLaunch,
  onStop,
}: {
  entries: CardEntry[];
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
}) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(300px,1fr))] gap-3">
      {entries.map((entry, i) => {
        // Stagger entrance by index (capped at 200ms)
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
