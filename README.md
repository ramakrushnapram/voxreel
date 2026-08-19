# VoxReel Studio

AI video studio built on the [Pollo AI](https://docs.pollo.ai/) platform API: generate images, animate stills, and produce short clips — the foundation for long-form narrated video with voiceover and YouTube publishing.

**Stack:** React 19 + Vite 8 · ASP.NET Core 10 · EF Core (code-first) · PostgreSQL

See [PROJECT-PLAN.md](PROJECT-PLAN.md) for the full architecture, the verified API contracts, and the 12-phase build order.

---

## What works today (Phase 1)

| Module | What it does |
|---|---|
| **Image Studio** | Generate a still from a prompt, or upload an image and describe how it should change |
| **Animate** | Turn a still into a 4–15s clip |
| **Quick Clip** | Prompt straight to video |
| **Gallery** | Live status polling, inline playback |

Still to build: script generation, scene planning, TTS narration, FFmpeg assembly, voice swapping, YouTube publishing, MCP server. See the plan.

---

## The constraint that shapes everything

Pollo caps a single clip at **15 seconds** (`length: 4|5|6|7|8|9|10|11|12|15`). A 20-minute video is ~150 clips.

Long runtimes are therefore produced by **assembly**, not by asking for a longer clip: script → scenes → per-scene narration + visuals → FFmpeg concat. Narration audio is the timeline backbone; visuals are cut to fit it.

Two consequences worth knowing before you build on this:

- **Generated media expires after 14 days.** Every asset is downloaded to local storage on completion, and the local copy is the system of record. Never persist a Pollo URL.
- **Pollo has no voice API.** No TTS, no voice changer, no lip sync. Voice work needs a second provider (ElevenLabs). See the capability matrix in the plan.

---

## Running it

**Prerequisites:** .NET 10 SDK · Node.js 20+ · PostgreSQL · a [Pollo API key](https://api.pollo.ai/api-keys)

```bash
# 1. Configure secrets (never committed)
dotnet user-secrets set "Pollo:ApiKey" "<your-key>" --project AIVIDEO.Server
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=voxreel;Username=postgres;Password=<your-password>" \
  --project AIVIDEO.Server

# 2. Create the schema
dotnet ef database update --project AIVIDEO.Server

# 3. Run (the SPA dev server starts automatically)
dotnet run --project AIVIDEO.Server --launch-profile https
```

Then open <https://localhost:7244>. The UI reports a missing API key or unreachable database up front rather than failing on your first generation.

### Animating an uploaded file needs one more step

Pollo fetches source images **from its own servers**, so a file on your machine is unreachable to it. To animate an upload, expose this server publicly and set `Storage:PublicBaseUrl`:

```bash
dotnet user-secrets set "Storage:PublicBaseUrl" "https://<your-tunnel>.example" --project AIVIDEO.Server
```

Without it, uploads still work and preview locally, but generation is rejected with an explanation. Pasting an already-public image URL works with no setup.

---

## Configuration

| Key | Purpose |
|---|---|
| `Pollo:ApiKey` | Sent as `x-api-key`. **Secrets only.** |
| `Pollo:WebhookUrl` | Completion callback. Leave empty locally — polling handles it. |
| `Pollo:MaxConcurrentTasks` | Throttle on in-flight generations |
| `Pollo:Models:*` | Model routing by role (Hero, Broll, ImageToVideo, CharacterLock, Still) |
| `Storage:Root` | Media directory (default `.media`, gitignored) |
| `Storage:PublicBaseUrl` | Required to animate uploaded files |
| `ConnectionStrings:Default` | PostgreSQL. **Secrets only.** |

Model defaults point at the two endpoints whose schemas are verified against the live docs. Switching a role to another model means checking that model's schema first — field names and casing differ (`kling-v3-omni` uses `720P`, `pollo-v2-5` uses `720p`).

---

## API

```
POST /api/generations/text-to-video     prompt → clip
POST /api/generations/image-to-video    image → clip
POST /api/generations/image             prompt → still, or image + prompt → edited still
GET  /api/generations/{id}              status + assets
GET  /api/generations?take=50           recent
POST /api/assets/upload                 multipart image upload
GET  /api/assets/{id}/raw               stream stored asset (range requests supported)
GET  /api/system/status                 configuration diagnostics
```

---

## Notes

- Storage is **local disk only** (`.media/yyyy/MM/dd/`). Uploaded filenames never reach the filesystem — files are stored under generated GUIDs with a whitelisted extension.
- `Microsoft.OpenApi` is pinned to 2.7.5 to clear [GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc). Do not "upgrade" to 3.x — it breaks the ASP.NET Core 10 source generator.
- Migrations auto-apply in Development only. Elsewhere they are a deploy step.
