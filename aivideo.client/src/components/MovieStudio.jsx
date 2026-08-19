import { useCallback, useEffect, useRef, useState } from 'react';
import { api } from '../api';

const STATUS_LABELS = {
    Draft: 'Queued',
    Planning: 'Writing script & planning scenes',
    Narrating: 'Recording narration',
    GeneratingVisuals: 'Generating scene visuals',
    Assembling: 'Assembling video',
    Ready: 'Ready',
    Failed: 'Failed',
};

const ACTIVE = ['Draft', 'Planning', 'Narrating', 'GeneratingVisuals', 'Assembling'];

/**
 * Long-form movie builder. Topic in, a full narrated video out — assembled from free images,
 * Ken Burns motion, and local text-to-speech. Everything is free and local; a build takes a
 * few minutes, so the project list polls while any project is still working.
 */
export default function MovieStudio({ status }) {
    const [title, setTitle] = useState('');
    const [topic, setTopic] = useState('');
    const [scriptText, setScriptText] = useState('');
    const [useScript, setUseScript] = useState(false);
    const [minutes, setMinutes] = useState(1);
    const [aspectRatio, setAspectRatio] = useState('16:9');
    const [visualStyle, setVisualStyle] = useState('cinematic');
    const [useRag, setUseRag] = useState(false);
    const [music, setMusic] = useState(true);
    const [subtitles, setSubtitles] = useState(true);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);
    const [projects, setProjects] = useState([]);
    const [selected, setSelected] = useState(null);

    const ollamaReady = status?.ollamaAvailable ?? false;

    const refresh = useCallback(async () => {
        try {
            const list = await api.listVideoProjects();
            setProjects(list);
        } catch { /* best effort */ }
    }, []);

    useEffect(() => { refresh(); }, [refresh]);

    const anyActive = projects.some((p) => ACTIVE.includes(p.status));
    useEffect(() => {
        if (!anyActive) return undefined;
        const t = setInterval(refresh, 4000);
        return () => clearInterval(t);
    }, [anyActive, refresh]);

    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);
        try {
            await api.createVideoProject({
                title: title.trim() || topic.trim().slice(0, 60) || 'Untitled',
                topic: topic.trim(),
                scriptText: useScript ? scriptText.trim() : null,
                targetMinutes: Number(minutes),
                aspectRatio,
                visualStyle,
                useRag,
                backgroundMusic: music,
                subtitles,
            });
            setTopic('');
            setTitle('');
            setScriptText('');
            await refresh();
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(false);
        }
    }

    async function remove(id) {
        setProjects((p) => p.filter((x) => x.id !== id));
        if (selected?.id === id) setSelected(null);
        try { await api.deleteVideoProject(id); } catch { refresh(); }
    }

    return (
        <div className="layout">
            <form className="panel" onSubmit={submit}>
                <div className="panel-head">
                    <h2>Movie Maker</h2>
                    <p>A full narrated video from a topic — free images, motion, and voiceover, assembled for you.</p>
                </div>

                {!ollamaReady && !useScript && (
                    <div className="banner banner-warn">
                        <strong>Ollama isn't running.</strong> Writing a script from a topic needs it —
                        or paste your own script below, which works without Ollama.
                        <code>ollama pull llama3.2</code>
                    </div>
                )}

                <label>
                    <span>Title <em>(optional)</em></span>
                    <input type="text" value={title} placeholder="The Cape Hatteras Lighthouse"
                        onChange={(e) => setTitle(e.target.value)} />
                </label>
                <div className="segmented">
                    <button type="button" className={!useScript ? 'seg seg-active' : 'seg'} onClick={() => setUseScript(false)}>
                        From a topic
                    </button>
                    <button type="button" className={useScript ? 'seg seg-active' : 'seg'} onClick={() => setUseScript(true)}>
                        Paste a script / transcript
                    </button>
                </div>

                {!useScript && (
                    <label>
                        <span>Topic</span>
                        <textarea rows={3} value={topic} placeholder="Tell the story of the tallest lighthouse in America"
                            onChange={(e) => setTopic(e.target.value)} />
                    </label>
                )}

                {useScript && (
                    <label>
                        <span>Script / transcript</span>
                        <textarea rows={8} value={scriptText}
                            placeholder="Paste your script or a YouTube transcript. Timestamps and [Music] markers are cleaned automatically; the words become the narration."
                            onChange={(e) => setScriptText(e.target.value)} />
                        <em className="hint">Long transcripts are grouped into up to 60 scenes. The LLM script step is skipped.</em>
                    </label>
                )}
                <div className="row">
                    <label>
                        <span>Length</span>
                        <select value={minutes} onChange={(e) => setMinutes(e.target.value)}>
                            {[1, 2, 3, 5].map((m) => <option key={m} value={m}>{m} min</option>)}
                        </select>
                    </label>
                    <label>
                        <span>Aspect</span>
                        <select value={aspectRatio} onChange={(e) => setAspectRatio(e.target.value)}>
                            <option value="16:9">16:9 (landscape)</option>
                            <option value="9:16">9:16 (shorts)</option>
                            <option value="1:1">1:1 (square)</option>
                        </select>
                    </label>
                </div>
                <label>
                    <span>Visual style</span>
                    <select value={visualStyle} onChange={(e) => setVisualStyle(e.target.value)}>
                        <option value="cinematic">Cinematic</option>
                        <option value="photorealistic">Photorealistic</option>
                        <option value="cartoon">Cartoon (kids)</option>
                        <option value="anime">Anime</option>
                        <option value="3d">3D animation (Pixar-like)</option>
                        <option value="watercolor">Watercolor</option>
                        <option value="storybook">Storybook</option>
                    </select>
                </label>
                <label className="check">
                    <input type="checkbox" checked={music} onChange={(e) => setMusic(e.target.checked)} />
                    <span>Background music<em>A soft ambient bed, ducked under the narration.</em></span>
                </label>
                <label className="check">
                    <input type="checkbox" checked={subtitles} onChange={(e) => setSubtitles(e.target.checked)} />
                    <span>Burn-in subtitles<em>On-screen captions timed to the narration.</em></span>
                </label>
                <label className="check">
                    <input type="checkbox" checked={useRag} onChange={(e) => setUseRag(e.target.checked)} />
                    <span>Ground in my documents (RAG)<em>Uses the Knowledge tab's material.</em></span>
                </label>

                {error && <p className="error">{error}</p>}

                <button type="submit" className="primary"
                    disabled={busy || (useScript ? !scriptText.trim() : (!topic.trim() || !ollamaReady))}>
                    {busy ? 'Starting…' : 'Make the movie'}
                </button>
                <p className="hint">Takes a few minutes. It writes a script, records narration, generates a visual per scene, and stitches it together.</p>
            </form>

            <section className="gallery">
                <div className="gallery-head">
                    <h2>Your movies</h2>
                    <button className="link-button" onClick={refresh}>Refresh</button>
                </div>
                {projects.length === 0 && <p className="empty">No movies yet.</p>}
                <div className="movie-list">
                    {projects.map((p) => (
                        <article className={`card card-${p.status.toLowerCase()}`} key={p.id}>
                            <header className="card-head">
                                <span className={`badge badge-${ACTIVE.includes(p.status) ? 'processing' : p.status.toLowerCase()}`}>
                                    {STATUS_LABELS[p.status] ?? p.status}
                                </span>
                                <span className="card-kind">{p.targetMinutes} min</span>
                            </header>

                            <div className="card-media">
                                {p.videoUrl && <video src={p.videoUrl} controls preload="metadata" />}
                                {!p.videoUrl && ACTIVE.includes(p.status) && (
                                    <div className="placeholder">
                                        <div className="spinner" />
                                        <p>{STATUS_LABELS[p.status]}… {p.progress}%</p>
                                        <div className="progress"><div style={{ width: `${p.progress}%` }} /></div>
                                    </div>
                                )}
                                {!p.videoUrl && p.status === 'Failed' && (
                                    <div className="placeholder placeholder-failed"><p>{p.errorMessage ?? 'Failed.'}</p></div>
                                )}
                            </div>

                            <p className="card-prompt">{p.title}</p>

                            <div className="card-actions">
                                {p.videoUrl && (
                                    <a className="act act-download" href={p.videoUrl} download={`${p.title.replace(/[^a-z0-9]+/gi, '-')}.mp4`}>↓ Download</a>
                                )}
                                <button className="act act-delete" onClick={() => remove(p.id)}>🗑 Delete</button>
                            </div>
                        </article>
                    ))}
                </div>
            </section>
        </div>
    );
}
