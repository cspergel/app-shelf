import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Search, Plus, RefreshCw, Layers, LayoutGrid, Activity } from "lucide-react";
import { useBridge } from "@/lib/use-bridge";
import { CardGridDnd, type CardEntry, type StandaloneView } from "@/components/card-grid-dnd";
import { PortDoctor } from "@/components/port-doctor";
import { Toast, type ToastState } from "@/components/toast";
import { AppDialog } from "@/components/app-dialog";
import { HotkeySettings } from "@/components/hotkey-settings";
import { ConfirmDialog, type ConfirmRequest } from "@/components/confirm-dialog";
import { LogPanel } from "@/components/log-panel";
import { removeApp } from "@/lib/app-actions";
import { cn } from "@/lib/utils";
import type { AppView } from "@/lib/types";

type Tab = "apps" | "ports";

// ── Group aggregation ──────────────────────────────────────────────────────
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

  // Build all entries, then sort favorites-first across both groups and standalones.
  // A group counts as favorited when any of its members is favorited.
  const entries: CardEntry[] = [
    ...[...groups.entries()].map(
      ([name, members]): CardEntry => ({ type: "group", name, members }),
    ),
    ...standalones.map((app): StandaloneView => ({ type: "standalone", app })),
  ];

  const isFav = (e: CardEntry): boolean =>
    e.type === "group" ? e.members.some((m) => m.favorite) : e.app.favorite;
  const sortName = (e: CardEntry): string =>
    e.type === "group" ? e.name : e.app.name;

  entries.sort((a, b) => {
    // 1. favorites first
    const fa = isFav(a);
    const fb = isFav(b);
    if (fa !== fb) return fa ? -1 : 1;
    // 2. stable secondary: groups before standalones within the same favorite tier
    if (a.type !== b.type) return a.type === "group" ? -1 : 1;
    // 3. alphabetical
    return sortName(a).localeCompare(sortName(b));
  });

  return entries;
}

