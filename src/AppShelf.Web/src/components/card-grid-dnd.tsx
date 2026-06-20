import { useMemo, useState } from "react";
import {
  DndContext,
  DragOverlay,
  PointerSensor,
  useSensor,
  useSensors,
  useDroppable,
  pointerWithin,
  type DragStartEvent,
  type DragEndEvent,
  type DragOverEvent,
} from "@dnd-kit/core";
import {
  SortableContext,
  rectSortingStrategy,
  useSortable,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { AppCard } from "./app-card";
import { GroupCard } from "./group-card";
import { GroupNameDialog, type GroupNameRequest } from "./group-name-dialog";
import { type RegroupTarget, type DragKind, type GroupSeed } from "@/lib/regroup";
import { cn } from "@/lib/utils";
import type { AppView } from "@/lib/types";

// ── Entry model (mirrors App.tsx) ────────────────────────────────────────────
export interface GroupedView {
  type: "group";
  name: string;
  members: AppView[];
}
export interface StandaloneView {
  type: "standalone";
  app: AppView;
}
export type CardEntry = GroupedView | StandaloneView;

interface CardGridDndProps {
  entries: CardEntry[];
  apps: AppView[];
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
  onToast: (kind: "success" | "error", message: string) => void;
  onEdit: (app: AppView) => void;
  onRemove: (app: AppView) => void;
  onFavorite: (id: string, favorite: boolean) => void;
  onShowLogs?: (id: string) => void;
  /**
   * Apply a drag-to-group gesture. The grid updates the LOCAL card list optimistically and
   * persists in the background (no blocking status re-scan). For a brand-new group this
   * resolves with `needsNameDialog: true` (+ a suggested name and seeds) and applies nothing
   * until the dialog re-calls with a `groupName`.
   */
  onRegroup: (
    draggedId: string,
    target: RegroupTarget,
    groupName?: string,
    roles?: Record<string, string>,
  ) => Promise<{ needsNameDialog: boolean; suggestedGroupName?: string; seeds?: GroupSeed[] }>;
  /**
   * Called with `true` when a drag starts and `false` when it ends (including cancel).
   * Used by the bridge to suppress status polls during drag + a brief post-drop cooldown,
   * so the periodic port-probe re-render never collides with dnd-kit settling.
   */
  onDragStateChange?: (dragging: boolean) => void;
}

const entryDomId = (e: CardEntry) =>
  e.type === "group" ? `group:${e.name}` : `app:${e.app.id}`;

/**
 * Drag-to-group card grid built on @dnd-kit. Reorders standalone cards, forms groups
 * (drop card on card → name/role dialog), joins groups (drop on a group), leaves groups
 * (drop a member on the standalone area), and reorders members within a group. A
 * cursor-following DragOverlay lifts the card; drop targets light up. Planning stays in
 * Core (RegroupPlanner) — see lib/regroup.ts. Activation distance keeps clicks working.
 */
export function CardGridDnd(props: CardGridDndProps) {
  const {
    entries,
    apps,
    onLaunch,
    onStop,
    onToast,
    onEdit,
    onRemove,
    onFavorite,
    onShowLogs,
    onRegroup,
    onDragStateChange,
  } = props;

  const [active, setActive] = useState<DragKind | null>(null);
  const [overId, setOverId] = useState<string | null>(null);

  // Pending NewGroup dialog (drag formed a brand-new group; awaiting name + roles).
  const [pendingGroup, setPendingGroup] = useState<{
    draggedId: string;
    target: RegroupTarget;
    request: GroupNameRequest;
  } | null>(null);

  // Distance constraint: a real drag only begins after the pointer moves 6px, so a plain
  // click on a card button still fires (dnd-kit won't swallow it). This is the polish knob
  // that keeps launch/stop/favorite/menu/expand clickable.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
  );

  const appById = useMemo(() => {
    const m = new Map<string, AppView>();
    for (const a of apps) m.set(a.id, a);
    return m;
  }, [apps]);

  const topLevelIds = useMemo(() => entries.map(entryDomId), [entries]);

  function onDragStart(e: DragStartEvent) {
    const data = e.active.data.current as { drag?: DragKind } | undefined;
    if (data?.drag) {
      setActive(data.drag);
      onDragStateChange?.(true);
    }
  }

  function onDragOver(e: DragOverEvent) {
    setOverId(e.over ? String(e.over.id) : null);
  }

  async function onDragEnd(e: DragEndEvent) {
    const dragged = active;
    setActive(null);
    setOverId(null);
    onDragStateChange?.(false);
    if (!dragged) return;

    const overRaw = e.over?.id ? String(e.over.id) : null;
    if (!overRaw) return;

    const target = resolveTarget(dragged.app, overRaw, appById, apps);
    if (!target) return;

    // The drop applies optimistically + persists in the background inside onRegroup — it does
    // NOT block on the heavy per-app status scan, so the grid settles instantly and you can
    // drag again immediately. A brand-new group is the one case that pauses for a name dialog.
    try {
      const res = await onRegroup(dragged.app.id, target);
      if (res.needsNameDialog && res.suggestedGroupName && res.seeds) {
        setPendingGroup({
          draggedId: dragged.app.id,
          target,
          request: { suggestedName: res.suggestedGroupName, seeds: res.seeds },
        });
      }
    } catch (err) {
      onToast("error", err instanceof Error ? err.message : String(err));
    }
  }

  async function confirmNewGroup(name: string, roles: Record<string, string>) {
    const pending = pendingGroup;
    setPendingGroup(null);
    if (!pending) return;
    try {
      await onRegroup(pending.draggedId, pending.target, name, roles);
    } catch (err) {
      onToast("error", err instanceof Error ? err.message : String(err));
    }
  }

  return (
    <DndContext
      sensors={sensors}
      collisionDetection={pointerWithin}
      onDragStart={onDragStart}
      onDragOver={onDragOver}
      onDragEnd={onDragEnd}
      onDragCancel={() => {
        setActive(null);
        setOverId(null);
        onDragStateChange?.(false);
      }}
    >
      <CanvasDropZone activeDragging={!!active && !!active.app.group}>
        <SortableContext items={topLevelIds} strategy={rectSortingStrategy}>
          <div className="grid grid-cols-[repeat(auto-fill,minmax(300px,1fr))] gap-3">
            {entries.map((entry, i) => {
              const delay = Math.min(i * 35, 200);
              const style = { animationDelay: `${delay}ms` };
              const domId = entryDomId(entry);

              if (entry.type === "group") {
                return (
                  <DroppableGroup
                    key={domId}
                    domId={domId}
                    name={entry.name}
                    isOver={overId === domId && active?.app.group !== entry.name}
                  >
                    <GroupCard
                      name={entry.name}
                      members={entry.members}
                      onLaunch={onLaunch}
                      onStop={onStop}
                      onToast={onToast}
                      onEdit={onEdit}
                      onRemove={onRemove}
                      onFavorite={onFavorite}
                      onShowLogs={onShowLogs}
                      activeMemberId={active?.app.id ?? null}
                      overMemberId={overId}
                      style={style}
                    />
                  </DroppableGroup>
                );
              }

              return (
                <SortableAppCard
                  key={domId}
                  domId={domId}
                  app={entry.app}
                  isOver={overId === domId && active?.app.id !== entry.app.id}
                  onLaunch={onLaunch}
                  onStop={onStop}
                  onToast={onToast}
                  onEdit={onEdit}
                  onRemove={onRemove}
                  onFavorite={onFavorite}
                  onShowLogs={onShowLogs}
                  style={style}
                />
              );
            })}
          </div>
        </SortableContext>
      </CanvasDropZone>

      {/* Cursor-following lifted card — the card visibly follows the pointer. A short snappy
          drop animation keeps the release feeling instant (the layout has already updated
          optimistically, so a long settle would only add perceived lag). */}
      <DragOverlay dropAnimation={{ duration: 120, easing: "cubic-bezier(0.2,0,0,1)" }}>
        {active ? (
          <div className="w-[300px] rotate-[1.5deg] scale-[1.03] opacity-95 shadow-card-hover">
            <AppCard
              app={active.app}
              onLaunch={() => {}}
              onStop={() => {}}
              onToast={() => {}}
            />
          </div>
        ) : null}
      </DragOverlay>

      <GroupNameDialog
        request={pendingGroup?.request ?? null}
        onConfirm={confirmNewGroup}
        onCancel={() => setPendingGroup(null)}
      />
    </DndContext>
  );
}

