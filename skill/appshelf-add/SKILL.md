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
3. Report back what was detected and added (name, cmd, url, reserved port). If
   `appshelf add` reported no framework detected, ask the user for the launch command
   and URL, then re-run with explicit flags.

## Notes
- Do not write to %APPDATA%/AppShelf/config.json yourself.
- The CLI handles slug/id generation, dedupe, and clash-free port reservation.
- On add, AppShelf reserves a port for the project (keeping its framework default if free,
  otherwise the next free port) so projects don't collide. To view or change it, use the
  `appshelf-port` skill or `appshelf port <id> --set <n>`.
