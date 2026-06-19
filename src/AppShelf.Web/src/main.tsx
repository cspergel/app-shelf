import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App.tsx";
import "./index.css";

// ── Theme bootstrap — runs before first paint to avoid flash ─────────────────
// Priority: localStorage → prefers-color-scheme → default "dark"
(function () {
  const stored = localStorage.getItem("appshelf-theme") as "light" | "dark" | null;
  if (stored === "light" || stored === "dark") {
    document.documentElement.dataset.theme = stored;
  } else {
    // Respect OS preference on first visit; store it so subsequent visits are stable
    const prefersDark =
      !window.matchMedia || window.matchMedia("(prefers-color-scheme: light)").matches === false;
    const resolved = prefersDark ? "dark" : "light";
    document.documentElement.dataset.theme = resolved;
    localStorage.setItem("appshelf-theme", resolved);
  }
})();

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
