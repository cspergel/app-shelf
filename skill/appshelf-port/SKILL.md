---
name: appshelf-port
description: View or assign a reserved local port for a project in AppShelf, the personal app launcher. Use when the user says "what port is <project> on", "assign a port to <project>", "give this a fixed port", or wants to avoid port clashes between local dev servers.
---

# Managing ports in AppShelf

AppShelf keeps a port registry so the user's local dev servers never collide and bind to
loopback (127.0.0.1) only. The `appshelf` CLI is the single source of truth — this skill
only invokes it. Never edit config.json directly.

## Steps
1. Identify the app by id or name (run `appshelf list` if unsure).
2. Run the CLI:
   - Show the current reservation:        `appshelf port <id|name>`
   - List all reservations + conflicts:    `appshelf ports`
   - Pin a specific port (fixed):          `appshelf port <id|name> --set <port>`
   - Auto-assign the next free port:        `appshelf port <id|name> --auto`
3. Report back the assigned port and the app's updated URL. If `appshelf ports` shows a
   CONFLICT, offer to reassign one of the apps with `--auto`.

## Notes
- Do not write to %APPDATA%/AppShelf/config.json yourself.
- A "fixed" port is pinned by the user and the allocator will never move it.
- Reserved ports are applied at launch: AppShelf passes the port to the dev server and binds
  it to 127.0.0.1, so apps stay clash-free and off the LAN.
