/**
 * capture-palettes.mjs
 * Builds the AppShelf web UI fresh, then renders each of the 4 palette themes
 * using Playwright + vite preview. Theme is injected into the HTML source via
 * route interception (most reliable method — CDP and post-load setAttribute
 * both fail because Chromium's style engine doesn't always propagate CSS
 * variable changes on :root to computed styles of child elements in headless).
 *
 * Run: node scripts/capture-palettes.mjs   (from src/AppShelf.Web directory)
 *
 * To set the active palette as the default, see index.css — change the
 * :root:not([data-theme]) block to copy the desired theme's token values,
 * OR pass data-theme in index.html directly.
 */

import { chromium }               from "playwright";
import { writeFileSync, mkdirSync,
         readFileSync, statSync }  from "node:fs";
import { join, resolve }           from "node:path";
import { spawn, execSync }         from "node:child_process";

// ── Config ─────────────────────────────────────────────────────────────────
const PREVIEW_PORT = 5198;
const PREVIEW_URL  = `http://localhost:${PREVIEW_PORT}`;
const OUT_DIR      = resolve("../../process/general-plans/references");
const VIEWPORT     = { width: 1440, height: 820 };
const WAIT_MS      = 1000;   // settle after React mount + animations

const THEMES = [
  { id: "light",  label: "1  Light",            file: "appshelf-pal-light.png"  },
  { id: "slate",  label: "2  Soft Slate (mid)", file: "appshelf-pal-slate.png"  },
  { id: "indigo", label: "3  Indigo Dark",       file: "appshelf-pal-indigo.png" },
  { id: "warm",   label: "4  Warm Dim",          file: "appshelf-pal-warm.png"   },
];

// ── Helpers ─────────────────────────────────────────────────────────────────
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function waitForServer(url, timeoutMs = 30_000) {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    try {
      const r = await fetch(url, { signal: AbortSignal.timeout(1500) });
      if (r.ok || r.status < 500) return;
    } catch { /* not ready yet */ }
    await sleep(300);
  }
  throw new Error(`Server at ${url} did not start within ${timeoutMs}ms`);
}

