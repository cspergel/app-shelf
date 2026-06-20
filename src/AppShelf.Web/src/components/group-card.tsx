import { useState } from "react";
import { ChevronRight, Play, Square, ExternalLink, Star, GripVertical } from "lucide-react";
import { SortableContext, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import type { DragKind } from "@/lib/regroup";
import { StatusDot, statusMeta } from "./status-dot";
import { CardMenu } from "./card-menu";
import { cn } from "@/lib/utils";
import type { AppView, LaunchStatus } from "@/lib/types";

interface GroupCardProps {
  name: string;
  members: AppView[];
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
  onToast: (kind: "success" | "error", message: string) => void;
  onEdit?: (app: AppView) => void;
  onRemove?: (app: AppView) => void;
  onFavorite?: (id: string, favorite: boolean) => void;
  /** Drag state from the grid (optional — group card also renders outside DnD in tests). */
  activeMemberId?: string | null;
  overMemberId?: string | null;
  style?: React.CSSProperties;
}

/** Derive an aggregate status from group members. */
function aggregateStatus(members: AppView[]): LaunchStatus {
  if (members.every((m) => m.status === "Running")) return "Running";
  if (members.some((m) => m.status === "Running" || m.status === "Starting")) return "Starting";
  if (members.some((m) => m.status === "Error" || m.status === "StoppedUnexpectedly")) return "Error";
  if (members.some((m) => m.status === "PortInUse")) return "PortInUse";
  return "Stopped";
}

/** "2 of 3 running" aggregate label. */
function aggregateLabel(members: AppView[]): string {
  const running = members.filter((m) => m.status === "Running").length;
  const total = members.length;
  if (running === total) return `All ${total} running`;
  if (running === 0) return `${total} stopped`;
  return `${running} of ${total} running`;
}

/** Find the frontend member URL for the group Open action. */
function frontendUrl(members: AppView[]): string | null {
  return (
    members.find((m) => m.role === "frontend")?.url ??
    members.find((m) => m.url)?.url ??
    null
  );
}

/** Role label color pill. */
function RoleChip({ role }: { role: string }) {
  if (!role) return null;
  const isFrontend = role === "frontend";
  return (
    <span
      className={cn(
        "text-2xs font-medium px-1.5 py-0.5 rounded-chip tracking-[0.01em]",
        isFrontend
          ? "text-accent bg-accent-muted"
          : "text-text-faint bg-surface-elevated/60",
      )}
    >
      {role}
    </span>
  );
}

export function GroupCard({ name, members, onLaunch, onStop, onToast, onEdit, onRemove, onFavorite, activeMemberId, overMemberId, style }: GroupCardProps) {
  const [expanded, setExpanded] = useState(true);
  const aggStatus = aggregateStatus(members);
  const aggMeta = statusMeta(aggStatus);
  const label = aggregateLabel(members);
  const openUrl = frontendUrl(members);

  const allRunning = members.every((m) => m.status === "Running");
  const allStopped = members.every((m) => m.status === "Stopped");
  const anyActive = members.some(
    (m) => m.status === "Running" || m.status === "Starting",
  );

  const handleStartAll = () => members.filter((m) => m.status === "Stopped").forEach((m) => onLaunch(m.id));
  const handleStopAll = () => members.filter((m) => m.status !== "Stopped").forEach((m) => onStop(m.id));

  return (
    <article
      style={style}
      className={cn(
        "relative overflow-hidden rounded-card border border-hairline bg-surface-card",
        "shadow-card transition-all duration-[120ms] ease-out-expo animate-fade-up",
        "hover:bg-surface-card-hover hover:shadow-card-hover hover:-translate-y-px",
      )}
    >
      {/* Accent green rail — groups always use accent color, not status color */}
      <span className="absolute left-0 top-0 bottom-0 w-[3px] rounded-l-card bg-accent opacity-70 animate-rail-grow" />

      <div className="pl-5 pr-4 pt-4 pb-3.5">

        {/* ── GROUP HEADER ────────────────────────────────────────────── */}
        <div className="flex items-center gap-2 mb-1">
          {/* Expand/collapse chevron */}
          <button
            onClick={() => setExpanded((e) => !e)}
            className="text-text-faint hover:text-text-secondary transition-colors duration-[120ms] -ml-0.5"
            title={expanded ? "Collapse group" : "Expand group"}
          >
            <ChevronRight
              className={cn(
                "h-3.5 w-3.5 transition-transform duration-[120ms] ease-out-expo",
                expanded && "rotate-90",
              )}
            />
          </button>

          {/* Group name */}
          <h3 className="flex-1 truncate text-sm font-semibold text-text-primary min-w-0">
            {name}
          </h3>

          {/* Aggregate status dot + count */}
          <div className={cn("flex items-center gap-1.5 shrink-0 ml-2", aggMeta.textClass)}>
            <StatusDot status={aggStatus} />
            <span className="text-xs font-medium whitespace-nowrap">{label}</span>
          </div>
        </div>

        {/* Sub-label: member frameworks summarized */}
        <p className="text-xs text-text-faint mb-3 pl-5">
          {members.map((m) => m.framework).filter(Boolean).join(" · ") || `${members.length} apps`}
        </p>

        {/* ── HAIRLINE DIVIDER ─────────────────────────────────────────── */}
        <div className="border-top-hairline -mx-4 mb-3" />

        {/* ── GROUP FOOTER ACTIONS ────────────────────────────────────── */}
        <div className="flex items-center gap-0.5">
          {/* Status summary left */}
          <span className={cn("flex-1 text-xs font-medium", aggMeta.textClass)}>
            {allRunning ? "All systems running" : allStopped ? "All stopped" : label}
          </span>

          {/* Ghost secondary actions */}
          {anyActive && (
            <button className="btn-ghost-action" onClick={handleStopAll} title="Stop all">
              <span className="flex items-center gap-1">
                <Square className="h-3 w-3" />
                Stop all
              </span>
            </button>
          )}
          {!allRunning && (
            <button className="btn-ghost-action" onClick={handleStartAll} title="Start all">
              <span className="flex items-center gap-1">
                <Play className="h-3 w-3" />
                Start all
              </span>
            </button>
          )}

          {/* Primary: Open → frontend URL */}
          {openUrl && allRunning && (
            <button
              onClick={() => window.open(openUrl, "_blank")}
              className={cn(
                "inline-flex items-center gap-1.5 px-3 py-1 rounded-[5px]",
                "text-xs font-semibold tracking-[-0.01em] text-white",
                "bg-accent hover:bg-accent-hover shadow-[0_1px_2px_rgba(0,0,0,0.4)]",
                "transition-all duration-[120ms] ease-out-expo active:scale-[0.97]",
              )}
            >
              <ExternalLink className="h-3.5 w-3.5" />
              Open
            </button>
          )}
        </div>

        {/* ── EXPANDED MEMBER ROWS ─────────────────────────────────────── */}
        {expanded && (
          <SortableContext
            items={members.map((m) => `member:${m.id}`)}
            strategy={verticalListSortingStrategy}
          >
            <div className="mt-3 border-top-hairline pt-2.5 flex flex-col gap-0.5 animate-slide-down">
              {members.map((member) => (
                <MemberRow
                  key={member.id}
                  member={member}
                  dragging={activeMemberId === member.id}
                  isOver={overMemberId === `member:${member.id}` && activeMemberId !== member.id}
                  onLaunch={onLaunch}
                  onStop={onStop}
                  onToast={onToast}
                  onEdit={onEdit}
                  onRemove={onRemove}
                  onFavorite={onFavorite}
                />
              ))}
            </div>
          </SortableContext>
        )}
      </div>
    </article>
  );
}

// ── Sortable member row (drag to reorder within group / out to ungroup) ───────
function MemberRow({
  member,
  dragging,
  isOver,
  onLaunch,
  onStop,
  onToast,
  onEdit,
  onRemove,
  onFavorite,
}: {
  member: AppView;
  dragging: boolean;
  isOver: boolean;
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
  onToast: (kind: "success" | "error", message: string) => void;
  onEdit?: (app: AppView) => void;
  onRemove?: (app: AppView) => void;
  onFavorite?: (id: string, favorite: boolean) => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition } = useSortable({
    id: `member:${member.id}`,
    data: { drag: { kind: "app", app: member } as DragKind },
  });

  const mMeta = statusMeta(member.status);
  const mStopped = member.status === "Stopped";
  const mRunning = member.status === "Running";

  const style: React.CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition,
    opacity: dragging ? 0.4 : 1,
  };

  return (
    <div
      ref={setNodeRef}
      style={style}
      className={cn(
        "relative flex items-center gap-2 px-1 py-1.5 rounded-[5px] transition-colors duration-[120ms] group/member",
        "hover:bg-surface-card-hover",
        isOver && "bg-accent/10 ring-1 ring-inset ring-accent/40",
      )}
    >
      {/* Drag grip — the explicit handle (whole row is draggable too via listeners). */}
      <button
        {...attributes}
        {...listeners}
        className="shrink-0 cursor-grab text-text-faint/40 hover:text-text-secondary active:cursor-grabbing transition-colors duration-[120ms] touch-none"
        title="Drag to reorder or regroup"
        aria-label="Drag handle"
      >
        <GripVertical className="h-3 w-3" />
      </button>

      {/* Per-member status rail (2px, shorter) */}
      <span
        className={cn(
          "w-0.5 h-6 rounded-full shrink-0",
          mMeta.railClass,
          mStopped && "opacity-25",
        )}
      />

      {/* Favorite star */}
      <button
        onClick={() => onFavorite?.(member.id, !member.favorite)}
        className={cn(
          "shrink-0 transition-all duration-[120ms] hover:scale-110 active:scale-95",
          member.favorite ? "text-[#E5C04B]" : "text-text-faint/50 hover:text-[#E5C04B]/80",
        )}
        title={member.favorite ? "Remove favorite" : "Add to favorites"}
        aria-pressed={member.favorite}
      >
        <Star className="h-3 w-3" fill={member.favorite ? "currentColor" : "none"} />
      </button>

      {/* Member identity: name + role chip */}
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-1.5 min-w-0">
          <span className="text-xs font-medium text-text-secondary truncate">
            {member.name}
          </span>
          {member.role && <RoleChip role={member.role} />}
        </div>
        {member.port && <span className="meta-mono text-2xs">{`:${member.port}`}</span>}
      </div>

      {/* Status dot + quick action (hover-only) */}
      <div className="relative shrink-0 flex items-center justify-center w-4 h-4">
        <StatusDot
          status={member.status}
          className="absolute group-hover/member:opacity-0 transition-opacity duration-[120ms]"
        />
        <button
          className="absolute opacity-0 group-hover/member:opacity-100 transition-opacity duration-[120ms] btn-ghost-action text-2xs py-0 px-0 w-full h-full flex items-center justify-center"
          onClick={() => {
            if (mRunning) window.open(member.url, "_blank");
            else if (mStopped) onLaunch(member.id);
            else onStop(member.id);
          }}
          title={mRunning ? "Open" : mStopped ? "Start" : "Stop"}
        >
          {mRunning ? "↗" : mStopped ? "▶" : "■"}
        </button>
      </div>

      {/* Per-member ⋯ menu */}
      <div className="shrink-0 opacity-70 group-hover/member:opacity-100 transition-opacity duration-[120ms] focus-within:opacity-100">
        <CardMenu app={member} onResult={onToast} size="sm" onEdit={onEdit} onRemove={onRemove} />
      </div>
    </div>
  );
}
