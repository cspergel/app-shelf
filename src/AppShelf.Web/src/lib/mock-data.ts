// Mock data served when window.chrome.webview is absent (plain browser / dev / screenshot).
// Mirrors the shape AppShelf.Core posts back via the bridge.
import type {
  AppView,
  PortRowView,
  KillResult,
  ReclaimResult,
  ServiceStopResult,
} from "./types";

export const MOCK_APPS: AppView[] = [
  // ── SNF Admit Assist group ────────────────────────────────────────────────
  {
    id: "snf-frontend",
    name: "SNF Frontend",
    url: "http://localhost:5173",
    framework: "Vite · React",
    favorite: true,
    group: "SNF Admit Assist",
    role: "frontend",
    port: 5173,
    status: "Running",
    tags: ["#snf", "#clinical"],
    dir: "C:\\dev\\snf\\frontend",
  },
  {
    id: "snf-backend",
    name: "SNF Backend",
    url: "http://127.0.0.1:8000",
    framework: "FastAPI",
    favorite: false,
    group: "SNF Admit Assist",
    role: "backend",
    port: 8000,
    status: "Running",
    tags: ["#snf"],
    dir: "C:\\dev\\snf\\backend",
  },

  // ── Standalone apps ───────────────────────────────────────────────────────
  {
    id: "portfolio",
    name: "Portfolio Site",
    url: "http://localhost:3000",
    framework: "Next.js",
    favorite: false,
    group: null,
    role: "",
    port: 3000,
    status: "Stopped",
    tags: ["#personal"],
    dir: "C:\\dev\\portfolio",
  },
  {
    id: "design-system",
    name: "Design System",
    url: "http://localhost:6006",
    framework: "Storybook",
    favorite: false,
    group: null,
    role: "",
    port: 6006,
    status: "Starting",
    tags: ["#ui", "#personal"],
    dir: "C:\\dev\\design-system",
  },
  {
    id: "ml-dashboard",
    name: "ML Dashboard",
    url: "http://localhost:8501",
    framework: "Streamlit",
    favorite: false,
    group: null,
    role: "",
    port: 8501,
    status: "StoppedUnexpectedly",
    tags: ["#ml"],
    dir: "C:\\dev\\ml-dashboard",
  },
  {
    id: "dev-proxy",
    name: "Dev Proxy",
    url: "http://localhost:8080",
    framework: "Node.js",
    favorite: false,
    group: null,
    role: "",
    port: 8080,
    status: "PortInUse",
    tags: [],
    dir: "C:\\dev\\dev-proxy",
  },

  // URL-only app (no dir) — dir-dependent quick actions are hidden for this one.
  {
    id: "internal-wiki",
    name: "Internal Wiki",
    url: "https://wiki.internal.example.com",
    framework: null,
    favorite: false,
    group: null,
    role: "",
    port: null,
    status: "Running",
    tags: ["#docs"],
    dir: null,
  },

  // ── Extra loose cards so drag-to-group is fully demoable in a plain browser ──
  // (the real user currently has only one group; these give something to drag.)
  {
    id: "shop-frontend",
    name: "Shop Frontend",
    url: "http://localhost:5174",
    framework: "Vite · React",
    favorite: false,
    group: null,
    role: "",
    port: 5174,
    status: "Stopped",
    tags: ["#shop"],
    dir: "C:\\dev\\shop\\frontend",
  },
  {
    id: "shop-backend",
    name: "Shop Backend",
    url: "http://127.0.0.1:8001",
    framework: "FastAPI",
    favorite: false,
    group: null,
    role: "",
    port: 8001,
    status: "Stopped",
    tags: ["#shop"],
    dir: "C:\\dev\\shop\\backend",
  },
  {
    id: "blog",
    name: "Blog",
    url: "http://localhost:4321",
    framework: "Astro",
    favorite: false,
    group: null,
    role: "",
    port: 4321,
    status: "Stopped",
    tags: ["#personal"],
    dir: "C:\\dev\\blog",
  },
];

// Optimistic in-memory state for mock actions (no-op bridge in browser)
let mockState: AppView[] = MOCK_APPS.map((a) => ({ ...a }));

export function getMockApps(): AppView[] {
  return mockState.map((a) => ({ ...a }));
}

export function mockLaunch(id: string): void {
  mockState = mockState.map((a) =>
    a.id === id ? { ...a, status: "Starting" } : a,
  );
  // Simulate transition to Running after 1.5s
  setTimeout(() => {
    mockState = mockState.map((a) =>
      a.id === id ? { ...a, status: "Running" } : a,
    );
  }, 1500);
}

