/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // ── Surface ramp — TRUE Ostara palette (krud-dev/ostara Minimal MUI) ──
        // Cool blue-slate hue family. Cards are LIGHTER than the window.
        // Literal hex values baked by Tailwind at build time (single theme).
        surface: {
          window:       "#161C24",   // Ostara background — deepest cool blue-slate
          card:         "#212B36",   // Ostara paper — clearly lighter than window
          "card-hover": "#2A3540",   // card hover — subtle lift, same hue family
          elevated:     "#323D4A",   // elevated panels / dropdowns / popovers
          inset:        "#0F141B",   // inset / recessed areas — slightly darker than window
        },

        // ── Accent — Ostara GREEN (the defining change from prior violet pass) ─
        // Source: Ostara Minimal palette primary
        accent: {
          DEFAULT: "#00AB55",        // Ostara primary.main — emerald green
          hover:   "#007B55",        // Ostara primary.dark
          light:   "#5BE584",        // Ostara primary.light
          lighter: "#C8FACD",        // Ostara primary.lighter
          dark:    "#007B55",        // Ostara primary.dark
          darker:  "#005249",        // Ostara primary.darker
          muted:   "rgba(0,171,85,0.15)",   // green tint for chip backgrounds
          subtle:  "rgba(0,171,85,0.07)",   // very faint green fill
        },

        // ── Hairline — cool gray at low opacity ───────────────────────────
        // Ostara: white @ ~8% feels right on these surfaces
        hairline: "rgba(145,158,171,0.08)",

        // ── Text — Ostara cool blue-gray ramp ─────────────────────────────
        text: {
          primary:   "#F9FAFB",    // near-white — Ostara text.primary
          secondary: "#919EAB",    // Ostara text.secondary — muted labels/meta
          faint:     "#637381",    // Ostara text.disabled — timestamps, placeholders
          accent:    "#5BE584",    // Ostara primary.light — URL links when running
        },

        // ── Status — harmonized with Ostara green family ───────────────────
        // Running uses the Ostara green so accent and "running" are on-brand.
        status: {
          running:  "#00AB55",    // Ostara primary.main — running = accent green
          starting: "#FFAB00",    // Ostara warning.main — amber
          error:    "#FF5630",    // Ostara error.main — red
          stopped:  "#454F5B",    // Ostara grey.700 — cool gray, intentionally dim
          port:     "#FF5630",    // same as error
          crash:    "#FF7350",    // distinct orange-red (slightly lighter than error)
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
        "accent-glow":"0 0 0 2px rgba(0,171,85,0.35)",
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
        // Bottom drawer entrance (log panel)
        "slide-up": {
          from: { opacity: "0", transform: "translateY(12px)" },
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
        "slide-up":  "slide-up 0.16s cubic-bezier(0.16,1,0.3,1) both",
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
