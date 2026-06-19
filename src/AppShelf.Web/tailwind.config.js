/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // ── Surface ramp — driven by CSS variables (data-theme switching) ──
        // CSS vars store raw RGB channels (e.g. "15 15 28") so Tailwind can
        // inject alpha modifiers via rgb(var(--surface-window) / <alpha-value>).
        // Switch palette: document.documentElement.dataset.theme = "light" | "slate" | "indigo" | "warm"
        surface: {
          window:       "rgb(var(--surface-window) / <alpha-value>)",
          card:         "rgb(var(--surface-card) / <alpha-value>)",
          "card-hover": "rgb(var(--surface-card-hover) / <alpha-value>)",
          elevated:     "rgb(var(--surface-elevated) / <alpha-value>)",
          inset:        "rgb(var(--surface-inset) / <alpha-value>)",
        },

        // ── Accent (indigo-violet family) ───────────────────────────────────
        accent: {
          DEFAULT: "rgb(var(--accent-rgb) / <alpha-value>)",
          hover:   "rgb(var(--accent-hover-rgb) / <alpha-value>)",
          muted:   "var(--accent-muted)",
          subtle:  "var(--accent-subtle)",
        },

        // ── Borders ────────────────────────────────────────────────────────
        // hairline uses rgba() directly in CSS (--hairline-opacity controls it)
        // so we expose it as a plain CSS var reference for border-hairline class.
        hairline: "rgba(var(--hairline) / var(--hairline-opacity, 0.10))",

        // ── Text ───────────────────────────────────────────────────────────
        text: {
          primary:   "rgb(var(--text-primary) / <alpha-value>)",
          secondary: "rgb(var(--text-secondary) / <alpha-value>)",
          faint:     "rgb(var(--text-faint) / <alpha-value>)",
          accent:    "rgb(var(--text-accent) / <alpha-value>)",
        },

        // ── Status — driven by CSS vars (light theme uses deeper values) ───
        status: {
          running:  "rgb(var(--status-running) / <alpha-value>)",
          starting: "rgb(var(--status-starting) / <alpha-value>)",
          error:    "rgb(var(--status-error) / <alpha-value>)",
          stopped:  "rgb(var(--status-stopped) / <alpha-value>)",
          port:     "rgb(var(--status-port) / <alpha-value>)",
          crash:    "rgb(var(--status-crash) / <alpha-value>)",
        },
      },

      fontFamily: {
        sans: ["Inter", "Segoe UI Variable", "Segoe UI", "system-ui", "-apple-system", "sans-serif"],
        mono: ["JetBrains Mono", "Cascadia Code", "Consolas", "monospace"],
      },

      fontSize: {
        // Tight, deliberate type scale — Linear cadence
        "2xs": ["10px", { lineHeight: "14px", letterSpacing: "0.04em" }],
        xs:    ["11px", { lineHeight: "16px", letterSpacing: "0.02em" }],
        sm:    ["12px", { lineHeight: "18px", letterSpacing: "0.01em" }],
        base:  ["13px", { lineHeight: "20px", letterSpacing: "0" }],
        md:    ["14px", { lineHeight: "20px", letterSpacing: "-0.01em" }],
        lg:    ["15px", { lineHeight: "22px", letterSpacing: "-0.015em" }],
        xl:    ["18px", { lineHeight: "26px", letterSpacing: "-0.02em" }],
        "2xl": ["22px", { lineHeight: "30px", letterSpacing: "-0.025em" }],
      },

      borderRadius: {
        card:  "8px",   // slightly flatter than before — more Linear
        chip:  "4px",
        pill:  "999px",
        input: "6px",
      },

      // 8-px spacing rhythm
      spacing: {
        "0.5": "2px",
        "1":   "4px",
        "1.5": "6px",
        "2":   "8px",
        "2.5": "10px",
        "3":   "12px",
        "3.5": "14px",
        "4":   "16px",
        "5":   "20px",
        "6":   "24px",
        "7":   "28px",
        "8":   "32px",
        "9":   "36px",
        "10":  "40px",
        "12":  "48px",
        "14":  "56px",
        "16":  "64px",
      },

      boxShadow: {
        card:        "0 1px 3px rgba(0,0,0,0.55), 0 1px 2px rgba(0,0,0,0.35)",
        "card-hover":"0 8px 24px rgba(0,0,0,0.60), 0 2px 6px rgba(0,0,0,0.40)",
        elevated:    "0 4px 16px rgba(0,0,0,0.50)",
        inset:       "inset 0 1px 0 rgba(255,255,255,0.04)",
        "accent-glow":"0 0 0 2px rgba(110,98,219,0.35)",
      },

      keyframes: {
        // Card grid entrance
        "fade-up": {
          from: { opacity: "0", transform: "translateY(6px)" },
          to:   { opacity: "1", transform: "translateY(0)" },
        },
        // Starting state: opacity pulse (no halo, Linear-grade)
        "pulse-dot": {
          "0%, 100%": { opacity: "1" },
          "50%":      { opacity: "0.3" },
        },
        // Running indicator: subtle scale breathe
        "breathe": {
          "0%, 100%": { transform: "scale(1)", opacity: "1" },
          "50%":      { transform: "scale(1.15)", opacity: "0.85" },
        },
        // Group expand
        "slide-down": {
          from: { opacity: "0", transform: "translateY(-4px)" },
          to:   { opacity: "1", transform: "translateY(0)" },
        },
        // Status rail entrance
        "rail-grow": {
          from: { transform: "scaleY(0)", transformOrigin: "top" },
          to:   { transform: "scaleY(1)", transformOrigin: "top" },
        },
      },

      animation: {
        "fade-up":   "fade-up 0.20s cubic-bezier(0.16,1,0.3,1) both",
        "pulse-dot": "pulse-dot 1.2s ease-in-out infinite",
        "breathe":   "breathe 2.4s ease-in-out infinite",
        "slide-down":"slide-down 0.14s cubic-bezier(0.16,1,0.3,1) both",
        "rail-grow": "rail-grow 0.18s cubic-bezier(0.16,1,0.3,1) both",
      },

      transitionDuration: {
        "120": "120ms",
        "150": "150ms",
        "200": "200ms",
      },

      transitionTimingFunction: {
        "out-expo": "cubic-bezier(0.16,1,0.3,1)",
      },
    },
  },
  plugins: [],
};
