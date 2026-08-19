import { useState } from 'react';
import { api } from '../api';
import ImagePicker from './ImagePicker';

/** M2 — animate a single still into a short clip. */
export default function AnimatePanel({ status, onCreated }) {
    const [source, setSource] = useState(null);
    const [prompt, setPrompt] = useState('');
    const [length, setLength] = useState(5);
    const [resolution, setResolution] = useState('720p');
    const [mode, setMode] = useState('basic');
    const [generateAudio, setGenerateAudio] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const lengths = status?.allowedClipLengths ?? [4, 5, 6, 7, 8, 9, 10, 11, 12, 15];

    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);

        try {
            const created = await api.imageToVideo({
                assetId: source?.assetId ?? null,
                imageUrl: source?.imageUrl ?? null,
                prompt: prompt.trim() || null,
                length: Number(length),
                resolution,
                mode,
                generateAudio,
            });
            onCreated(created);
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(false);
        }
    }

    return (
        <form className="panel" onSubmit={submit}>
            <div className="panel-head">
                <h2>Animate</h2>
                <p>Turn a still into motion. The prompt describes the movement, not the subject.</p>
            </div>

            <label>
                <span>Source image</span>
                <ImagePicker
                    value={source}
                    onChange={setSource}
                    publicBaseUrlConfigured={status?.publicBaseUrlConfigured ?? false}
                />
            </label>

            <label>
                <span>Motion prompt <em>(optional)</em></span>
                <textarea
                    rows={3}
                    value={prompt}
                    placeholder="Slow push in, drifting fog, gentle camera drift to the left"
                    onChange={(e) => setPrompt(e.target.value)}
                />
            </label>

            <div className="row">
                <label>
                    <span>Length</span>
                    <select value={length} onChange={(e) => setLength(e.target.value)}>
                        {lengths.map((l) => <option key={l} value={l}>{l}s</option>)}
                    </select>
                </label>

                <label>
                    <span>Resolution</span>
                    <select value={resolution} onChange={(e) => setResolution(e.target.value)}>
                        <option value="720p">720p</option>
                        <option value="1080p">1080p</option>
                    </select>
                </label>

                <label>
                    <span>Mode</span>
                    <select value={mode} onChange={(e) => setMode(e.target.value)}>
                        <option value="basic">basic</option>
                        <option value="pro">pro</option>
                    </select>
                </label>
            </div>

            <label className="check">
                <input
                    type="checkbox"
                    checked={generateAudio}
                    onChange={(e) => setGenerateAudio(e.target.checked)}
                />
                <span>
                    Let Pollo add ambient audio
                    <em>Leave off for long-form work — narration is mixed separately and would clash.</em>
                </span>
            </label>

            {error && <p className="error">{error}</p>}

            <button type="submit" className="primary" disabled={!source || busy}>
                {busy ? 'Submitting…' : 'Animate image'}
            </button>
        </form>
    );
}