export function mockStop(id: string): void {
  mockState = mockState.map((a) =>
    a.id === id ? { ...a, status: "Stopped" } : a,
  );
}

// ── Add / Edit dialog mock fallback ──────────────────────────────────────────
// In a plain browser (no WebView2) the dialog is still fully demoable: pickFolder
// returns a fake path, detectStack returns plausible suggestions, and add/update/
// remove mutate the in-memory mock list (so the grid reflects the change on poll).

export function mockPickFolder(): string {
  return "C:\\dev\\sample-app";
}

export function mockDetectStack(_dir: string): {
  framework: string | null;
  cmd: string | null;
  url: string | null;
  port: number | null;
  installCmd: string | null;
  dir: string | null;
  detected: boolean;
} {
  return {
    framework: "Vite · React",
    cmd: "npm run dev",
    url: "http://localhost:5173",
    port: 5173,
    installCmd: "npm install",
    dir: null,
    detected: true,
  };
}

/** Slugify a name the way Core's Slug.From roughly does (lowercase, hyphenated). */
function mockSlug(name: string): string {
  const base =
    name
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "") || "app";
  // Ensure uniqueness against the current mock state.
  let id = base;
  let n = 2;
  while (mockState.some((a) => a.id === id)) id = `${base}-${n++}`;
  return id;
}

export function mockAddApp(app: AppView): AppView {
  const created: AppView = { ...app, id: app.id || mockSlug(app.name) };
  mockState = [...mockState, created];
  return created;
}

export function mockUpdateApp(app: AppView): AppView {
  mockState = mockState.map((a) => (a.id === app.id ? { ...a, ...app } : a));
  return app;
}

export function mockRemoveApp(id: string): void {
  mockState = mockState.filter((a) => a.id !== id);
}

/** Persist a favorite toggle into the in-memory mock state (survives the session). */
export function mockSetFavorite(id: string, favorite: boolean): AppView | null {
  let updated: AppView | null = null;
  mockState = mockState.map((a) => {
    if (a.id !== id) return a;
    updated = { ...a, favorite };
    return updated;
  });
  return updated;
}

export function mockListGroups(): string[] {
  return [
    ...new Set(
      mockState
        .map((a) => a.group)
        .filter((g): g is string => !!g && g.trim().length > 0)
        .map((g) => g.trim()),
    ),
  ].sort((a, b) => a.localeCompare(b));
}

export function mockReservedPorts(excludeId?: string): number[] {
  return mockState
    .filter((a) => a.port != null && a.id !== excludeId)
    .map((a) => a.port as number);
}

// ── Drag-to-group mock fallback ──────────────────────────────────────────────
// Mirrors AppShelf.Core RegroupPlanner just enough to make drag fully demoable in a
// plain browser: create group / join / leave / move / reorder all visibly mutate the
// in-memory mock list. The real bridge runs the canonical Core planner; this is only
// for the no-WebView2 demo path.

import type { RegroupTarget } from "./regroup";

/** A drop target (browser) maps to the same shape the bridge sends C#. */
function guessRole(framework: string | null): string {
  const f = (framework ?? "").toLowerCase();
  const fe = ["vite", "next", "react", "astro", "svelte", "angular", "storybook"];
  const be = ["fastapi", "flask", "uvicorn", "streamlit", "gradio", "django", "node"];
  if (fe.some((t) => f.includes(t))) return "frontend";
  if (be.some((t) => f.includes(t))) return "backend";
  return "other";
}

function membersOf(group: string): AppView[] {
  return mockState.filter((a) => a.group === group);
}

/** Dissolve a group down to its lone remaining member (clears that member's group). */
function dissolveIfOrphaned(group: string | null): void {
  if (!group) return;
  const rest = membersOf(group);
  if (rest.length === 1) {
    mockState = mockState.map((a) =>
      a.id === rest[0].id ? { ...a, group: null, role: a.role || "" } : a,
    );
  }
}

