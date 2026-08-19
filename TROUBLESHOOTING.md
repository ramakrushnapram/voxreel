# VoxReel — Troubleshooting

Fixes for the errors people hit most, especially right after cloning.

---

## "502 Bad Gateway" when registering or logging in

**A 502 means the frontend reached the dev proxy, but the .NET backend behind it was not
running or not reachable.** It is never produced by the app's own code — it comes from the
Vite dev proxy when the backend is down. Common causes:

### 1. The backend isn't running (most common)
The client (Vite) proxies every `/api` call to the backend. If you started only the client,
`/api/auth/register` has nothing to talk to → 502.

**Fix — run the backend, which auto-starts the client:**
```bash
dotnet run --project AIVIDEO.Server --launch-profile https
```
Then open <https://localhost:7244>. Do **not** run `npm run dev` on its own and browse the
Vite port expecting the API to work.

### 2. You tried during the first few seconds of startup
On first run the backend builds, connects to PostgreSQL, and applies migrations — that takes
10–20 seconds. Requests made before it finishes get a 502. **Wait for the log line
`Now listening on: https://localhost:7244`, then register.**

### 3. The backend crashed on startup
Older builds threw on startup when `Jwt:Key` was unset outside Development — which a fresh
clone always is, since the key lives in user-secrets and is never committed. That crash
showed up as a 502 on every request.

**This is now fixed:** when no `Jwt:Key` is configured, the app generates one and stores it in
a gitignored `.jwtkey` file, so it boots with zero configuration. If you are on an older
checkout, pull the latest.

To confirm the backend is actually up:
```bash
curl -k https://localhost:7244/api/system/status
```
A JSON response means it's running. Connection refused means it isn't — check the terminal
where you ran `dotnet run` for errors.

---

## "Database unavailable" / registration fails with a database error

The backend is running but can't reach PostgreSQL. Almost always a wrong password in the
connection string (the working one lives in user-secrets, not the repo).

**Fix:**
```bash
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=voxreel;Username=postgres;Password=YOUR_PASSWORD" \
  --project AIVIDEO.Server
```
Or run `./setup.ps1 -PostgresPassword "YOUR_PASSWORD"`. See [SETUP.md](SETUP.md).

The app creates the `voxreel` database and tables itself on startup — you don't run
migrations manually.

---

## "Your session has expired" right after logging in

Happens only if the JWT signing key changed between issuing your token and validating it
(e.g. an older build regenerated a random key every restart). The current build persists the
key (`.jwtkey` or user-secrets), so this no longer recurs. If you see it once after updating,
just sign in again — it will stick.

---

## Login says "Incorrect email or password"

The email/password pair doesn't match an account **in this database**. Accounts do not
transfer between clones or machines — each install has its own user list. Register a new
account, or reset a password directly:

```bash
# with psql, against your voxreel database
UPDATE "Users" SET ... ;   # or simplest: register a fresh account
```

---

## Image generation fails with "Pollo returned 403"

Your Pollo API account has no credits. The **Free** provider (the default in Image Studio)
needs no key and no credits — use it. Pollo (paid) only works once its account is topped up.
See the provider dropdown in Image Studio.

---

## LLM features say "Ollama isn't running"

Prompt enhancement, script writing, and RAG use a local LLM. Install and start it:
```bash
# from ollama.com, then:
ollama pull llama3.2
ollama pull nomic-embed-text
```
The app auto-detects Ollama at `http://localhost:11434` — no restart needed. See
[OLLAMA.md](OLLAMA.md).

---

## Quick health check

```bash
curl -k https://localhost:7244/api/system/status
```
```jsonc
{
  "databaseReachable": true,   // false → fix the connection string
  "polloConfigured": false,    // false is fine; Free images still work
  "ollamaAvailable": false,    // false → install Ollama for LLM features
  ...
}
```
If `databaseReachable` is `true`, registration and login will work.
