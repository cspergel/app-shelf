# AppShelf — Build Specification

**Document type:** Implementation spec for an AI dev agent (Claude Code)
**Scope:** Personal weekend build. Single user (the developer), Windows-first.
**Author intent:** A local launcher for the developer's own web/prototype projects so they don't open a terminal to start each one. Optimized for *convenience*, not for a commercial product, multi-user, or an app store.

---

## 0. Read this first (design philosophy)

This is a **personal cockpit**, not a product. The user is the only person who will ever run it. Every design decision follows from that:

- **The user knows their own stacks.** Detection only needs to handle the handful of frameworks they actually use, with manual override always available. Do not build a general-purpose stack-detection engine.
- **JSON config, not a database.** ~6–30 apps. A hand-editable, git-versionable JSON file in `%APPDATA%/AppShelf/config.json` is the single source of truth. No SQLite, no migrations.
- **One source of truth for logic.** "Add a project" and "kill a process tree" are implemented once in `AppShelf.Core`. The GUI, the CLI, and the skill are all thin front doors to that shared core.
- **Ship the usable thing first.** Terminal/CLI control works before the GUI. Browser launch works before embedded WebView2. The spotlight overlay is the daily-use feature and comes after the basics work.
- **Do not build the deferred list (Section 11).** It is tempting and it is wrong for v0.

The single sentence that defines success: **launching a local app should feel as fast as opening Spotlight.**

---

## 1. Stack & solution layout

- **Language/runtime:** C# / .NET 8
- **GUI:** WPF (Windows-native, clean WebView2 path later, low ceremony)
- **WebView2:** referenced but NOT used in v0 (browser launch first; see §6)
- **Config:** JSON file, no database
- **CLI:** .NET console app, published as a single-file `appshelf.exe` added to PATH

```
appshelf/
  AppShelf.sln
  src/
    AppShelf.Core/          # shared logic — the real engine
      Models/
        AppEntry.cs
        AppConfig.cs
        LaunchStatus.cs
      Config/
        ConfigStore.cs       # read/write/locate config.json
      Detection/
        StackDetector.cs     # infer cmd + url from a folder
        FrameworkRules.cs     # the detection rule table
      Process/
        ProcessManager.cs     # launch / stop / status
        JobObject.cs          # Win32 job object for process-tree kill
      Launch/
        Launcher.cs           # high-level: launch app -> poll -> open target
        TargetOpener.cs       # browser now; webview later
    AppShelf.Cli/           # `appshelf` command
      Program.cs
      Commands/
        AddCommand.cs
        ListCommand.cs
        LaunchCommand.cs
        StopCommand.cs
        RemoveCommand.cs
    AppShelf.App/           # WPF: tray + main window + spotlight overlay
      App.xaml(.cs)
      MainWindow.xaml(.cs)
      Spotlight/
        SpotlightWindow.xaml(.cs)
        HotkeyService.cs      # global hotkey registration
      Tray/
        TrayIcon.cs
      ViewModels/
        MainViewModel.cs
        AppCardViewModel.cs
  skill/
    appshelf-add/
      SKILL.md
  README.md
```

`AppShelf.Cli` and `AppShelf.App` both depend on `AppShelf.Core`. They contain **no business logic** — only argument parsing / UI binding that calls into Core.

---

## 2. Data model

### config.json (lives at `%APPDATA%/AppShelf/config.json`)

```json
{
  "version": 1,
  "apps": [
    {
      "id": "my-app",
      "name": "My App",
      "dir": "C:/projects/my-app",
      "cmd": "npm run dev",
      "url": "http://localhost:5173",
      "installCmd": "npm install",
      "tags": ["seo", "web"],
      "favorite": false,
      "open": "browser",
      "createdAt": "2026-06-06T00:00:00Z",
      "lastLaunchedAt": null
    }
  ]
}
```

### AppEntry.cs (fields)

