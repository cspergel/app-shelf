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
