// Mock data served when window.chrome.webview is absent (plain browser / dev / screenshot).
// Mirrors the shape AppShelf.Core posts back via the bridge.
import type { AppView } from "./types";

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
