# Security & Trust Model

AppShelf is a **single-user, local developer tool**. It launches the dev servers *you* register,
on *your* machine, under *your* account. Understanding its trust model matters more than a long
list of mitigations.

## `config.json` is a trusted artifact — treat it like a shell script

Each registered app stores a launch command (e.g. `npm run dev`) that AppShelf runs via
`cmd.exe /c <command>` in the project directory. That means **anything in your config can run
arbitrary commands**, exactly as if you typed them into a terminal. This is by design — dev
commands legitimately need shell features.

Consequences:

- **Don't import a `config.json` you didn't write.** Sharing your config is equivalent to sharing a
  `.bat` file — review it first.
- Launch commands are **not** sandboxed or sanitized. They run with your privileges.
- The `APPSHELF_CONFIG` environment variable can redirect which config file is loaded (used for
  test isolation). Anything that can set your environment can point AppShelf at a different config —
  the same trust boundary as any local process.

## What AppShelf does *not* do

- It does **not** require or request administrator elevation. It runs as a standard user.
- It only opens **`http://` / `https://`** URLs in the browser. Other URI schemes (`file:`,
  `ms-settings:`, custom protocols) are rejected, so a stored URL can't be used to launch an app.
- The **Port Doctor never auto-kills** processes it can't attribute ("Unknown" tier). Bulk
  "kill orphans" only touches processes it classifies as likely-orphaned. Killing any specific port
  is always an explicit, confirmed action.
- It manages **loopback** dev ports only; it is not a network service and opens no listening sockets
  of its own.

## Process control

Launched servers are assigned to a Win32 **Job Object** with `KILL_ON_JOB_CLOSE`, so stopping an
app (or quitting AppShelf) tears down the entire process tree and frees the port — even on a crash,
because the OS closes the job handle on process exit.

## Reporting an issue

This is a personal project shared as-is. If you find a security problem, please open an issue
describing it (avoid posting working exploit detail against third parties). There is no formal SLA.