| Field            | Type        | Notes                                                        |
| ---------------- | ----------- | ----------------------------------------------------------- |
| `id`             | string      | slug, unique; derived from name if not given                |
| `name`           | string      | display name                                                |
| `dir`            | string?     | working directory for local apps; null for pure URL apps    |
| `cmd`            | string?     | launch command; null for pure URL apps                      |
| `url`            | string      | the URL to open once running (or the URL for a URL-only app)|
| `installCmd`     | string?     | optional, e.g. `npm install`                                |
| `tags`           | string[]    | free-form                                                   |
| `favorite`       | bool        | default false                                               |
| `open`           | enum string | `"browser"` \| `"webview"` — v0 only honors `"browser"`     |
| `createdAt`      | ISO string  |                                                             |
| `lastLaunchedAt` | ISO string? |                                                             |

- An app is a **URL-only app** when `cmd` is null/empty (just opens `url`).
- An app is a **local app** when `cmd` is present (run `cmd` in `dir`, poll `url`, then open `url`).

### LaunchStatus.cs (enum)

```
Stopped | Starting | Running | Error | PortInUse
```

Status is **runtime-only** (not persisted). The GUI derives it live; the CLI reports it on demand.

---

## 3. AppShelf.Core — the engine

### 3.1 ConfigStore

- `Load()` → `AppConfig` (creates the file with empty apps list if missing; creates `%APPDATA%/AppShelf/` dir).
- `Save(AppConfig)` → atomic write (write to temp file, then move/replace) so a crash mid-write never corrupts the config.
- `AddApp(AppEntry)` → validates unique `id`, appends, saves.
- `RemoveApp(string id)` → removes, saves.
- `UpdateApp(AppEntry)` → replaces by id, saves.
- Use `System.Text.Json` with camelCase, indented output (so the file stays hand-editable).
- File-lock politely: if the file is locked, retry briefly, then surface a clear error. (CLI and GUI may both write.)

### 3.2 StackDetector + FrameworkRules

`StackDetector.Detect(string dir)` returns a `DetectionResult { cmd, url, installCmd, framework }` or null.

Detection rule table (keep it small — only what the user uses):

**Node (presence of `package.json`):**
- Read `package.json`. Pick script by priority: `dev` > `start` > `serve` > `preview`. → `cmd = "npm run <script>"`, `installCmd = "npm install"`.
- Infer framework + default port from dependencies:

| Dependency      | Framework | Default port | URL                      |
| --------------- | --------- | ------------ | ------------------------ |
| `vite`          | Vite      | 5173         | http://localhost:5173    |
| `next`          | Next.js   | 3000         | http://localhost:3000    |
| `react-scripts` | CRA       | 3000         | http://localhost:3000    |
| `astro`         | Astro     | 4321         | http://localhost:4321    |
| `@sveltejs/kit` | SvelteKit | 5173         | http://localhost:5173    |
| `@angular/core` | Angular   | 4200         | http://localhost:4200    |

**Python:**

| Signal (file contains)        | cmd                          | Default port | URL                   |
| ----------------------------- | ---------------------------- | ------------ | --------------------- |
| `streamlit`                   | `streamlit run <file>`       | 8501         | http://localhost:8501 |
| `FastAPI(`                    | `uvicorn main:app --reload`  | 8000         | http://localhost:8000 |
| `Flask(__name__)`             | `python app.py`              | 5000         | http://localhost:5000 |
| `gradio`                      | `python app.py`              | 7860         | http://localhost:7860 |

- If a `vite.config.*` / framework config specifies a port, prefer it over the default.
- If nothing matches, return null → caller prompts for manual `cmd`/`url`.
- Detection is **best-effort**. The user always confirms/edits. Do not over-engineer.

### 3.3 ProcessManager + JobObject (the one piece of real engineering)

The hard problem: `npm run dev` and friends spawn child processes. A naive `Process.Kill()` orphans the children, which keep holding the port. Solve it with a **Win32 Job Object**:

- On launch: create a job object configured with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, start the process, assign the process to the job.
- On stop: close the job handle (or `TerminateJobObject`) → the entire process tree dies, port is released.
- Keep a map `id -> (Process, JobHandle)` for running apps, in-memory in the GUI/long-lived process. (The CLI is short-lived; see §4 note on CLI stop.)

