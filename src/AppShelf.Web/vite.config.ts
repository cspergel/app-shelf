import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import path from "node:path";

// Port 5199 deliberately: 5173 is a registered AppShelf app port (Vite default),
// and a clash would navigate the WebView2 host to the wrong dev server.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    port: 5199,
    strictPort: true,
  },
});
