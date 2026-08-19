# VoxReel — Local LLM (Ollama) & RAG

The Script Writer, the ✨ Enhance-with-AI button, and RAG all run on a **local** LLM through
[Ollama](https://ollama.com). It's free, runs on your machine, and needs no API key — nothing
leaves your computer. The app works fine without it; these features simply show
"install Ollama" until it's running.

---

## Setup

**1. Install Ollama** — <https://ollama.com/download> (Windows/Mac/Linux). It runs a local
server on `http://localhost:11434` automatically after install.

**2. Pull the two models:**
```bash
ollama pull llama3.2          # chat model — prompt enhancement + script writing (~2 GB)
ollama pull nomic-embed-text  # embeddings — RAG document search (~275 MB)
```

**3. That's it.** The app polls `localhost:11434` and the features light up automatically —
no server restart needed. Confirm with:
```bash
curl -k https://localhost:7244/api/system/status
```
`"ollamaAvailable": true` and the model names under `ollamaModels` means you're ready.

---

## What each feature does

| Feature | Where | Model | Notes |
|---|---|---|---|
| **Enhance with AI** | Image Studio, next to the prompt | `llama3.2` | Rewrites a short prompt into a rich, detailed one before generating |
| **Script Writer** | Studio → Script Writer tab | `llama3.2` | Topic + length → narration script |
| **Knowledge (RAG)** | Studio → Knowledge tab | `nomic-embed-text` | Upload/paste reference text; it's embedded and used to ground scripts |

RAG flow: your documents are split into chunks and embedded. When you enable **"Ground in my
documents"** in the Script Writer, the app embeds your topic, finds the most similar chunks
(cosine similarity, computed in-app — no pgvector needed), and feeds them to the model so the
script uses your facts and tone.

---

## Configuration

Defaults live in `appsettings.json` under `Ollama` and can be overridden via user-secrets or
environment variables:

```jsonc
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "ChatModel": "llama3.2",          // any pulled instruct model, e.g. "llama3.1", "mistral"
  "EmbeddingModel": "nomic-embed-text",
  "TimeoutSeconds": 180             // raise if script generation is slow on CPU-only machines
}
```

To use a different chat model, pull it and set `Ollama:ChatModel`:
```bash
ollama pull mistral
dotnet user-secrets set "Ollama:ChatModel" "mistral" --project AIVIDEO.Server
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| "Ollama isn't running" | Server not started | Install Ollama; it starts on 11434 automatically. Test: `curl http://localhost:11434/api/tags` |
| 404 / "pull the model first" | Model not downloaded | `ollama pull llama3.2` (and `nomic-embed-text`) |
| Script generation times out | Large model on CPU | Use a smaller model, or raise `Ollama:TimeoutSeconds` |
| Enhance button greyed out | Ollama not detected | Same as above; the button enables once `ollamaAvailable` is true |

Everything here is optional and free. No key, no cost, no data leaving your machine.