// ── Root app ───────────────────────────────────────────────────────────────
export default function App() {
  const { apps, loading, error, connected, refresh, launch, stop, toggleFavorite, regroupOptimistic, setDragging } = useBridge();
  const [query, setQuery] = useState("");
  const [tab, setTab] = useState<Tab>("apps");
  const [toast, setToast] = useState<ToastState | null>(null);

  // Add/Edit dialog: open + the app being edited (null = add mode).
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<AppView | null>(null);

  // Hotkey settings panel (opened from the tray "Hotkey…" menu via C#→JS navigation push).
  const [hotkeyOpen, setHotkeyOpen] = useState(false);

  // Remove confirmation: the pending request + the target app.
  const [confirm, setConfirm] = useState<ConfirmRequest | null>(null);
  const [pendingRemove, setPendingRemove] = useState<AppView | null>(null);

  // Log viewer: the app whose log panel is open (null = closed). Only one panel at a time.
  const [logPanelAppId, setLogPanelAppId] = useState<string | null>(null);
  const showLogs = useCallback((id: string) => setLogPanelAppId(id), []);
  // Crash ids whose log panel has already been auto-surfaced or manually dismissed. A still-crashed
  // app must NOT re-open its panel after the user closes it; entries are pruned once the app recovers
  // (logTail clears) so a future crash surfaces fresh.
  const surfacedCrashes = useRef<Set<string>>(new Set());
  const closeLogs = useCallback(() => {
    setLogPanelAppId((prev) => {
      if (prev) surfacedCrashes.current.add(prev); // remember the dismissal
      return null;
    });
  }, []);

  const onToast = useCallback(
    (kind: "success" | "error", message: string) => setToast({ kind, message }),
    [],
  );

  const openAdd = useCallback(() => {
    setEditing(null);
    setDialogOpen(true);
  }, []);

  const openEdit = useCallback((app: AppView) => {
    setEditing(app);
    setDialogOpen(true);
  }, []);

  // C#→JS navigation push: the tray menu drives the web UI by posting
  // { type: "navigate", view: "..." } (no requestId). The bridge's request/response messages
  // carry a requestId and are handled by use-bridge; we skip those here to avoid double-handling.
  // window.chrome.webview is undefined in plain-browser/mock mode — guarded below.
  useEffect(() => {
    const handler = (event: { data: unknown }) => {
      let data: unknown;
      try {
        data = typeof event.data === "string" ? JSON.parse(event.data) : event.data;
      } catch {
        return;
      }
      const msg = data as { requestId?: string; type?: string; view?: string };
      if (msg?.requestId) return; // bridge response — not for us
      if (msg?.type === "navigate") {
        if (msg.view === "ports") setTab("ports");
        else if (msg.view === "add-app") openAdd();
        else if (msg.view === "hotkey-settings") setHotkeyOpen(true);
      }
    };
    const webview = (
      window as unknown as {
        chrome?: {
          webview?: {
            addEventListener?: (t: "message", l: (e: { data: unknown }) => void) => void;
            removeEventListener?: (t: "message", l: (e: { data: unknown }) => void) => void;
          };
        };
      }
    ).chrome?.webview;
    webview?.addEventListener?.("message", handler);
    return () => webview?.removeEventListener?.("message", handler);
  }, [openAdd]);

  // Auto-surface the log panel on crash: the bridge inlines a crashed app's log tail in the poll
  // payload (logTail is non-null only for StoppedUnexpectedly). Surface the panel for a crashed app
  // we haven't already shown/dismissed this crash cycle — so closing it does NOT immediately re-open
  // while the app stays crashed. Recovered apps are pruned so a later re-crash surfaces again.
  useEffect(() => {
    const crashedNow = new Set(
      apps.filter((a) => a.logTail && a.logTail.length > 0).map((a) => a.id),
    );
    for (const id of surfacedCrashes.current) {
      if (!crashedNow.has(id)) surfacedCrashes.current.delete(id);
    }
    if (logPanelAppId !== null) return;
    const next = apps.find(
      (a) => a.logTail && a.logTail.length > 0 && !surfacedCrashes.current.has(a.id),
    );
    if (next) {
      surfacedCrashes.current.add(next.id);
      setLogPanelAppId(next.id);
    }
  }, [apps, logPanelAppId]);

  const onSaved = useCallback(
    (message: string) => {
      onToast("success", message);
      void refresh();
    },
    [onToast, refresh],
  );

  const requestRemove = useCallback((app: AppView) => {
    setPendingRemove(app);
    setConfirm({
      title: `Remove "${app.name}"?`,
      message:
        "This removes it from AppShelf (your project files are not touched). " +
        "If it's running, it will be stopped first.",
      confirmLabel: "Remove",
      danger: true,
    });
  }, []);

  const doRemove = useCallback(async () => {
    const app = pendingRemove;
    setConfirm(null);
    setPendingRemove(null);
    if (!app) return;
    try {
      await removeApp(app.id);
      onToast("success", `Removed "${app.name}"`);
      void refresh();
    } catch (e) {
      onToast("error", e instanceof Error ? e.message : String(e));
    }
  }, [pendingRemove, onToast, refresh]);

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
              onClick={openAdd}
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
            <CardGridDnd
              entries={entries}
              apps={apps}
              onLaunch={launch}
              onStop={stop}
              onToast={onToast}
              onEdit={openEdit}
              onRemove={requestRemove}
              onFavorite={toggleFavorite}
              onShowLogs={showLogs}
              onRegroup={regroupOptimistic}
              onDragStateChange={setDragging}
            />
          )}
        </main>
      )}

      {/* Add / Edit dialog */}
      <AppDialog
        app={editing}
        open={dialogOpen}
        onClose={() => setDialogOpen(false)}
        onSaved={onSaved}
      />

      {/* Spotlight hotkey settings (opened from tray "Hotkey…") */}
      <HotkeySettings
        open={hotkeyOpen}
        onClose={() => setHotkeyOpen(false)}
        onToast={onToast}
      />

      {/* Remove confirmation */}
      <ConfirmDialog
        request={confirm}
        onConfirm={() => void doRemove()}
        onCancel={() => {
          setConfirm(null);
          setPendingRemove(null);
        }}
      />

      {/* Per-app log viewer (bottom drawer). Opened from a card's "Logs" button or auto-surfaced
          on crash. Only one panel at a time; closing returns null. */}
      {logPanelAppId !== null && (
        <LogPanel
          appId={logPanelAppId}
          appName={apps.find((a) => a.id === logPanelAppId)?.name ?? ""}
          onClose={closeLogs}
        />
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