// ── Resolve a dnd-kit "over" id to a Core drag intent ────────────────────────
function resolveTarget(
  dragged: AppView,
  overRaw: string,
  appById: Map<string, AppView>,
  apps: AppView[],
): RegroupTarget | null {
  if (overRaw === "canvas") {
    // Leaving a group → standalone. No-op when already standalone.
    return dragged.group ? { kind: "onCanvas" } : null;
  }

  if (overRaw.startsWith("group:")) {
    const groupName = overRaw.slice("group:".length);
    if (dragged.group === groupName) return null; // already in it
    return { kind: "onGroup", groupName };
  }

  if (overRaw.startsWith("member:")) {
    // Dropped on a member row.
    const memberId = overRaw.slice("member:".length);
    const member = appById.get(memberId);
    if (!member || !member.group) return null;
    if (member.id === dragged.id) return null;
    // Same group as dragged → reorder; else join the member's group.
    if (dragged.group && dragged.group === member.group) {
      // Reorder is scoped to the dragged app's ROLE band (Core's PlanReorder). Compute the
      // target index = the over-member's slot among those peers. Only meaningful when the two
      // share a role; otherwise the bands differ and Core would no-op, so skip.
      if (member.role !== dragged.role) return null;
      const peers = apps
        .filter((a) => a.group === member.group && a.role === dragged.role)
        .filter((a) => a.id !== dragged.id);
      const index = Math.max(0, peers.findIndex((a) => a.id === member.id));
      return { kind: "atMemberIndex", groupName: member.group, index };
    }
    return { kind: "onGroup", groupName: member.group };
  }

  if (overRaw.startsWith("app:")) {
    const targetId = overRaw.slice("app:".length);
    if (targetId === dragged.id) return null;
    return { kind: "onApp", appId: targetId };
  }

  return null;
}