/** Suggest a name for two apps being grouped (shared dir folder → common words → fallback). */
function suggestName(a: AppView, b: AppView): string {
  const folder = (d?: string | null) => {
    if (!d) return null;
    const parts = d.replace(/\//g, "\\").replace(/\\+$/, "").split("\\");
    return parts.length >= 2 ? parts[parts.length - 2] : null;
  };
  const fa = folder(a.dir);
  const fb = folder(b.dir);
  if (fa && fb && fa.toLowerCase() === fb.toLowerCase()) return fa;
  const wa = a.name.trim().split(/\s+/);
  const wb = b.name.trim().split(/\s+/);
  const common: string[] = [];
  for (let i = 0; i < Math.min(wa.length, wb.length); i++) {
    if (wa[i].toLowerCase() !== wb[i].toLowerCase()) break;
    common.push(wa[i]);
  }
  if (common.length) return common.join(" ");
  return `${a.name.trim()} group`;
}

/** Result of a mock regroup: applied changes, or a name-dialog request for NewGroup. */
export interface MockRegroupResult {
  applied: boolean;
  needsNameDialog: boolean;
  suggestedGroupName?: string;
  seeds?: { id: string; name: string; role: string }[];
}

export function mockApplyRegroup(
  draggedId: string,
  target: RegroupTarget,
  groupName?: string,
  roles?: Record<string, string>,
): MockRegroupResult {
  const dragged = mockState.find((a) => a.id === draggedId);
  if (!dragged) return { applied: false, needsNameDialog: false };

  const setGroup = (id: string, group: string | null, role: string) => {
    mockState = mockState.map((a) =>
      a.id === id ? { ...a, group, role } : a,
    );
  };

  // Resolve a destination group name from the target.
  if (target.kind === "onCanvas") {
    if (!dragged.group) return { applied: false, needsNameDialog: false };
    const src = dragged.group;
    setGroup(draggedId, null, dragged.role || "");
    dissolveIfOrphaned(src);
    return { applied: true, needsNameDialog: false };
  }

  if (target.kind === "atMemberIndex") {
    // Reorder within a role band: renumber via array index order. Mock only needs to
    // visibly move the dragged member; reorder by reinserting in members display order.
    const group = target.groupName;
    if (dragged.group !== group) return { applied: false, needsNameDialog: false };
    const peers = membersOf(group).filter((m) => m.role === dragged.role);
    const without = peers.filter((m) => m.id !== draggedId);
    const idx = Math.max(0, Math.min(target.index, without.length));
    without.splice(idx, 0, dragged);
    // Reflect order by rebuilding mockState with peers reordered in place.
    const order = new Map(without.map((m, i) => [m.id, i]));
    mockState = [...mockState].sort((a, b) => {
      const oa = order.get(a.id);
      const ob = order.get(b.id);
      if (oa == null || ob == null) return 0;
      return oa - ob;
    });
    return { applied: true, needsNameDialog: false };
  }

  // onGroup / onApp both resolve to "join this group" (onApp onto a grouped card joins it).
  let destGroup: string | null = null;
  let joiner = dragged;

  if (target.kind === "onGroup") {
    destGroup = target.groupName;
  } else if (target.kind === "onApp") {
    if (target.appId === draggedId) return { applied: false, needsNameDialog: false };
    const tgt = mockState.find((a) => a.id === target.appId);
    if (!tgt) return { applied: false, needsNameDialog: false };
    if (tgt.group) {
      destGroup = tgt.group;
    } else if (dragged.group) {
      // Dragged is grouped, target standalone → pull the standalone into dragged's group.
      destGroup = dragged.group;
      joiner = tgt;
    } else {
      // Both standalone → NEW group.
      const suggested = groupName?.trim() || suggestName(dragged, tgt);
      // Does a real group already exist with that name? Then it's a merge (no dialog).
      const exists = membersOf(suggested).length > 0;
      if (!groupName && !exists) {
        return {
          applied: false,
          needsNameDialog: true,
          suggestedGroupName: suggested,
          seeds: [dragged, tgt].map((a) => ({
            id: a.id,
            name: a.name,
            role: guessRole(a.framework),
          })),
        };
      }
      const draggedRole = roles?.[dragged.id] ?? guessRole(dragged.framework);
      const tgtRole = roles?.[tgt.id] ?? guessRole(tgt.framework);
      setGroup(dragged.id, suggested, draggedRole);
      setGroup(tgt.id, suggested, tgtRole);
      return { applied: true, needsNameDialog: false };
    }
  }

  if (!destGroup) return { applied: false, needsNameDialog: false };
  if (joiner.group === destGroup) return { applied: false, needsNameDialog: false };

  const src = joiner.group;
  setGroup(joiner.id, destGroup, guessRole(joiner.framework));
  dissolveIfOrphaned(src ?? null);
  return { applied: true, needsNameDialog: false };
}

export function mockRenameGroup(oldName: string, newName: string): void {
  const trimmed = newName.trim();
  if (!trimmed || trimmed === oldName) return;
  mockState = mockState.map((a) =>
    a.group === oldName ? { ...a, group: trimmed } : a,
  );
}

// ── Port Doctor mock data ────────────────────────────────────────────────────
// One row across each tier, plus a service-backed port and an ancestry chain with
// a [dead] node — so the Ports view renders fully in a plain browser.
export const MOCK_PORTS: PortRowView[] = [
  // Managed — AppShelf launched it (one of our dev servers)
  {
    port: 5173,
    family: "ipv4",
    tier: "Managed",
    pid: 18244,
    processName: "node",
    isService: false,
    serviceName: null,
    parentAlive: true,
    ownerAppId: "snf-frontend",
    ownerAppName: "SNF Frontend",
    exePath: "C:\\Program Files\\nodejs\\node.exe",
    commandLine: "node C:\\dev\\snf\\frontend\\node_modules\\vite\\bin\\vite.js",
    exeDir: "C:\\Program Files\\nodejs",
    startedAt: new Date(Date.now() - 1000 * 60 * 14).toISOString(),
    ancestry: [
      {
        pid: 9120,
        processName: "AppShelf",
        alive: true,
        commandLine: "C:\\dist\\AppShelf.exe",
        exeDir: "C:\\dist",
      },
    ],
  },
  // LikelyOrphaned — maps to a registered app but the launcher is dead (Reclaim offered)
  {
    port: 8000,
    family: "ipv4",
    tier: "LikelyOrphaned",
    pid: 24880,
    processName: "python",
    isService: false,
    serviceName: null,
    parentAlive: false,
    ownerAppId: "snf-backend",
    ownerAppName: "SNF Backend",
    exePath: "C:\\Python312\\python.exe",
    commandLine: "python -m uvicorn app.main:app --port 8000",
    exeDir: "C:\\Python312",
    startedAt: new Date(Date.now() - 1000 * 60 * 60 * 3).toISOString(),
    ancestry: [
      {
        pid: 15012,
        processName: "cmd",
        alive: false,
        commandLine: "cmd /c uvicorn app.main:app",
        exeDir: "C:\\Windows\\System32",
      },
      {
        pid: 7704,
        processName: "WindowsTerminal",
        alive: false,
        commandLine: null,
        exeDir: "C:\\Program Files\\WindowsApps\\Microsoft.WindowsTerminal",
      },
    ],
  },
  // Service-backed — Stop service offered (needs UAC)
  {
    port: 5432,
    family: "ipv4",
    tier: "Unknown",
    pid: 4096,
    processName: "postgres",
    isService: true,
    serviceName: "postgresql-x64-16",
    parentAlive: true,
    ownerAppId: null,
    ownerAppName: null,
    exePath: "C:\\Program Files\\PostgreSQL\\16\\bin\\postgres.exe",
    commandLine: "\"C:\\Program Files\\PostgreSQL\\16\\bin\\postgres.exe\" -D ...",
    exeDir: "C:\\Program Files\\PostgreSQL\\16\\bin",
    startedAt: new Date(Date.now() - 1000 * 60 * 60 * 48).toISOString(),
    ancestry: [
      {
        pid: 768,
        processName: "services",
        alive: true,
        commandLine: null,
        exeDir: "C:\\Windows\\System32",
      },
    ],
  },
  // Registered — maps to a registered app, parent alive ("yours")
  {
    port: 3000,
    family: "ipv6",
    tier: "Registered",
    pid: 13560,
    processName: "node",
    isService: false,
    serviceName: null,
    parentAlive: true,
    ownerAppId: "portfolio",
    ownerAppName: "Portfolio Site",
    exePath: "C:\\Program Files\\nodejs\\node.exe",
    commandLine: "node next dev",
    exeDir: "C:\\Program Files\\nodejs",
    startedAt: new Date(Date.now() - 1000 * 60 * 22).toISOString(),
    ancestry: [],
  },
  // Unknown — no registered app on this port (never auto-kill)
  {
    port: 7000,
    family: "ipv4",
    tier: "Unknown",
    pid: 2210,
    processName: "ControlCenter",
    isService: false,
    serviceName: null,
    parentAlive: true,
    ownerAppId: null,
    ownerAppName: null,
    exePath: "C:\\Program Files\\Apple\\AirPlay\\ControlCenter.exe",
    commandLine: null,
    exeDir: "C:\\Program Files\\Apple\\AirPlay",
    startedAt: null,
    ancestry: [],
  },
];

export function getMockPorts(): PortRowView[] {
  return MOCK_PORTS.map((p) => ({ ...p }));
}

// No-op mock actions: return an optimistic success so the UI flow can be exercised.
export function mockKillPort(_port: number): KillResult {
  return { success: true, reason: null, accessDenied: false };
}
export function mockReclaimPort(_ownerAppId: string, _port: number): ReclaimResult {
  return {
    kill: { success: true, reason: null, accessDenied: false },
    launch: { status: "Running", reason: null },
  };
}
export function mockStopService(_serviceName: string, _port: number): ServiceStopResult {
  return { started: true, cancelled: false, portFreed: true, reason: null };
}
