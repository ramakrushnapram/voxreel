import { useState } from 'react';
import { api } from '../api';
import ImagePicker from './ImagePicker';

const ASPECT_RATIOS = ['16:9', '9:16', '1:1', '4:3', '3:4', '3:2', '2:3', '21:9'];
const RESOLUTIONS = ['1K', '2K', '4K'];

/**
 * M1 — Image Studio. Two modes against one endpoint: prompt-only generation, and
 * "upload an image and describe the change" editing.
 */
export default function ImageStudio({ status, onCreated }) {
    const [mode, setMode] = useState('generate');
    const [prompt, setPrompt] = useState('');
    const [aspectRatio, setAspectRatio] = useState('16:9');
    const [resolution, setResolution] = useState('1K');
    // Default to the free provider: it works with no key and no cost. Pollo is opt-in and
    // needs a key with credits (an empty Pollo balance returns 403).
    const [provider, setProvider] = useState('free');
    const [source, setSource] = useState(null);
    const [busy, setBusy] = useState(false);
    const [enhancing, setEnhancing] = useState(false);
    const [error, setError] = useState(null);

    const editing = mode === 'edit';
    const ollamaReady = status?.ollamaAvailable ?? false;

    async function enhance() {
        if (!prompt.trim()) return;
        setEnhancing(true);
        setError(null);
        try {
            const { enhanced } = await api.enhancePrompt(prompt.trim());
            setPrompt(enhanced);
        } catch (err) {
            setError(err.message);
        } finally {
            setEnhancing(false);
        }
    }
    // Editing an existing image is a Pollo-only capability, so switching to edit forces Pollo.
    const effectiveProvider = editing ? 'pollo' : provider;
    const canSubmit = prompt.trim().length > 0 && (!editing || source) && !busy;

    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);

        try {
            const created = await api.generateImage({
                prompt: prompt.trim(),
                aspectRatio,
                resolution,
                provider: effectiveProvider,
                sourceAssetId: editing ? source?.assetId ?? null : null,
                sourceImageUrl: editing ? source?.imageUrl ?? null : null,
            });
            onCreated(created);
            setPrompt('');
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(false);
        }
    }

    return (
        <form className="panel" onSubmit={submit}>
            <div className="panel-head">
                <h2>Image Studio</h2>
                <p>Generate a still, or upload one and describe how it should change.</p>
            </div>

            <div className="segmented">
                <button
                    type="button"
                    className={!editing ? 'seg seg-active' : 'seg'}
                    onClick={() => setMode('generate')}
                >
                    Generate from a prompt
                </button>
                <button
                    type="button"
                    className={editing ? 'seg seg-active' : 'seg'}
                    onClick={() => setMode('edit')}
                >
                    Upload an image and ask
                </button>
            </div>

            {!editing && (
                <label>
                    <span>Provider</span>
                    <select value={provider} onChange={(e) => setProvider(e.target.value)}>
                        <option value="free">Free — Pollinations (no key, $0)</option>
                        <option value="pollo">Pollo — your key (needs credits)</option>
                    </select>
                    {provider === 'pollo' && !status?.polloConfigured && (
                        <em className="warn-inline">No Pollo key on the server — this will fail. Use Free.</em>
                    )}
                    {provider === 'free' && (
                        <em className="hint">Uses the open Flux model. Free and unlimited, no key needed.</em>
                    )}
                </label>
            )}

            {editing && (
                <label>
                    <span>Source image</span>
                    <ImagePicker
                        value={source}
                        onChange={setSource}
                        publicBaseUrlConfigured={status?.publicBaseUrlConfigured ?? false}
                    />
                    <em className="hint">Image editing uses Pollo and needs a key with credits.</em>
                </label>
            )}

            <label>
                <span className="label-row">
                    {editing ? 'What should change?' : 'Prompt'}
                    <button
                        type="button"
                        className="mini-btn"
                        onClick={enhance}
                        disabled={enhancing || !prompt.trim() || !ollamaReady}
                        title={ollamaReady ? 'Rewrite into a richer prompt with the local LLM' : 'Install Ollama to enable AI prompt enhancement'}
                    >
                        {enhancing ? 'Enhancing…' : '✨ Enhance with AI'}
                    </button>
                </span>
                <textarea
                    rows={4}
                    value={prompt}
                    placeholder={
                        editing
                            ? 'Make the sky a dramatic sunset and add volumetric light through the trees'
                            : 'A lone lighthouse on a storm-lit cliff, cinematic, 35mm, cold blue palette'
                    }
                    onChange={(e) => setPrompt(e.target.value)}
                />
            </label>

            <div className="row">
                <label>
                    <span>Aspect ratio</span>
                    <select value={aspectRatio} onChange={(e) => setAspectRatio(e.target.value)}>
                        {ASPECT_RATIOS.map((r) => <option key={r} value={r}>{r}</option>)}
                    </select>
                </label>

                <label>
                    <span>Resolution</span>
                    <select value={resolution} onChange={(e) => setResolution(e.target.value)}>
                        {RESOLUTIONS.map((r) => <option key={r} value={r}>{r}</option>)}
                    </select>
                </label>
            </div>

            {error && <p className="error">{error}</p>}

            <button type="submit" className="primary" disabled={!canSubmit}>
                {busy ? 'Submitting…' : editing ? 'Apply edit' : 'Generate image'}
            </button>
        </form>
    );
}
