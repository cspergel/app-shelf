/** @type {import('tailwindcss').Config} */
export default {
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      colors: {
        // AppShelf palette (matches src/AppShelf.App/Themes/AppShelf.xaml).
        accent: {
          DEFAULT: "#7C6AEF",
          hover: "#9486F2",
          muted: "#241F3A",
        },
        surface: {
          window: "#0E0F13",
          card: "#16181F",
          "card-hover": "#1B1E27",
          elevated: "#21242E",
        },
        hairline: "#262A34",
        text: {
          primary: "#E6E8EC",
          secondary: "#9CA3AF",
          faint: "#565B66",
        },
        status: {
          running: "#34C98E",
          starting: "#E5A84B",
          error: "#E5645A",
          stopped: "#565B66",
        },
      },
      fontFamily: {
        sans: [
          "Inter",
          "Segoe UI",
          "system-ui",
          "-apple-system",
          "sans-serif",
        ],
        mono: ["JetBrains Mono", "Cascadia Code", "Consolas", "monospace"],
      },
      borderRadius: {
        card: "10px",
      },
      keyframes: {
        "fade-in": {
          from: { opacity: "0", transform: "translateY(4px)" },
          to: { opacity: "1", transform: "translateY(0)" },
        },
        "pulse-dot": {
          "0%, 100%": { opacity: "1" },
          "50%": { opacity: "0.35" },
        },
      },
      animation: {
        "fade-in": "fade-in 0.22s cubic-bezier(0.16,1,0.3,1) both",
        "pulse-dot": "pulse-dot 1.4s ease-in-out infinite",
      },
    },
  },
  plugins: [],
};
