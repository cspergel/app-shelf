import { useEffect, useLayoutEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import {
  MoreHorizontal,
  TerminalSquare,
  Code2,
  FolderOpen,
  FileCog,
  BookOpen,
  Link2,
  ClipboardCopy,
  Pencil,
  Trash2,
  type LucideIcon,
} from "lucide-react";
import {
  openTerminal,
  openEditor,
  openFolder,
  openEnv,
  openDocs,
  copyToClipboard,
} from "@/lib/quick-actions";
import { cn } from "@/lib/utils";
import type { AppView, QuickActionResult } from "@/lib/types";

export interface CardMenuProps {
  app: AppView;
  /** Toast a result back to the user (success or friendly failure). */
  onResult: (kind: "success" | "error", message: string) => void;
  /** Tighter trigger for the cramped group-member row. */
  size?: "sm" | "md";
  /** Open the Edit dialog for this app. When omitted, the Edit item is hidden. */
  onEdit?: (app: AppView) => void;
  /** Remove this app (caller confirms first). When omitted, the Remove item is hidden. */
  onRemove?: (app: AppView) => void;
}

interface MenuItem {
  key: string;
  label: string;
  icon: LucideIcon;
  /** Hidden entirely when false (e.g. dir-dependent items on a URL-only app). */
  show: boolean;
  run: () => Promise<QuickActionResult> | QuickActionResult;
  /** Success toast text when the action reports ok and gave no reason of its own. */
  okMessage: string;
  /** A plain command item (no toast, no result) — used for Edit/Remove. */
  command?: () => void;
  /** Destructive styling (red) for the item. */
  danger?: boolean;
  /** Render a divider above this item. */
  dividerBefore?: boolean;
}

/**
 * The card's ⋯ overflow menu: per-app quick dev actions in the Ostara design. Opens a
 * popover anchored to the trigger, closes on outside-click / Esc. Dir-dependent items are
 * hidden for URL-only apps. Actions toast their result via onResult.
 */
// Menu width (px) — used to right-align the portal popover under the trigger.
const MENU_WIDTH = 184;

export function CardMenu({ app, onResult, size = "md", onEdit, onRemove }: CardMenuProps) {
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState<{ top: number; left: number } | null>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  // Position the portal popover under the trigger, right-aligned, clamped to the viewport.
  useLayoutEffect(() => {
    if (!open || !triggerRef.current) return;
    const r = triggerRef.current.getBoundingClientRect();
    const left = Math.max(8, Math.min(r.right - MENU_WIDTH, window.innerWidth - MENU_WIDTH - 8));
    setPos({ top: r.bottom + 4, left });
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      const t = e.target as Node;
      if (triggerRef.current?.contains(t) || menuRef.current?.contains(t)) return;
      setOpen(false);
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    const onScroll = () => setOpen(false);
    window.addEventListener("mousedown", onDown);
    window.addEventListener("keydown", onKey);
    window.addEventListener("scroll", onScroll, true);
    window.addEventListener("resize", onScroll);
    return () => {
      window.removeEventListener("mousedown", onDown);
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("scroll", onScroll, true);
      window.removeEventListener("resize", onScroll);
    };
  }, [open]);

  const hasDir = !!app.dir;
  const hasUrl = !!app.url;
  // Heuristic for "server app" — only servers have meaningful API docs.
  const isServer = app.role === "backend" || /fastapi|flask|django|express|uvicorn/i.test(app.framework ?? "");

  const items: MenuItem[] = [
    {
      key: "terminal",
      label: "Open terminal",
      icon: TerminalSquare,
      show: hasDir,
      run: () => openTerminal(app),
      okMessage: "Opened terminal",
    },
    {
      key: "editor",
      label: "Open in editor",
      icon: Code2,
      show: hasDir,
      run: () => openEditor(app),
      okMessage: "Opened editor",
    },
    {
      key: "folder",
      label: "Open folder",
      icon: FolderOpen,
      show: hasDir,
      run: () => openFolder(app),
      okMessage: "Opened folder",
    },
    {
      key: "env",
      label: "Open .env",
      icon: FileCog,
      show: hasDir,
      run: () => openEnv(app),
      okMessage: "Opened .env",
    },
    {
      key: "docs",
      label: "Open API docs",
      icon: BookOpen,
      show: hasUrl && isServer,
      run: () => openDocs(app),
      okMessage: "Opened docs",
    },
    {
      key: "copy-url",
      label: "Copy URL",
      icon: Link2,
      show: hasUrl,
      run: () => copyToClipboard(app.url),
      okMessage: "Copied URL",
    },
    {
      key: "copy-path",
      label: "Copy folder path",
      icon: ClipboardCopy,
      show: hasDir,
      run: () => copyToClipboard(app.dir ?? ""),
      okMessage: "Copied folder path",
    },
    // ── Manage (Edit / Remove) — only when wired ───────────────────────────
    {
      key: "edit",
      label: "Edit",
      icon: Pencil,
      show: !!onEdit,
      run: () => ({ ok: true, reason: null }),
      okMessage: "",
      command: () => onEdit?.(app),
      dividerBefore: true,
    },
    {
      key: "remove",
      label: "Remove",
      icon: Trash2,
      show: !!onRemove,
      run: () => ({ ok: true, reason: null }),
      okMessage: "",
      command: () => onRemove?.(app),
      danger: true,
    },
  ];

  const visible = items.filter((i) => i.show);

  const handle = async (item: MenuItem) => {
    setOpen(false);
    // Command items (Edit/Remove) fire a callback — no bridge call, no toast here.
    if (item.command) {
      item.command();
      return;
    }
    const result = await item.run();
    if (result.ok) {
      onResult("success", result.reason ?? item.okMessage);
    } else {
      onResult("error", result.reason ?? "Action failed");
    }
  };

  return (
    <div className="shrink-0">
      <button
        ref={triggerRef}
        onClick={(e) => {
          e.stopPropagation();
          setOpen((o) => !o);
        }}
        className={cn(
          "text-text-faint transition-colors duration-[120ms] rounded p-0.5",
          "hover:text-text-secondary hover:bg-surface-card-hover",
          open && "text-text-secondary bg-surface-card-hover",
          size === "md" ? "mt-0.5" : "",
        )}
        title="More options"
        aria-haspopup="menu"
        aria-expanded={open}
      >
        <MoreHorizontal className={size === "md" ? "h-3.5 w-3.5" : "h-3 w-3"} />
      </button>

      {open && pos &&
        createPortal(
          <div
            ref={menuRef}
            role="menu"
            style={{ top: pos.top, left: pos.left, width: MENU_WIDTH }}
            className={cn(
              "fixed z-50 origin-top-right",
              "rounded-card border border-hairline bg-surface-elevated p-1",
              "shadow-card-hover animate-fade-up",
            )}
          >
            {visible.length === 0 ? (
            <p className="px-2.5 py-2 text-2xs text-text-faint">No actions available</p>
          ) : (
            visible.map((item) => {
              const Icon = item.icon;
              return (
                <div key={item.key}>
                  {item.dividerBefore && (
                    <div className="my-1 border-top-hairline" />
                  )}
                  <button
                    role="menuitem"
                    onClick={() => void handle(item)}
                    className={cn(
                      "flex w-full items-center gap-2.5 rounded-[5px] px-2.5 py-1.5",
                      "text-xs font-medium text-left",
                      "transition-colors duration-[120ms]",
                      item.danger
                        ? "text-status-error/90 hover:bg-status-error/10 hover:text-status-error"
                        : "text-text-secondary hover:bg-accent-muted hover:text-text-primary",
                    )}
                  >
                    <Icon
                      className={cn(
                        "h-3.5 w-3.5 shrink-0",
                        item.danger ? "text-status-error/80" : "text-text-faint",
                      )}
                    />
                    <span className="truncate">{item.label}</span>
                  </button>
                </div>
              );
            })
            )}
          </div>,
          document.body,
        )}
    </div>
  );
}
