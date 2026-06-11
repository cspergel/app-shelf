# Binding dev servers to IPv4 (`127.0.0.1`)

A short how-to for keeping your local projects on a single, predictable loopback
address. Written after a "backend shows grayed-out while live" bug, whose root
cause was an IPv4/IPv6 mismatch.

---

## Why this matters

`localhost` is **not** a single address. On Windows it resolves to **two**:

- `::1` — IPv6 loopback
- `127.0.0.1` — IPv4 loopback

...and Windows hands back `::1` **first**.

Different dev servers pick different families by default:

| Server | Default bind | Family |
|---|---|---|
| Vite (`npm run dev`, host unset) | `[::1]:5173` | IPv6 only |
| uvicorn `--host 127.0.0.1` | `127.0.0.1:8000` | IPv4 only |
| uvicorn `--host 0.0.0.0` | `0.0.0.0:8000` | IPv4 (all) |
| Flask / FastAPI dev default | `127.0.0.1` | IPv4 only |

When your frontend is on `::1` and your backend is on `127.0.0.1`, they live on
**different networks that happen to share the name "localhost."** That causes:

- Tools/scripts/teammates hitting `http://127.0.0.1:5273` get **connection refused**
  even though the app is "up" (it's only on `::1`).
- Frontend→backend proxying and CORS rules keyed to an address can silently fail.
- Health checks that probe one family report the other as down.

> **AppShelf itself now tolerates either family** — its status check probes both. This
> guide is about making *your projects* consistent so nothing else trips on the mismatch.

**Goal:** put everything on IPv4 `127.0.0.1`. It matches what most tooling assumes and
matches the typical backend default.

---

## Vite (frontend)

Set `server.host` in `vite.config.{js,ts,mjs}`:

```js
import { defineConfig } from 'vite'

export default defineConfig({
  server: {
    host: '127.0.0.1',   // force IPv4 loopback (default would bind ::1)
    port: 5273,          // optional: pin the port so it never drifts
    strictPort: true,    // optional: fail loudly instead of hopping to 5274 if taken
  },
})
```

- `host: '127.0.0.1'` → binds IPv4 only. Use `host: true` instead to bind **all**
  interfaces (IPv4 `0.0.0.0`) if you want to reach it from your phone/another machine.
- `strictPort: true` is recommended for AppShelf: if the port is busy, you want an error,
  not Vite silently moving to a different port that no longer matches your registered URL.

Verify after restarting `npm run dev`:

```powershell
netstat -ano | Select-String "LISTENING" | Select-String ":5273"
# Want to see:  TCP  127.0.0.1:5273  ...   (NOT  [::1]:5273)
```

---

## uvicorn / FastAPI (backend)

Already IPv4 if you pass `--host 127.0.0.1` (your uvicorn backend command). To also reach it
from other devices use `--host 0.0.0.0`:

```powershell
venv\Scripts\python.exe -m uvicorn app.main:app --port 8000 --host 127.0.0.1
```

If you start uvicorn from Python instead of the CLI:

```python
uvicorn.run("app.main:app", host="127.0.0.1", port=8000)
```

---

## Flask

```python
app.run(host="127.0.0.1", port=5000)   # IPv4 loopback
```

---

## Next.js / Create React App

Both default to IPv4 already, but you can be explicit:

```jsonc
// package.json
"scripts": {
  "dev": "next dev -H 127.0.0.1 -p 3000",
  // CRA:
  "start": "set HOST=127.0.0.1&& react-scripts start"
}
```

---

## Quick verification checklist

After changing a server's bind, restart it and confirm:

```powershell
# 1. What family is it actually on?  (want 127.0.0.1, not [::1])
netstat -ano | Select-String "LISTENING" | Select-String ":<PORT>"

# 2. Can you reach it over IPv4?
Test-NetConnection 127.0.0.1 -Port <PORT> -InformationLevel Quiet   # want: True
```

If both your frontend and backend show `127.0.0.1:<port>` in step 1, you're consistent
and the whole class of "works in the browser but X can't reach it" problems is gone.

---

## TL;DR

- `localhost` = `::1` **and** `127.0.0.1`; Windows tries IPv6 first.
- Mixed families across your front/back end is a latent papercut.
- Pin every dev server to `127.0.0.1` (Vite: `server.host: '127.0.0.1'`).
- AppShelf detects either family regardless — this is about keeping *your* stack uniform.
