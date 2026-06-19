import { useMemo, useState } from "react";
import { RefreshCw, Search, Layers } from "lucide-react";
import { useBridge } from "@/lib/use-bridge";
import { AppCard } from "@/components/app-card";
import { Button } from "@/components/ui/button";

export default function App() {
  const { apps, loading, error, connected, refresh, launch, stop } = useBridge();
  const [query, setQuery] = useState("");

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return apps;
    return apps.filter(
      (a) =>
        a.name.toLowerCase().includes(q) ||
        (a.framework ?? "").toLowerCase().includes(q) ||
        (a.group ?? "").toLowerCase().includes(q),
    );
  }, [apps, query]);

  const runningCount = apps.filter((a) => a.status === "Running").length;

  return (
    <div className="flex h-full flex-col">
      {/* Top bar */}
      <header className="flex items-center gap-3 border-b border-hairline px-5 py-3.5">
        <div className="flex items-center gap-2">
          <div className="flex h-7 w-7 items-center justify-center rounded-md bg-accent-muted">
            <Layers className="h-4 w-4 text-accent-hover" />
          </div>
          <span className="text-sm font-semibold tracking-[-0.01em] text-text-primary">
            AppShelf
          </span>
          {runningCount > 0 && (
            <span className="ml-1 rounded-full bg-status-running/15 px-2 py-0.5 text-[11px] font-medium text-status-running">
              {runningCount} running
            </span>
          )}
        </div>

        <div className="relative ml-3 flex-1 max-w-md">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-text-faint" />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Search apps"
            className="h-8 w-full rounded-md border border-hairline bg-surface-card pl-8 pr-3 text-[13px] text-text-primary placeholder:text-text-faint outline-none focus:border-accent/60"
          />
        </div>

        <Button
          variant="ghost"
          size="icon"
          onClick={() => void refresh()}
          title="Refresh"
        >
          <RefreshCw className="h-3.5 w-3.5" />
        </Button>
      </header>

      {/* Body */}
      <main className="flex-1 overflow-y-auto px-5 py-5">
        {!connected ? (
          <EmptyState
            title="No bridge connection"
            hint="Open this UI through the AppShelf desktop app (WebView2). In a plain browser there is no C# engine to talk to."
          />
        ) : loading ? (
          <EmptyState title="Loading apps…" />
        ) : error ? (
          <EmptyState title="Couldn't load apps" hint={error} />
        ) : filtered.length === 0 ? (
          <EmptyState
            title={query ? "No matches" : "No apps registered yet"}
            hint={query ? "Try a different search." : "Add a project to get started."}
          />
        ) : (
          <div className="grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-3 animate-fade-in">
            {filtered.map((app) => (
              <AppCard
                key={app.id}
                app={app}
                onLaunch={launch}
                onStop={stop}
              />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}

function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <div className="flex h-full min-h-[300px] flex-col items-center justify-center text-center">
      <p className="text-sm font-semibold text-text-primary">{title}</p>
      {hint && (
        <p className="mt-1.5 max-w-sm text-[13px] text-text-secondary">{hint}</p>
      )}
    </div>
  );
}
