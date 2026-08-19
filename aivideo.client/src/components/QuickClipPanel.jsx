import { useState } from 'react';
import { api } from '../api';

/** M3 — prompt straight to a short clip. */
export default function QuickClipPanel({ status, onCreated }) {
    const [prompt, setPrompt] = useState('');
    const [length, setLength] = useState(5);
    const [resolution, setResolution] = useState('720p');
    const [aspectRatio, setAspectRatio] = useState('16:9');
    const [mode, setMode] = useState('basic');
    const [role, setRole] = useState('Broll');
    const [generateAudio, setGenerateAudio] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const lengths = status?.allowedClipLengths ?? [4, 5, 6, 7, 8, 9, 10, 11, 12, 15];
    const maxSeconds = status?.maxClipSeconds ?? 15;

    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);

        try {
            const created = await api.textToVideo({
                prompt: prompt.trim(),
                length: Number(length),
                resolution,
                aspectRatio,
                mode,
                role,
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
                <h2>Quick Clip</h2>
                <p>
                    A prompt straight to video. Pollo caps a single clip at {maxSeconds}s — longer
                    runtimes come from assembling many clips, not from a larger number here.
                </p>
            </div>

            <label>
                <span>Prompt</span>
                <textarea
                    rows={4}
                    value={prompt}
                    placeholder="Aerial shot over a misty pine forest at dawn, slow forward drift, golden light"
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
                    <span>Aspect</span>
                    <select value={aspectRatio} onChange={(e) => setAspectRatio(e.target.value)}>
                        <option value="16:9">16:9</option>
                        <option value="9:16">9:16</option>
                    </select>
                </label>
            </div>

            <div className="row">
                <label>
                    <span>Mode</span>
                    <select value={mode} onChange={(e) => setMode(e.target.value)}>
                        <option value="basic">basic</option>
                        <option value="pro">pro</option>
                    </select>
                </label>

                <label>
                    <span>Model role</span>
                    <select value={role} onChange={(e) => setRole(e.target.value)}>
                        <option value="Broll">B-roll (cheaper)</option>
                        <option value="Hero">Hero (best quality)</option>
                    </select>
                </label>
            </div>

            <label className="check">
                <input
                    type="checkbox"
                    checked={generateAudio}
                    onChange={(e) => setGenerateAudio(e.target.checked)}
                />
                <span>Let Pollo add ambient audio</span>
            </label>

            {error && <p className="error">{error}</p>}

            <button type="submit" className="primary" disabled={!prompt.trim() || busy}>
                {busy ? 'Submitting…' : 'Generate clip'}
            </button>
        </form>
    );
}
