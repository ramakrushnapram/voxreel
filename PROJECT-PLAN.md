# AI Long-Form Video Studio — Full Project Plan

A VidRush-style production system: **image generation → animation → long-form video from a script → hero/movie-trailer voiceover → voice swapping on uploaded video → YouTube publishing**, drivable from an **MCP server**.

- **Client:** React 19 + Vite 8 (`aivideo.client/`)
- **Server:** ASP.NET Core / .NET 10 (`AIVIDEO.Server/`)
- **AI video + images:** [Pollo AI Platform API](https://docs.pollo.ai/)
- **Voice:** ElevenLabs (Pollo has no voice API — see §3)
- **Reference product:** [Pollo VidRush](https://pollo.ai/m/vidrush)

> Everything below was verified against the live Pollo and ElevenLabs docs on 2026-08-19. Where a capability does **not** exist, this doc says so explicitly rather than inventing an endpoint. Anything marked ⚠️ needs confirmation before you build on it.

---

## 1. Project name

Current working name `AIVIDEO` is a placeholder — it is generic, unbrandable, and untrademarkable. Decide now, because renaming .NET namespaces, the solution file, and the MCP server identifier later is tedious.

### Recommendation: **VoxReel**

The product has exactly two pillars — **voice** (`vox`) and **video** (`reel`). VoxReel says both in seven letters, is easy to say and spell, works as a verb ("VoxReel it"), and leaves room to expand beyond YouTube. Namespace `VoxReel.Server`, MCP id `voxreel`, CLI `voxreel`.

### Alternates

| Name | Why it works | Watch out for |
|---|---|---|
| **VoxReel** ⭐ | Voice + video in one word; short; brandable | Check `.com` / `.ai` availability |
| **Reelwright** | "-wright" = maker (playwright, shipwright). Fits scripted long-form craft | Slightly longer; people may type "Reelwrite" |
| **Auteur** | Cinematic, means "film author" — you write, direct, and voice. Elegant | Non-obvious spelling for non-French speakers |
| **Narrata** | Narration-first, which is literally the architecture (§6) | Softer, less "video" |
| **Filmforge** | Strong, industrial, obvious what it does | "Forge" is heavily used in AI tooling |
| **Cinemind** | Cinematic + AI | Slightly generic |

**Avoid:** anything starting `Vid-` or `AI-` (VidGen, AIVideo, VideoAI). That space is saturated, SEO is hopeless, and trademark clearance will fail.

**Before committing:** check domain availability, a USPTO/EUIPO trademark search, npm name (for the MCP package), and the GitHub org. Do this in one sitting — the name should survive all four.

> Naming is a judgment call and it is yours to make. The rest of this document uses `AIVIDEO` so nothing is blocked while you decide.

---

## 2. What the product does — module map

| # | Module | Input | Output | Engine |
|---|---|---|---|---|
| M1 | **Image Studio** | prompt, or image to edit | still images, 1K–4K | Pollo image models |
| M2 | **Animate** | one image | 4–15 s animated clip | Pollo image-to-video |
| M3 | **Quick Clip** | prompt | 4–15 s clip | Pollo text-to-video |
| M4 | **Character Lock** | 1–3 reference images of a subject | consistent character across scenes | Pollo `ref2video` |
| M5 | **Long-Form Studio** ⭐ | topic / transcript / YouTube URL | **10–30 min narrated video** | full pipeline (§6) |
| M6 | **Voice Studio** | text + voice choice | hero / trailer / animated narration | ElevenLabs TTS |
| M7 | **Voice Swap** | **uploaded video** | same video, different voice | ElevenLabs speech-to-speech |
| M8 | **Publish** | finished render | YouTube video + thumbnail + captions | YouTube Data API v3 |
| M9 | **MCP Server** | agent tool calls | drives M1–M8 | stdio + HTTP |

M5 is the product. M1–M4 are its building blocks and also ship as standalone tools. M6–M7 are the voice layer you asked for.

---

## 3. Capability matrix — what Pollo does and does NOT do

This is the most important table in the document. Several things you asked for are **not** Pollo features, and building as if they were would waste weeks.

| Capability you asked for | Pollo? | Reality |
|---|:---:|---|
| Text → video | ✅ | `POST /generation/{brand}/{model}`, 4–15 s per clip |
| Image → video (animate) | ✅ | Same endpoint, `input.image` |
| Image generation | ✅ | Nano Banana Pro, GPT Image 2, PolloJourney, Kling image |
| Image **editing** | ✅ | Nano Banana Pro `imageUrl` / `images[]` |
| Character consistency | ✅ | Kling 3.0 Omni `ref2video`, `refs[{type:"subject"}]` |
| Ambient audio on a clip | ✅ | `generateAudio: true` — sound design, **not narration** |
| **Text-to-speech / narration** | ❌ | **No TTS endpoint.** Use ElevenLabs |
| **Voice changing (hero/movie voice)** | ❌ | **No voice API.** Use ElevenLabs speech-to-speech |
| **Upload a video and transform it** | ⚠️ | No video-to-video endpoint. `ref2video` accepts a `video` **reference**, but it generates a *new* clip — it does not edit yours |
| **Lip sync** | ❌ | Not in the schema. Third party needed (§9) |
| Video upscaling / extending | ⚠️ | Marketing copy mentions it; **not in the OpenAPI paths.** Verify before promising |
| Long video (10–30 min) in one call | ❌ | **Hard cap 15 s.** This is why §6 exists |

**Consequence:** this is a **two-provider system**. Pollo owns pixels. ElevenLabs owns voice. FFmpeg owns assembly. Any plan that assumes Pollo does voice will fail at integration time.

---

## 4. The core constraint — why long-form needs a pipeline

From the verified Pollo 2.5 schema:

```
length:     4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 15   (seconds — hard cap)
resolution: 720p | 1080p
aspectRatio:16:9 | 9:16
mode:       basic | pro
```

A 20-minute video is 1200 seconds ≈ **150 clips at 8 s each**. There is no "generate 20 minutes" endpoint anywhere in the API.

### Duration math

| Quantity | Value |
|---|---|
| Target runtime | 1200 s (20 min) |
| Narration rate | ~150 words/min |
| Script length | ~3,000 words |
| Scenes @ ~8 s | ~150 |
| All-AI-video | 150 video generations — slow and expensive |
| **Hybrid (default)** | ~30 AI video clips + ~120 AI stills with Ken Burns pans |

**Hybrid is the default strategy.** Image generation is far cheaper and faster than video generation. Pan/zoom a still for 8 seconds under narration and most viewers cannot distinguish it from generated footage. Spend real video generations on the hook, chapter openers, and money shots.

> Get live per-generation pricing from <https://docs.pollo.ai/pricing>. Do not quote cost figures from this document.

### Generated media expires in 14 days

Pollo stores output URLs for **14 days only**. The server must **download every asset to local/blob storage the moment a task completes**. Never store a Pollo URL as the system of record.

---

## 5. Verified API contracts

### 5.1 Pollo — auth

Header `x-api-key: <key>` on every request. Keys from <https://api.pollo.ai/api-keys>.
Base: `https://pollo.ai/api/platform`

### 5.2 Video generation (text-to-video **and** image-to-video, one endpoint)

```http
POST https://pollo.ai/api/platform/generation/pollo/pollo-v2-5
x-api-key: <key>
Content-Type: application/json
```

```jsonc
{
  "input": {
    "prompt": "string (1–2000 chars)",   // text-to-video
    "image":  "https://.../frame.png",   // OR image-to-video (JPG/PNG/JPEG, public URL)
    "length": 8,
    "resolution": "1080p",
    "aspectRatio": "16:9",               // ignored for image-to-video
    "mode": "pro",
    "generateAudio": false               // keep false — we own the audio track
  },
  "webhookUrl": "https://your-host/api/webhooks/pollo",
  "clientSource": "aivideo"
}
```

```json
{ "taskId": "string", "status": "waiting" }
```

Defaults: `length=4`, `resolution=720p`, `mode=basic`, `generateAudio=false`, `aspectRatio=16:9`.

### 5.3 Image generation / editing — Nano Banana Pro

```http
POST https://pollo.ai/api/platform/generation/google/nano-banana-pro/image
```

```jsonc
{
  "input": {
    "prompt": "string (max 10000 chars)",
    "aspectRatio": "1:1|9:16|16:9|4:3|3:4|3:2|2:3|5:4|4:5|21:9",
    "resolution": "1K|2K|4K",            // default 1K
    "imageUrl": "https://...",           // optional — single-image edit
    "images": ["https://...", "..."]     // optional — multi-image edit (min 1)
  },
  "webhookUrl": "...",
  "clientSource": "aivideo"
}
```

Text-only mode needs `prompt` + `aspectRatio`. Edit mode needs `aspectRatio` + (`imageUrl` or `images`); inputs must be JPG/PNG/JPEG with aspect ratio between 1:4 and 4:1.

**This endpoint powers M1 and the ~120 Ken Burns stills per long-form video.**

### 5.4 Character consistency — Kling 3.0 Omni `ref2video`

```http
POST https://pollo.ai/api/platform/generation/kling-ai/kling-v3-omni/ref2video
```

```jsonc
{
  "input": {
    "prompt": "string (1–2500 chars)",
    "refs": [                            // 1–7 items
      { "type": "image"   /* ... */ },
      { "type": "subject" /* 1–3 images — character consistency */ },
      { "type": "video"   /* video as a style/motion reference */ },
      { "type": "audio"   /* audio reference */ }
    ],
    "duration": 5,                       // 3–15
    "aspectRatio": "16:9|9:16|1:1",
    "resolution": "720P|1080P|4K",
    "generateAudio": true,
    "videoNum": 1,                       // 1–4
    "imageMeta": []                      // optional cropping
  }
}
```

**This is how you keep the same host/character across 150 scenes.** Note `resolution` here is uppercase `720P` (not `720p` as in 5.2) — the casing is inconsistent across models, so never share a resolution constant between model clients.

### 5.5 Task status (all models)

```http
GET https://pollo.ai/api/platform/generation/{taskId}/status
x-api-key: <key>
```

```jsonc
{
  "taskId": "string",
  "input": {},
  "credit": 0,
  "costUsd": 0,
  "generations": [{
    "id": "string",
    "status": "waiting|processing|succeed|failed",
    "failMsg": null,
    "url": "https://.../out.mp4",     // ← the asset. Expires in 14 days.
    "cover": "https://.../thumb.jpg",
    "mediaType": "video|image|text|audio"
  }]
}
```

**Prefer webhooks** (`webhookUrl`) over polling; keep polling as reconciliation for missed callbacks. <https://docs.pollo.ai/webhooks>

### 5.6 Model routing table

Endpoints follow `POST /generation/{brand}/{model}`. Configure per tier, never hardcode.

| Tier | Model | Path |
|---|---|---|
| Hero / hook | Veo 3.1, Kling 3.0 | `/google/veo3-1`, `/kling-ai/kling-v3` |
| Bulk B-roll | Pollo 2.5 basic, Wan 2.2 Flash | `/pollo/pollo-v2-5`, `/wanx/wan-v2-2-flash` |
| Image → video | Kling 2.5 Turbo, Hailuo 2.3 | `/kling-ai/kling-v2-5-turbo`, `/hailuo/hailuo-2-3` |
| Character lock | Kling 3.0 Omni | `/kling-ai/kling-v3-omni/ref2video` |
| Stills | Nano Banana Pro, GPT Image 2 | `/google/nano-banana-pro/image`, `/gpt-image/gpt-image-2-0` |
| Talking avatar ⚠️ | Hailuo Live2D | `/hailuo/video-01-live2d` — verify lip-sync quality |

Full index: <https://docs.pollo.ai/llms.txt> · OpenAPI: <https://docs.pollo.ai/openapi.json>

---

## 6. Long-form pipeline (M5) — the heart of the product

**The narration audio is the timeline backbone.** Visuals are cut to fit narration, never the reverse.

```
┌─────────────────────── React 19 SPA (Vite 8) ────────────────────────┐
│ Image Studio │ Long-Form Studio │ Timeline │ Voice Studio │ Publish  │
└───────────────────────────────┬──────────────────────────────────────┘
                    REST + SignalR (live progress)
┌───────────────────────────────▼──────────────────────────────────────┐
│                 ASP.NET Core 10 — AIVIDEO.Server                     │
│                                                                      │
│  Pipeline (staged background queues, each independently resumable)   │
│   1 ScriptStage     topic│transcript│YouTube URL → narration script   │
│   2 SceneStage      script → timed scenes + visual prompts            │
│   3 NarrationStage  ElevenLabs TTS → wav + word timings  ◄─ TIMELINE  │
│   4 VisualStage     Pollo image/video per scene (gated fan-out)       │
│   5 AssemblyStage   FFmpeg: concat, transitions, subtitles, ducking   │
│   6 PublishStage    YouTube resumable upload + thumbnail + captions   │
│                                                                      │
│  Services  PolloClient · VoiceClient · FfmpegRunner · YouTubeClient   │
│            AssetStore · JobQueue · CostGuard                          │
└───────────────────────────────┬──────────────────────────────────────┘
      ┌───────────┬─────────────┼────────────┬──────────────┐
   Pollo AI   ElevenLabs     FFmpeg      YouTube      DB + blob store
```

### Why staged queues, not one long method

A 20-minute render is **30–90 minutes of wall clock** and hundreds of external calls. It must be:

- **Resumable** — a failure in scene 96 must not rerun scenes 1–95.
- **Idempotent** — each scene keyed by `(projectId, sceneIndex, promptHash)`; re-running a completed scene is a no-op.
- **Rate-limited** — bounded concurrency (start at 4–6 in-flight Pollo tasks), exponential backoff on 429.
- **Cost-capped** — `CostGuard` enforces a per-project USD ceiling *before* enqueuing, not after spending.
- **Observable** — per-scene status streamed to the UI over SignalR.

### Per-scene failure ladder

1. Retry same model, twice, with backoff.
2. Fall back to the cheaper tier model.
3. Fall back to a Ken Burns still.
4. Fall back to a solid-colour card with the scene's subtitle.

**One scene must never fail the whole render.** Mark it `degraded`, surface it in the UI, keep going.

### Visual consistency across 150 scenes

Without this, scene 12 and scene 113 look like different productions:

- A **style anchor** string prefixed onto every visual prompt (lighting, lens, palette, era).
- A **locked subject reference** (1–3 images) reused via `ref2video` for any scene with the host/character.
- A fixed seed per project where the model supports it.

---

## 7. Voice engine (M6) — hero, trailer, and animated voices

**Pollo cannot do this.** ElevenLabs is the recommended provider.

Auth header: `xi-api-key`. Verify current paths at <https://elevenlabs.io/docs/llms.txt>.

### 7.1 Narration — text to speech

`POST /v1/text-to-speech/{voice_id}` → narration audio per scene.

**Hard requirement: word-level timestamps.** Subtitles and scene timing both depend on them. Use the timestamps variant of the endpoint — if a provider cannot emit word boundaries, it is disqualified regardless of voice quality. ⚠️ Confirm the exact timestamped path before building.

### 7.2 Hero / movie-trailer / animated voices — Voice Design

Rather than hunting the voice library, **Voice Design** generates a voice from a text description:

> "Deep, gravelly movie-trailer narrator. Slow, dramatic, enormous low-end presence."
> "High-energy animated squirrel, fast, bright, cartoonish."
> "Calm British documentary narrator, warm, measured, Attenborough-like."

Flow: **Design a voice → get `generated_voice_id` → create the voice → use that `voice_id` for TTS and voice swap.**

⚠️ `POST /v1/text-to-voice/create-previews` is **deprecated**. Use the current *Design a voice* endpoint and the returned `generated_voice_id`. Verify in the changelog before coding.

Ship a **preset library** in the UI so users pick a vibe, not a prompt:

| Preset | Use |
|---|---|
| Movie Trailer | "In a world…" — hooks, chapter openers |
| Documentary | Long-form factual narration (default) |
| Animated Hero | Cartoon/character content |
| Villain | Dramatic contrast segments |
| News Anchor | Explainer / current affairs |
| Warm Storyteller | Bedtime, history, calm content |

### 7.3 Voice swap on an uploaded video (M7)

`POST /v1/speech-to-speech/{voice_id}` — send source audio, get it back **in a different voice with the original delivery, timing, and emotion preserved**.

Two routes, and the difference matters:

| Route | Method | Keeps timing? | Keeps accent? | Use when |
|---|---|:---:|:---:|---|
| **A — Voice Changer** ⭐ | Extract audio → speech-to-speech → remux | ✅ exactly | ✅ source accent preserved | You want the *same performance*, new voice. Lip movement stays plausible |
| **B — Re-narrate** | Extract → transcribe → TTS in new voice → remux | ❌ drifts | ❌ new accent | You want a genuinely different read, or a different language |

**Default to Route A.** It is one API call, timing-safe, and no transcription step to get wrong.

> **Documented behaviour, not a bug:** speech-to-speech **preserves the source accent**. A speaker with a strong accent voiced through a "deep American trailer voice" keeps the original accent. Surface this in the UI or you will get bug reports that are not bugs.

**Pipeline for M7:**

```bash
# 1. Extract audio
ffmpeg -i uploaded.mp4 -vn -acodec pcm_s16le -ar 44100 -ac 1 source.wav
# 2. POST source.wav → /v1/speech-to-speech/{voice_id} → converted.mp3
# 3. Remux over the original video, keeping video untouched
ffmpeg -i uploaded.mp4 -i converted.mp3 -map 0:v -map 1:a \
       -c:v copy -c:a aac -b:a 192k -shortest voiceswapped.mp4
```

**Lip sync is not solved by this.** Route A keeps timing so lips stay close, but they will not match perfectly. True lip sync needs a third party (Sync.so, Hedra) or Pollo's Hailuo Live2D for avatar-style content. ⚠️ Out of scope for v1 — set expectations accordingly.

### 7.4 Uploaded-video consent gate

Voice-swapping someone's video is a real misuse vector — non-consensual voice cloning and deepfakes. Before M7 processes any upload, require an explicit attestation that the user owns the footage or has permission, log it with the job, and rate-limit the endpoint. This is a product requirement, not legal boilerplate.

---

## 8. Data model

```
Project        Id, Title, Mode(QuickClip|LongForm|VoiceSwap|ImageStudio),
               TargetSeconds, AspectRatio, Resolution, Status,
               VoiceId, StyleAnchor, SubjectRefAssetIds, MusicTrackId,
               CostCapUsd, CostSpentUsd, CreatedUtc

Script         ProjectId, SourceType(Topic|Transcript|YouTubeUrl|Upload),
               SourceText, GeneratedMarkdown, WordCount

Scene          Id, ProjectId, Index, NarrationText, VisualPrompt,
               VisualKind(AiVideo|AiStill|RefVideo|Stock|Color),
               StartMs, DurationMs, Status, Attempts, DegradedReason

Asset          Id, ProjectId, SceneId?, Kind(Video|Image|Audio|Subtitle|Thumbnail),
               LocalPath, RemoteUrl, ExpiresUtc, Bytes, Sha256

PolloTask      Id, SceneId, Model, Endpoint, RequestJson, TaskId,
               Status, CostUsd, ResultUrl, FailMsg

VoiceProfile   Id, Provider, VoiceId, DisplayName, Preset,
               DesignPrompt, IsUserCreated

VoiceSwapJob   Id, ProjectId, SourceAssetId, TargetVoiceId,
               Route(VoiceChanger|Renarrate), ConsentAttestedUtc, Status

RenderJob      Id, ProjectId, Stage, Status, Progress,
               StartedUtc, FinishedUtc, ErrorMessage

Publication    Id, ProjectId, Platform, VideoId, Url, Privacy, PublishedUtc
```

SQLite + EF Core for dev; the schema moves to Postgres unchanged.

---

## 9. Server API surface

```
POST   /api/projects                          create project
GET    /api/projects/{id}                     project + scenes + status

# M1 Image Studio
POST   /api/images/generate                   prompt → still(s)
POST   /api/images/edit                       image(s) + prompt → edited still

# M2/M3 Quick modes
POST   /api/quick-clip                        prompt OR image → single clip
POST   /api/animate                           image → animated clip

# M5 Long-form
POST   /api/projects/{id}/script/generate     topic → script (LLM)
POST   /api/projects/{id}/script/import       paste transcript / .txt .srt .vtt
POST   /api/projects/{id}/script/from-youtube reference URL → style-matched script
PUT    /api/projects/{id}/script              manual edit
POST   /api/projects/{id}/scenes/plan         script → scenes + visual prompts
PUT    /api/scenes/{id}                       edit narration or visual prompt
POST   /api/scenes/{id}/regenerate            re-render ONE scene
POST   /api/projects/{id}/render              enqueue full render
GET    /api/projects/{id}/render/status       stage + per-scene progress
POST   /api/projects/{id}/render/cancel

# M6/M7 Voice
GET    /api/voices                            library + presets
POST   /api/voices/design                     description → new voice
POST   /api/voices/preview                    sample audio for a voice
POST   /api/voice-swap                        upload video + voiceId (+consent) → job
GET    /api/voice-swap/{id}                   status + result

# Infra
POST   /api/webhooks/pollo                    Pollo completion callback
GET    /api/assets/{id}                       stream local asset
GET    /api/publish/youtube/authorize         OAuth consent redirect
GET    /api/publish/youtube/callback
POST   /api/projects/{id}/publish/youtube     resumable upload + metadata
HUB    /hubs/render                           SignalR progress stream
```

---

## 10. FFmpeg assembly

**Ken Burns from a still** (8 s slow zoom, 1080p) — this renders ~80% of long-form visuals:

```bash
ffmpeg -loop 1 -i still.png -t 8 \
  -vf "scale=3840:-2,zoompan=z='min(zoom+0.0006,1.25)':d=200:s=1920x1080:fps=25,format=yuv420p" \
  -c:v libx264 -preset medium -crf 20 scene_042.mp4
```

**Normalise every scene to identical codec/fps/resolution before concat.** Mismatched streams are the number-one cause of corrupt concatenated output.

```bash
ffmpeg -f concat -safe 0 -i scenes.txt -c copy video_track.mp4
```

**Mix narration + music with sidechain ducking** so music drops under the voice:

```bash
ffmpeg -i video_track.mp4 -i narration.wav -i music.mp3 \
  -filter_complex "[2:a]volume=0.25[m];
                   [m][1:a]sidechaincompress=threshold=0.03:ratio=6:attack=20:release=400[duck];
                   [1:a][duck]amix=inputs=2:duration=first[a]" \
  -map 0:v -map "[a]" -c:v copy -c:a aac -b:a 192k final.mp4
```

**Subtitles** from the TTS word timings:

```bash
ffmpeg -i final.mp4 -vf "subtitles=captions.ass" -c:a copy final_subbed.mp4
```

Also emit a **sidecar `.srt`** and upload it to YouTube separately — burned-in captions are not searchable.

`FfmpegRunner` wraps every invocation with a hard timeout, stderr capture, and progress parsing from `-progress pipe:1`. Validate every scene with `ffprobe` before concat.

---

## 11. YouTube publishing (M8)

NuGet: `Google.Apis.YouTube.v3`, `Google.Apis.Auth`.

- **Scopes:** `youtube.upload`, plus `youtube.force-ssl` for thumbnails and captions.
- **Flow:** server-side OAuth 2.0 authorization code; refresh token stored per user, encrypted via `IDataProtectionProvider`.
- **Upload:** `ResumableUpload` with an explicit `ChunkSize`. A 20-minute 1080p file is large and a non-resumable POST will fail on any connection hiccup.
- **Metadata:** title, description with **chapter timestamps derived from scene boundaries**, tags, `categoryId`, `privacyStatus`.
- **After upload:** `Thumbnails.Set`, then `Captions.Insert` with the sidecar `.srt`.

### Quota reality — plan for this now

Default YouTube Data API quota is **10,000 units/day**; a single `videos.insert` costs **1,600 units**. That is **~6 uploads per day, total**. Request a quota increase from Google before any real use, and show remaining quota in the UI so users are not blindsided.

### Synthetic media disclosure

YouTube requires disclosure of AI-generated/altered content. Set the altered-content flag on upload and include a disclosure line in the default description template. **On by default, not opt-in.**

---

## 12. MCP server (M9)

`mcp/aivideo-mcp/` — a TypeScript MCP server exposing the pipeline as tools so Claude Code, Claude Desktop, Cursor, or any MCP client can drive it.

**Transports:** stdio (local) and streamable HTTP (remote).

| Tool | Purpose |
|---|---|
| `generate_image` | prompt → still (M1) |
| `edit_image` | image + prompt → edited still (M1) |
| `animate_image` | image → clip (M2) |
| `quick_clip` | prompt → clip (M3) |
| `create_project` | title, mode, targetMinutes, aspectRatio → projectId |
| `generate_script` | topic / transcript / reference YouTube URL → script |
| `plan_scenes` | script → scenes + visual prompts |
| `design_voice` | description → voice id (M6) |
| `render_video` | enqueue render → renderJobId |
| `get_render_status` | stage, percent, per-scene states |
| `regenerate_scene` | fix one scene without a full rerender |
| `voice_swap` | uploaded video + voice → new audio track (M7) |
| `publish_youtube` | upload with metadata (M8) |

```jsonc
// .mcp.json
{
  "mcpServers": {
    "aivideo": {
      "command": "node",
      "args": ["./mcp/aivideo-mcp/dist/index.js"],
      "env": {
        "AIVIDEO_BASE_URL": "https://localhost:7xxx",
        "AIVIDEO_API_KEY": "<key>"
      }
    }
  }
}
```

**Pollo ships its own MCP server** (`npx -y mcp-server-pollo`, tools `text2video` / `img2video` / `getTaskStatus`, env `POLLO_AI_API_KEY`). Useful for manual experimentation — but it knows nothing about scripts, scenes, voices, assembly, or publishing, so it does not replace ours. See <https://docs.pollo.ai/mcp-server>.

> **On "MCP in Chrome":** MCP is a client↔server protocol, not a browser extension API. A Chrome extension can host an MCP *client* speaking to the streamable-HTTP transport above. But if the real goal is "a button in Chrome that starts a render", a small extension calling the REST API directly is far simpler and is what I would recommend.

---

## 13. Configuration

`appsettings.json` — **secrets in User Secrets / environment variables, never in the repo.**

```jsonc
{
  "Pollo": {
    "BaseUrl": "https://pollo.ai/api/platform",
    "ApiKey": "",
    "WebhookUrl": "",
    "MaxConcurrentTasks": 5,
    "Models": {
      "Hero":         "google/veo3-1",
      "Broll":        "pollo/pollo-v2-5",
      "ImageToVideo": "kling-ai/kling-v2-5-turbo",
      "CharacterLock":"kling-ai/kling-v3-omni/ref2video",
      "Still":        "google/nano-banana-pro/image"
    }
  },
  "Voice": {
    "Provider": "ElevenLabs",
    "ApiKey": "",
    "DefaultVoiceId": "",
    "EnableVoiceDesign": true
  },
  "Ffmpeg":  { "BinaryPath": "ffmpeg", "FfprobePath": "ffprobe", "TimeoutMinutes": 30 },
  "Storage": { "Root": "C:\\RAM\\AIVIDEO\\.media", "MaxUploadMb": 2048 },
  "YouTube": { "ClientId": "", "ClientSecret": "", "RedirectUri": "" },
  "Limits":  { "DefaultCostCapUsd": 25, "MaxProjectMinutes": 30 }
}
```

```powershell
dotnet user-secrets init --project AIVIDEO.Server
dotnet user-secrets set "Pollo:ApiKey"  "<key>" --project AIVIDEO.Server
dotnet user-secrets set "Voice:ApiKey"  "<key>" --project AIVIDEO.Server
```

Add `.media/` to `.gitignore` — renders are large and must never be committed.

---

## 14. Build phases

| # | Phase | Deliverable | Proves |
|---|---|---|---|
| 1 | **Quick Clip** | image → Pollo image-to-video → plays in UI | Auth, webhook, asset download, URL expiry — end to end |
| 2 | **Image Studio** | prompt → still; image + prompt → edit | Image models, the cheap half of long-form |
| 3 | **Voice Studio** | Voice Design + TTS + preset library, with word timings | Voice provider works and emits timings |
| 4 | **Voice Swap** | upload video → new voice → download | M7 complete standalone; consent gate |
| 5 | **Scripting** | topic / transcript / YouTube URL → editable script | LLM + all three import paths |
| 6 | **Scene planning** | script → timed scenes, editable grid | The timeline model |
| 7 | **Narration** | per-scene TTS; durations become authoritative | Timeline backbone |
| 8 | **Visuals** | gated fan-out + 4-level fallback + character lock | Scale and resilience |
| 9 | **Assembly** | FFmpeg concat, ducking, subtitles → first full 10-min MP4 | **The actual product** |
| 10 | **Publish** | YouTube OAuth, resumable upload, thumbnail, captions | Distribution |
| 11 | **MCP** | stdio + HTTP server over the REST API | Agent control |
| 12 | **Hardening** | Cancellation, resume, cost ceiling, quota display, retries | Production readiness |

**Ship phase 1 first.** It is small and it validates every risky external assumption — key format, webhook reachability, URL expiry, asset sizes — before a line of pipeline code exists.

Phases 1–4 each ship as a **usable standalone tool**. That matters: you have something real in weeks, not after the whole pipeline lands.

---

## 15. Risks

| Risk | Mitigation |
|---|---|
| 150 generations per video → cost spike | Hybrid stills-first; server-side USD ceiling enforced *before* enqueue; live cost meter |
| Pollo URLs expire in 14 days | Download on completion; local path is the system of record |
| Renders take 30–90 min | Staged resumable queues; SignalR progress; never block a request thread |
| Visual drift across 150 scenes | Style anchor on every prompt; locked subject refs via `ref2video`; fixed seed |
| Two-provider dependency | Abstract `IVoiceProvider` / `IVideoProvider` from day one so either can be swapped |
| Resolution casing differs per model (`720p` vs `720P`) | Per-model request builders; never share resolution constants |
| YouTube quota ≈ 6 uploads/day | Request increase early; display remaining quota; queue uploads |
| FFmpeg concat corruption | Normalise codec/fps/resolution per scene; `ffprobe`-validate before concat |
| Voice cloning misuse | Consent attestation + logging + rate limiting on M7 |
| Synthetic media disclosure | Altered-content flag + description disclosure, on by default |
| Accent preserved in voice swap surprises users | Say so in the UI at the point of choosing a voice |
| Long-running work on Windows/IIS | Background service with graceful shutdown; persist stage state so a recycle resumes |

---

## 16. Open decisions — I need your call on these

1. **Project name** — VoxReel, or one of the alternates in §1? Blocks namespace and MCP id.
2. **Voice provider** — ElevenLabs assumed (best voice design + the only clean voice-changer path). Azure Speech is much cheaper at 20-min scale but has no trailer-voice design. Confirm.
3. **Script LLM** — Claude via the Anthropic API is the default. Confirm.
4. **Database** — SQLite for dev assumed. Postgres or SQL Server for production?
5. **Auth model** — single-user local tool, or multi-user accounts? Changes YouTube token storage and the whole consent design.
6. **Deployment target** — local Windows box, IIS, or container? FFmpeg availability and long-running background work differ significantly per target.
7. **Lip sync** — confirmed out of scope for v1? If it is required, it adds a third provider.

---

## References

**Pollo AI**
- Docs index — <https://docs.pollo.ai/llms.txt>
- Quick start — <https://docs.pollo.ai/quick-start>
- Task status — <https://docs.pollo.ai/task/get-task-status>
- Webhooks — <https://docs.pollo.ai/webhooks>
- MCP server — <https://docs.pollo.ai/mcp-server>
- Pricing — <https://docs.pollo.ai/pricing>
- OpenAPI — <https://docs.pollo.ai/openapi.json>
- VidRush — <https://pollo.ai/m/vidrush>

**Voice**
- ElevenLabs docs index — <https://elevenlabs.io/docs/llms.txt>
- Voice changer (speech-to-speech) — <https://elevenlabs.io/docs/api-reference/speech-to-speech/convert>
- Text to speech — <https://elevenlabs.io/docs/overview/capabilities/text-to-speech>
- Changelog (check for deprecations) — <https://elevenlabs.io/docs/changelog>