`ProcessManager` responsibilities:
- `Start(AppEntry)` → spawn `cmd` in `dir`, capture stdout/stderr (ring buffer, last ~200 lines in memory), assign to job, return a handle.
- `Stop(id)` → close job, remove from map.
- `IsPortListening(url)` → TCP connect check against host:port parsed from `url`.
- `IsPortInUse(port)` before launch → if already listening and we didn't start it, status = `PortInUse` (it may already be running, or a conflict).

**Warm vs. hard lifecycle (borrowed from app-it's model).** Distinguish two user actions so a closed window doesn't mean a killed server:
- **Close window** (the WebView2 case in v1.1, or just "hide" in v0 browser mode) → leave the dev server **running**. Reopening is instant (just re-point the browser / re-show the window) because the process never died. Status stays `Running`.
- **Stop / Quit** → the explicit kill. Close the job object, tear down the whole process tree, free the port. Status → `Stopped`.

This is a small distinction now but expensive to retrofit: the GUI must treat "close" and "stop" as separate verbs from the start, not collapse them into one. In v0 (browser launch) the warm path is trivial — the process keeps running and "Open" just relaunches the browser tab against the already-listening port (the §3.4 step-2 "already listening → skip spawn" path already handles this). Expose `Stop(id)` as the only thing that actually kills.

### 3.4 Launcher + TargetOpener

`Launcher.LaunchAsync(AppEntry)`:
1. If URL-only app → `TargetOpener.Open(entry)` immediately, done.
2. If port already listening → skip spawn, just open target (it's already up).
3. Else `ProcessManager.Start(entry)`, set status `Starting`.
4. Poll `IsPortListening(url)` every 250ms up to a timeout (~30s). On success → status `Running`, `TargetOpener.Open(entry)`, update `lastLaunchedAt`.
5. On timeout or process exit → status `Error`, keep the captured log tail available.

`TargetOpener.Open(entry)`:
- v0: if `open == "browser"` (or anything) → launch default browser at `url` (`Process.Start` with `UseShellExecute = true`).
- v1.1 hook: if `open == "webview"` → open embedded WebView2 window. **Stub this for v0** — a single method that's the only thing needing change later.

---

## 4. AppShelf.Cli

Single-file published `appshelf.exe`, added to PATH. Commands:

```
appshelf add                      # detect stack in CWD, confirm, append to config
appshelf add --name X --dir . --cmd "npm run dev" --url http://localhost:5173
appshelf add --url https://example.com --name "My Site"   # URL-only app
appshelf list                     # table: name | type | status | url
appshelf launch <id|name>         # launch (and open target)
appshelf stop <id|name>           # stop
appshelf rm <id|name>             # remove from config
appshelf open                     # launch the GUI app
```

- `appshelf add` with no flags: run `StackDetector.Detect(CWD)`, print what it found, ask for confirm/edit (name defaults to folder name), then `ConfigStore.AddApp`.
- Name/id resolution: accept either `id` or a case-insensitive `name` match; if ambiguous, list matches and exit.
- **CLI stop caveat:** the CLI is a short-lived process and does not hold the job handle of a GUI-launched app. For v0, `appshelf stop` works for apps the *CLI itself* launched in a still-running session; for apps launched by the GUI, stopping is the GUI's job. Document this. (A later option: a tiny background daemon owns all jobs; out of scope for the weekend.)
- Use `System.CommandLine` for parsing, or a minimal hand-rolled parser — keep it light.

---

## 5. AppShelf.App (WPF GUI)

### 5.1 Tray-first lifecycle
- App starts minimized to the **system tray** (NotifyIcon). It is meant to run in the background all day.
- Tray menu: `Open AppShelf` / `Add current app…` (opens add dialog) / `Quit`.
- Closing the main window hides to tray rather than exiting.

### 5.2 Main window — card grid
- Reads `config.json`, renders one card per app.
- Card shows: name, type badge (Local / URL), tag chips, live status dot, last-launched.
- Card actions by state:
  - Stopped: `Launch` `Edit` `⋯`
  - Running: `Open` `Restart` `Stop`
  - Error: `Logs` `Run Install` `Edit`
- Top bar: search box, `+ Add App`, filter by Favorites / Tag, sort by Recent.
- `Add App` dialog: three modes — Local folder (with detection preview) / Web URL / Manual command. Mirrors the CLI `add`.
- Edit dialog: all fields incl. `open` target (browser/webview — webview greyed out / "coming soon" in v0).
- A small expandable **Logs** panel per app showing the last ~200 captured lines, with Copy.

### 5.3 Spotlight overlay — the daily-use feature
- **Global hotkey** (default `Alt+Space`; make it configurable, and handle the case where it's already taken — fall back to `Ctrl+Alt+Space`). Register via `RegisterHotKey` Win32 API in `HotkeyService`.
- On hotkey: show a borderless, centered, always-on-top search box (like PowerToys Run / Raycast).
- Type → fuzzy-filter apps by name/tag. Arrow keys to move, Enter to launch the top/selected match, Esc to dismiss.
- If the selected app is running, Enter **opens** it; if stopped, Enter **launches** it.
- Overlay auto-dismisses on launch and on focus loss.
- This is the feature that makes the tool worth having — give it the most polish.

---

## 6. Launch target: browser now, WebView2 later

- **v0:** every launch opens the default browser at the app's `url`. Simple, ships immediately, zero WebView2 quirks.
- **v1.1:** implement the `TargetOpener` webview branch — a WPF window hosting `WebView2`, titled with the app name, app icon, remembers size/position, with reload. Per-app `open` field already chooses which path. **Do not build this in v0.** Just leave the clean seam.

**Reference when you build v1.1:** the `app-it` project (github.com/Christian-Katzmann/app-it) has a Windows sibling plugin (`plugins/app-it-windows/`, ~33% PowerShell + ~12% C#) that wraps a local web project in a native WebView2 window with its own `.ico` and `.lnk`. It is an unrun beta — never executed on real Windows hardware, so do **not** depend on it — but it is useful reference code for the WebView2 shell, icon generation, and shortcut creation rather than deriving those from scratch. Its window-close-keeps-server-warm, hard-quit-frees-port lifecycle is the same model now specified in §3.3; their implementation is worth reading for the port-freeing details.

---

## 7. The skill (`skill/appshelf-add/SKILL.md`)

The skill is a **thin wrapper over the CLI** — it must not reimplement detection.

```md
---
name: appshelf-add
description: Add a local project or URL to AppShelf, the personal app launcher. Use when the user says "add this to AppShelf", "add <project> to my launcher", or wants to register a project/folder/URL for one-click launching.
---

# Adding an app to AppShelf

AppShelf is the user's personal launcher. The `appshelf` CLI is the single
source of truth — this skill only invokes it. Never edit config.json directly.

## Steps
1. Determine the target:
   - If the user names a folder/path, cd there.
   - If they say "this" / "current project", use the current directory.
   - If they give a URL, it's a URL-only app.
2. Run the CLI:
   - Local project (let it auto-detect):  `appshelf add`
   - URL-only app:  `appshelf add --url <URL> --name "<Name>"`
   - If detection is wrong, re-run with explicit flags:
     `appshelf add --name "<Name>" --dir "<path>" --cmd "<command>" --url "<url>"`
3. Report back what was detected and added (name, cmd, url). If `appshelf add`
   reported no framework detected, ask the user for the launch command and URL,
   then re-run with explicit flags.

## Notes
- Do not write to %APPDATA%/AppShelf/config.json yourself.
- The CLI handles slug/id generation and dedupe.
```

Drop this folder into the user's skills directory. "Add from any terminal" is satisfied two ways: the CLI directly, and the skill (which calls the CLI).

---

## 8. Build order (realistic weekend)

### Saturday AM — Core + CLI (the foundation; usable on its own)
1. `AppShelf.Core`: models, `ConfigStore` (atomic read/write), `StackDetector` + rules.
2. `ProcessManager` + `JobObject` process-tree kill. **Test this explicitly:** launch a Vite/`npm run dev` app, stop it, confirm no orphan `node` and the port frees.
3. `Launcher` + `TargetOpener` (browser branch only).
4. `AppShelf.Cli`: `add` / `list` / `launch` / `stop` / `rm` / `open`. Publish single-file, add to PATH.
- **End state:** the user can manage every project from the terminal. Already useful.

### Saturday PM — WPF main window
5. Tray icon + lifecycle (start hidden, close-to-tray).
6. Card grid bound to config, live status via port poll.
7. Launch / Stop / Open / Restart buttons; Add + Edit dialogs; per-app logs panel.
- **End state:** a clickable launcher.

### Sunday AM — Spotlight overlay (daily driver)
8. `HotkeyService` global hotkey registration with fallback.
9. Borderless search overlay, fuzzy filter, keyboard-driven launch/open.
- **End state:** the tool the user will actually reach for.

### Sunday PM — Skill + polish + seed
10. `SKILL.md`, install to skills dir, test "add this project" end-to-end through the CLI.
11. Favorites, tags, sort-by-recent, app icons.
12. Seed config with your real projects.
13. README with build/run/PATH-setup instructions.
- **End state:** MVP that feels like a daily tool.

---

## 9. Testing checklist (what "done" means)

- [ ] `appshelf add` inside a Vite project detects `npm run dev` + port 5173.
- [ ] `appshelf add --url https://example.com --name MySite` creates a URL-only app.
- [ ] Launch a local app → port poll → browser opens at the URL → status `Running`.
- [ ] **Stop a running `npm run dev` app → no orphaned node process, port released.** (The make-or-break test.)
- [ ] Close/hide an app's window → dev server stays running; reopening is instant (no re-spawn). Only Stop/Quit kills the process and frees the port.
- [ ] Relaunch when already running → opens target without double-spawning.
- [ ] Port already in use by a non-AppShelf process → status `PortInUse`, no crash.
- [ ] Global hotkey opens overlay; typing filters; Enter launches; Esc dismisses.
- [ ] Hotkey conflict falls back gracefully.
- [ ] Config survives an app crash mid-write (atomic write verified).
- [ ] Hand-editing config.json and reloading reflects changes.
- [ ] Skill: "add this project" from a folder results in a correct config entry via the CLI.

---

## 10. Conventions & guardrails

- Core has **no UI and no Console dependencies** — pure library, unit-testable.
- All file paths normalized; tolerate both `/` and `\` in config.
- Never throw raw exceptions to the user; surface readable messages (bad dir, missing cmd, port conflict, locked config).
- Config writes are atomic (temp + replace).
- Keep detection rules in one table (`FrameworkRules`) so adding a stack is a one-line change.
- The `open` target is the only seam between v0 (browser) and v1.1 (webview) — keep it isolated in `TargetOpener`.

---

## 11. Explicitly NOT in v0 (do not build these)

- Embedded WebView2 windows (v1.1 — seam is left in `TargetOpener`).
- GitHub import / clone-and-run.
- AI "fix launch" troubleshooting.
- SQLite or any database.
- Cloud sync, accounts, team sharing, marketplace/app store.
- Docker orchestration.
- Installer/packaging generation for other apps.
- A background daemon owning all process jobs (revisit only if CLI-stop-of-GUI-apps becomes annoying).
- General-purpose stack detection beyond the rule table.

Each of these is a v1.x decision to make *after* daily use proves the core is worth extending.

---

## 12. v1.x roadmap (post-weekend, for context only)

- **v1.1** — WebView2 embedded windows (per-app `open: webview`); window size/position memory.
- **v1.2** — GitHub import: paste repo URL → clone → detect → install → card → launch.
- **v1.3** — AI fix-launch: on Error, bundle log tail + package.json/requirements + command and ask a model what to try.
- **v1.4** — shareable app "recipes" (export/import a single app's config).

---

*Build the foundation (Core + CLI) first and live in it for a day before polishing. The terminal workflow alone validates whether this saves real friction; everything above the CLI is convenience on top of a proven base.*
