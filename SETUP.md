# VoxReel — Setup Guide

Get the app running (with working **registration and login**) on a fresh machine.

> **Why this file exists:** accounts live in a PostgreSQL database, not in the code. A fresh
> clone has no database connection configured, so registration/login fail until you do the
> one step below. The working password is stored in *user-secrets* (deliberately never
> committed), so cloning alone is not enough — you set your own.

---

## Prerequisites

| Tool | Why | Check |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Runs the server | `dotnet --version` |
| [Node.js 20+](https://nodejs.org) | Builds/serves the React client | `node --version` |
| [PostgreSQL](https://www.postgresql.org/download/) | Stores accounts & generations | service running on port 5432 |

No Pollo or OpenAI key is required — image generation works **free** out of the box via the
Pollinations provider.

---

## Fastest path (Windows) — one script

From the repo root:

```powershell
./setup.ps1 -PostgresPassword "YOUR_POSTGRES_PASSWORD"
```

It will:
1. Verify PostgreSQL is reachable and the password works.
2. Store your connection string in **user-secrets** (never committed).
3. Create the `voxreel` database and schema (the app also does this on first run).
4. Print the command to start the app.

Then start it:

```powershell
dotnet run --project AIVIDEO.Server --launch-profile https
```

Open <https://localhost:7244>, click **Get started**, and register. Done.

---

## Manual path (any OS)

**1. Configure the database connection** (replace the password with your PostgreSQL one):

```bash
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=voxreel;Username=postgres;Password=YOUR_PASSWORD" \
  --project AIVIDEO.Server
```

**2. Run.** The app creates the `voxreel` database and all tables automatically on startup:

```bash
dotnet run --project AIVIDEO.Server --launch-profile https
```

**3.** Open <https://localhost:7244> → register → log in.

> You do **not** need to run `dotnet ef database update` manually — the app migrates on
> startup. Running it is still fine if you prefer.

---

## Why can't I log in with someone else's account?

You can't, and that's intentional. Accounts exist only in *your* database. When you clone the
repo you get the code, not anyone's data — so you register your own account and it is unique
to your machine. Two people running their own copies have completely separate user lists.

---

## Troubleshooting registration/login

The Studio and the API's `/api/system/status` both tell you what's wrong. Common causes:

| Symptom | Cause | Fix |
|---|---|---|
| "Database unavailable" / can't register | Wrong PostgreSQL password | Re-run step 1 with the correct password |
| "Cannot reach the API" | Server not running | `dotnet run --project AIVIDEO.Server --launch-profile https` |
| Login worked, then "session expired" after a restart | Only happens if no stable `Jwt:Key` is set | In dev a key is generated automatically; to persist across restarts set one: `dotnet user-secrets set "Jwt:Key" "<32+ char string>" --project AIVIDEO.Server` |
| PostgreSQL not installed | — | Install it, ensure the service is running on 5432 |

Check server health any time:

```bash
curl -k https://localhost:7244/api/system/status
```

`databaseReachable: true` means registration/login will work.

---

## Optional: paid, higher-quality models

Image generation is free by default (Flux via Pollinations). For ChatGPT/Gemini-tier quality
you can add a **Pollo** API key (its `nano-banana-pro` model *is* Google's Gemini image model).
Pollo's API needs paid credits.

```bash
dotnet user-secrets set "Pollo:ApiKey" "<your-key>" --project AIVIDEO.Server
```

Then choose **Pollo** in the Image Studio's Provider dropdown. Without credits, Pollo returns
403 and you should stay on **Free**.

---

## What's stored where

| Thing | Location | In git? |
|---|---|---|
| Code | this repo | ✅ |
| Accounts, generations | PostgreSQL `voxreel` database | ❌ (your machine only) |
| Generated media files | `.media/` folder | ❌ (gitignored) |
| DB password, JWT key, Pollo key | user-secrets (`%APPDATA%\Microsoft\UserSecrets`) | ❌ (never committed) |