// ── Sortable standalone card (drag source + reorder + droppable group target) ─
function SortableAppCard({
  domId,
  app,
  isOver,
  style,
  ...handlers
}: {
  domId: string;
  app: AppView;
  isOver: boolean;
  style?: React.CSSProperties;
  onLaunch: (id: string) => void;
  onStop: (id: string) => void;
  onToast: (kind: "success" | "error", message: string) => void;
  onEdit: (app: AppView) => void;
  onRemove: (app: AppView) => void;
  onFavorite: (id: string, favorite: boolean) => void;
  onShowLogs?: (id: string) => void;
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: domId,
    data: { drag: { kind: "app", app } as DragKind },
  });

  const dndStyle: React.CSSProperties = {
    ...style,
    transform: CSS.Translate.toString(transform),
    transition,
    // The source card dims while its overlay clone trails the cursor.
    opacity: isDragging ? 0.4 : 1,
    cursor: "grab",
    // Required for dnd-kit PointerSensor: without touch-action:none the browser's
    // native pointer-capture (scroll/pan) fires before the sensor's pointerdown
    // handler, so drag never initiates — especially in WebView2's touch-enabled
    // Chromium surface.
    touchAction: "none",
  };

  return (
    <div
      ref={setNodeRef}
      style={dndStyle}
      {...attributes}
      {...listeners}
      className={cn(
        "relative rounded-card transition-shadow duration-[120ms]",
        // Drop affordance: ring + accent glow when a card is hovered as a group target.
        isOver &&
          "ring-2 ring-accent/70 ring-offset-2 ring-offset-surface-window shadow-accent-glow",
      )}
    >
      <AppCard app={app} {...handlers} />
      {isOver && (
        <div className="pointer-events-none absolute inset-0 z-10 flex items-center justify-center rounded-card bg-accent/10">
          <span className="rounded-pill bg-accent px-2 py-0.5 text-2xs font-semibold text-white shadow-card">
            ＋ group
          </span>
        </div>
      )}
    </div>
  );
}

// ── Droppable wrapper around a group card (join target) ───────────────────────
function DroppableGroup({
  domId,
  isOver,
  children,
}: {
  domId: string;
  name: string;
  isOver: boolean;
  children: React.ReactNode;
}) {
  const { setNodeRef } = useDroppable({ id: domId });
  return (
    <div
      ref={setNodeRef}
      className={cn(
        "relative rounded-card transition-shadow duration-[120ms]",
        isOver &&
          "ring-2 ring-accent/70 ring-offset-2 ring-offset-surface-window shadow-accent-glow",
      )}
    >
      {children}
      {isOver && (
        <div className="pointer-events-none absolute inset-0 z-10 flex items-center justify-center rounded-card bg-accent/10">
          <span className="rounded-pill bg-accent px-2 py-0.5 text-2xs font-semibold text-white shadow-card">
            ＋ add to group
          </span>
        </div>
      )}
    </div>
  );
}

// ── Standalone-area drop zone (drop a member here → leave its group) ──────────
function CanvasDropZone({
  activeDragging,
  children,
}: {
  activeDragging: boolean;
  children: React.ReactNode;
}) {
  const { setNodeRef, isOver } = useDroppable({ id: "canvas" });
  return (
    <div
      ref={setNodeRef}
      className={cn(
        "relative min-h-full rounded-card transition-all duration-[120ms]",
        // Hint the "drop here to ungroup" zone only while dragging a grouped member.
        activeDragging &&
          "outline-dashed outline-1 outline-offset-4 outline-hairline",
        activeDragging && isOver && "outline-accent/50 bg-accent/[0.03]",
      )}
    >
      {activeDragging && isOver && (
        <div className="pointer-events-none absolute right-2 top-2 z-20 rounded-pill bg-surface-elevated px-2 py-0.5 text-2xs font-medium text-text-secondary shadow-card">
          Release to ungroup
        </div>
      )}
      {children}
    </div>
  );
}