// ── Main ───────────────────────────────────────────────────────────────────
async function main() {
  mkdirSync(OUT_DIR, { recursive: true });

  // Step 1: Fresh build so Tailwind CSS tokens are current
  console.log("Building (tsc + vite build)…");
  execSync("npm run build", { cwd: resolve("."), stdio: "inherit" });
  console.log("  Build complete.\n");

  // Step 2: Serve the built dist/ via vite preview
  console.log(`Starting vite preview on :${PREVIEW_PORT}…`);
  const preview = spawn(
    "npx", ["vite", "preview", "--port", String(PREVIEW_PORT), "--strictPort"],
    { cwd: resolve("."), shell: true, stdio: "pipe" }
  );
  preview.stderr.on("data", () => {});

  try {
    await waitForServer(PREVIEW_URL);
    console.log("  Preview ready.\n");

    const browser = await chromium.launch({ headless: true });
    const capturedPaths = [];
    const capturedSizes = [];

    for (const theme of THEMES) {
      console.log(`Capturing: ${theme.label}…`);

      // Fresh context per theme to avoid any style cache bleed
      const ctx  = await browser.newContext({ viewport: VIEWPORT });
      const page = await ctx.newPage();

      // Inject data-theme into the HTML source via route interception.
      // This is more reliable than setAttribute/CDP: Chromium's headless style
      // engine may not propagate CSS var changes on :root to child elements
      // computed styles when set post-load or via onNewDocument scripts.
      await page.route("**/*", async (route) => {
        const req = route.request();
        const url = req.url();
        if (url === `${PREVIEW_URL}/` || url.endsWith("/index.html")) {
          const response = await route.fetch();
          let html = await response.text();
          // Inject data-theme into the <html> opening tag
          html = html.replace(/<html([^>]*)>/, (_, attrs) => {
            // Remove any existing data-theme attr, then add ours
            const clean = attrs.replace(/\s*data-theme="[^"]*"/, "");
            return `<html${clean} data-theme="${theme.id}">`;
          });
          await route.fulfill({ response, body: html });
        } else {
          await route.continue();
        }
      });

      await page.goto(PREVIEW_URL, { waitUntil: "networkidle", timeout: 25_000 });
      await sleep(WAIT_MS);

      // Verify computed colors are correct
      const check = await page.evaluate(() => {
        const s    = getComputedStyle(document.documentElement);
        const div  = document.querySelector("#root > div");
        return {
          theme:            document.documentElement.dataset.theme,
          surfaceWindowVar: s.getPropertyValue("--surface-window").trim(),
          bodyBg:           getComputedStyle(document.body).backgroundColor,
          divBg:            div ? getComputedStyle(div).backgroundColor : "n/a",
        };
      });
      console.log(`  theme="${check.theme}"  --surface-window="${check.surfaceWindowVar}"  body="${check.bodyBg}"`);
      if (check.theme !== theme.id) {
        console.warn(`  WARNING: expected theme="${theme.id}" but got "${check.theme}"`);
      }

      const outPath = join(OUT_DIR, theme.file);
      await page.screenshot({ path: outPath, fullPage: false });
      capturedPaths.push(outPath);
      await ctx.close();

      const sizeBytes = statSync(outPath).size;
      capturedSizes.push(sizeBytes);
      console.log(`  -> ${outPath}  (${(sizeBytes / 1024).toFixed(1)} KB)`);
    }

    // ── Distinctness verification ────────────────────────────────────────
    console.log("\n─── Distinctness check ──────────────────────────────────────");
    THEMES.forEach((t, i) => {
      console.log(`  ${t.label.padEnd(24)} ${(capturedSizes[i] / 1024).toFixed(1).padStart(6)} KB`);
    });
    const spread = Math.max(...capturedSizes) - Math.min(...capturedSizes);
    console.log(`  Spread: ${(spread / 1024).toFixed(1)} KB`);
    if (spread < 5000) {
      console.warn("  WARNING: spread < 5 KB — themes may still look too similar!");
    } else {
      console.log("  OK — themes are genuinely distinct.");
    }

    // ── 2×2 contact sheet ────────────────────────────────────────────────
    console.log("\nCompositing 2×2 contact sheet…");
    const b64 = capturedPaths.map(
      (p) => "data:image/png;base64," + readFileSync(p).toString("base64")
    );

    const LABEL_H = 36;
    const CELL_W  = Math.round(VIEWPORT.width  / 2);
    const CELL_H  = Math.round(VIEWPORT.height / 2);
    const GAP     = 8;
    const SHEET_W = CELL_W * 2 + GAP * 3;
    const SHEET_H = (CELL_H + LABEL_H) * 2 + GAP * 3;

    const cells = THEMES.map((t, i) => ({
      ...t,
      b64:  b64[i],
      size: capturedSizes[i],
      x:    GAP + (i % 2) * (CELL_W + GAP),
      y:    GAP + Math.floor(i / 2) * (CELL_H + LABEL_H + GAP),
    }));

    const sheetHtml = `<!DOCTYPE html>
<html><head><meta charset="utf-8"><style>
*{margin:0;padding:0;box-sizing:border-box}
body{background:#080810;width:${SHEET_W}px;height:${SHEET_H}px;position:relative;overflow:hidden;font-family:"Segoe UI",system-ui,sans-serif}
.cell{position:absolute;border-radius:6px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,.55)}
.label{display:flex;align-items:center;justify-content:space-between;height:${LABEL_H}px;background:#13132A;padding:0 14px;font:600 13px/1 "Segoe UI",system-ui,sans-serif;color:#C0BCEE;letter-spacing:.01em;border-bottom:1px solid rgba(255,255,255,.06)}
.size{font:400 11px/1 monospace;color:#6868A0}
.shot{display:block;width:${CELL_W}px;height:${CELL_H}px;object-fit:cover;object-position:top left}
</style></head><body>
${cells.map((c) => `<div class="cell" style="left:${c.x}px;top:${c.y}px;width:${CELL_W}px">
  <div class="label"><span>${c.label}</span><span class="size">${(c.size/1024).toFixed(0)} KB</span></div>
  <img class="shot" src="${c.b64}"></div>`).join("\n")}
</body></html>`;

    const htmlPath = join(OUT_DIR, "_contact-sheet.html");
    writeFileSync(htmlPath, sheetHtml);

    const sheetCtx  = await browser.newContext();
    const sheetPage = await sheetCtx.newPage();
    await sheetPage.setViewportSize({ width: SHEET_W, height: SHEET_H });
    await sheetPage.goto(`file://${htmlPath}`, { waitUntil: "load" });
    await sleep(400);

    const sheetPath = join(OUT_DIR, "appshelf-palette-options.png");
    await sheetPage.screenshot({ path: sheetPath, fullPage: false });
    console.log(`  -> ${sheetPath}`);

    await browser.close();

  } finally {
    preview.kill();
    console.log("\nPreview server stopped.");
  }

  console.log("\nDone.");
}

main().catch((err) => { console.error("FATAL:", err); process.exit(1); });
