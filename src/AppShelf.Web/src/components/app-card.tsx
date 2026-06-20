import { Play, Square, ExternalLink, RotateCcw, Star, AlertTriangle, WifiOff } from "lucide-react";
import { StatusDot, StatusRail, statusMeta } from "./status-dot";
import { CardMenu } from "./card-menu";
import { cn } from "@/lib/utils";
import type { AppView } from "@/lib/types";

interface AppCardProps {
  app: AppView;
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
  onToast: (kind: "success" | "error", message: string) => void;
  onEdit?: (app: AppView) => void;
  onRemove?: (app: AppView) => void;
  onFavorite?: (id: string, favorite: boolean) => void;
  style?: React.CSSProperties;
}

export function AppCard({ app, onLaunch, onStop, onToast, onEdit, onRemove, onFavorite, style }: AppCardProps) {
  const meta = statusMeta(app.status);
  const favored = app.favorite;

  const toggleFavorite = () => onFavorite?.(app.id, !favored);

  const isStopped = app.status === "Stopped";
  const isRunning = app.status === "Running";
  const isStarting = app.status === "Starting";
  const isError = app.status === "Error" || app.status === "StoppedUnexpectedly";
  const isPortInUse = app.status === "PortInUse";

  // Derive a short port/url meta string
  const portLabel = app.port ? `:${app.port}` : null;
  const urlShort = app.url.replace(/^https?:\/\//, "");

  return (
    <article
      style={style}
      className={cn(
        // Base card shell
        "relative overflow-hidden rounded-card border border-hairline bg-surface-card",
        "shadow-card transition-all duration-[120ms] ease-out-expo",
        "animate-fade-up",
        // Hover lift — translate + deeper shadow
        "hover:-translate-y-px hover:bg-surface-card-hover hover:shadow-card-hover",
        // Error state: faint red tint on border
        isError && "border-status-error/20",
        isPortInUse && "border-status-error/20",
      )}
    >
      {/* Full-height status rail — the #1 structural move */}
      <StatusRail status={app.status} />

      {/* Card body — left-padded to clear the 3px rail */}
      <div className="flex flex-col pl-5 pr-4 pt-4 pb-3.5">

        {/* ── TOP ROW: title + star + overflow ─────────────────────────── */}
        <div className="flex items-start gap-2 mb-1.5">
          <h3 className="flex-1 truncate text-md font-semibold text-text-primary leading-snug">
            {app.name}
          </h3>

          {/* Favorite star — always present (subtle outline when off), gold fill when active. */}
          <button
            onClick={toggleFavorite}
            className={cn(
              "mt-0.5 shrink-0 transition-all duration-[120ms] hover:scale-110 active:scale-95",
              favored
                ? "text-[#E5C04B]"
                : "text-text-faint/60 hover:text-[#E5C04B]/80",
            )}
            title={favored ? "Remove favorite" : "Add to favorites"}
            aria-pressed={favored}
          >
            <Star
              className="h-3.5 w-3.5"
              fill={favored ? "currentColor" : "none"}
            />
          </button>

          {/* Overflow menu — quick dev actions + Edit / Remove */}
          <CardMenu app={app} onResult={onToast} onEdit={onEdit} onRemove={onRemove} />
        </div>

        {/* ── META ROW: framework · port · url ──────────────────────────── */}
        <div className="flex items-center gap-2 mb-3 min-w-0">
          {app.framework && (
            <>
              <span className="text-xs text-text-faint truncate">{app.framework}</span>
              {(portLabel || urlShort) && (
                <span className="w-px h-2.5 bg-hairline shrink-0" />
              )}
            </>
          )}
          {portLabel && (
            <>
              <span className="meta-mono shrink-0">{portLabel}</span>
              {urlShort && <span className="w-px h-2.5 bg-hairline shrink-0" />}
            </>
          )}
          {urlShort && (
            <span
              className={cn(
                "meta-mono truncate",
                isRunning ? "text-text-accent" : "text-text-faint",
              )}
            >
              {urlShort}
            </span>
          )}
        </div>

        {/* ── TAGS ────────────────────────────────────────────────────────── */}
        {app.tags && app.tags.length > 0 && (
          <div className="flex flex-wrap gap-1 mb-3">
            {app.tags.map((tag) => (
              <span
                key={tag}
                className="text-2xs font-medium text-accent bg-accent-muted px-1.5 py-0.5 rounded-chip tracking-[0.01em]"
              >
                {tag}
              </span>
            ))}
          </div>
        )}

        {/* ── HAIRLINE DIVIDER ─────────────────────────────────────────────── */}
        <div className="border-top-hairline -mx-4 mb-3" />

        {/* ── FOOTER: status label left · actions right ──────────────────── */}
        <div className="flex items-center gap-2">

          {/* Status badge — left-anchored, always visible */}
          <div className={cn("flex items-center gap-1.5 flex-1 min-w-0 overflow-hidden", meta.textClass)}>
            {app.status === "StoppedUnexpectedly" ? (
              <AlertTriangle className="h-3 w-3 shrink-0" />
            ) : app.status === "PortInUse" ? (
              <WifiOff className="h-3 w-3 shrink-0" />
            ) : (
              <StatusDot status={app.status} />
            )}
            <span className="text-xs font-medium tracking-[0.01em] truncate">
              {meta.label}
            </span>
          </div>

          {/* Actions — right-anchored, tight */}
          <div className="flex items-center gap-0.5 shrink-0">
            {/* Secondary actions: borderless ghost text (no outline, no bg at rest) */}
            {(isRunning || isStarting) && (
              <button
                className="btn-ghost-action"
                onClick={() => onStop(app.id)}
                title="Stop"
              >
                <span className="flex items-center gap-1">
                  <Square className="h-3 w-3" />
                  Stop
                </span>
              </button>
            )}

            {(isError || isPortInUse) && (
              <button
                className="btn-ghost-action"
                onClick={() => onLaunch(app.id)}
                title="Restart"
              >
                <span className="flex items-center gap-1">
                  <RotateCcw className="h-3 w-3" />
                  Restart
                </span>
              </button>
            )}

            {/* Primary action — the only button with real fill weight */}
            {isStopped && (
              <PrimaryButton onClick={() => onLaunch(app.id)} icon={<Play className="h-3.5 w-3.5" />}>
                Start
              </PrimaryButton>
            )}
            {isRunning && (
              <PrimaryButton
                onClick={() => window.open(app.url, "_blank")}
                icon={<ExternalLink className="h-3.5 w-3.5" />}
              >
                Open
              </PrimaryButton>
            )}
            {isStarting && (
              <PrimaryButton
                onClick={() => onStop(app.id)}
                variant="danger"
                icon={<Square className="h-3.5 w-3.5" />}
              >
                Cancel
              </PrimaryButton>
            )}
            {(isError || isPortInUse) && (
              <PrimaryButton onClick={() => onLaunch(app.id)} icon={<Play className="h-3.5 w-3.5" />}>
                Start
              </PrimaryButton>
            )}
          </div>
        </div>
      </div>
    </article>
  );
}

// ── Inline primary button — only element with real visual weight ────────────
function PrimaryButton({
  children,
  onClick,
  icon,
  variant = "accent",
}: {
  children: React.ReactNode;
  onClick: () => void;
  icon?: React.ReactNode;
  variant?: "accent" | "danger";
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        "inline-flex items-center gap-1.5 px-3 py-1 rounded-[5px]",
        "text-xs font-semibold tracking-[-0.01em] text-white",
        "transition-all duration-[120ms] ease-out-expo",
        "active:scale-[0.97]",
        variant === "accent" &&
          "bg-accent hover:bg-accent-hover shadow-[0_1px_2px_rgba(0,0,0,0.4)]",
        variant === "danger" &&
          "bg-status-error/80 hover:bg-status-error shadow-[0_1px_2px_rgba(0,0,0,0.4)]",
      )}
    >
      {icon}
      {children}
    </button>
  );
}
