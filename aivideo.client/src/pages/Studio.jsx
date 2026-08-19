import { useCallback, useEffect, useState } from 'react';
import { api, isTerminal } from '../api';
import ImageStudio from '../components/ImageStudio';
import AnimatePanel from '../components/AnimatePanel';
import QuickClipPanel from '../components/QuickClipPanel';
import ScriptStudio from '../components/ScriptStudio';
import KnowledgeStudio from '../components/KnowledgeStudio';
import MovieStudio from '../components/MovieStudio';
import GenerationCard from '../components/GenerationCard';

const TABS = [
    { id: 'movie', label: '🎬 Movie Maker' },
    { id: 'image', label: 'Image Studio' },
    { id: 'animate', label: 'Animate' },
    { id: 'clip', label: 'Quick Clip' },
    { id: 'script', label: 'Script Writer' },
    { id: 'knowledge', label: 'Knowledge' },
];

/** The signed-in workspace: generation panels plus a live-polling gallery of the user's own work. */
export default function Studio() {
    const [tab, setTab] = useState('movie');
    const [status, setStatus] = useState(null);
    const [generations, setGenerations] = useState([]);
    const [galleryError, setGalleryError] = useState(null);

    useEffect(() => {
        api.status().then(setStatus).catch(() => setStatus(null));
    }, []);

    const refresh = useCallback(async () => {
        try {
            setGenerations(await api.listGenerations());
            setGalleryError(null);
        } catch (err) {
            setGalleryError(err.message);
        }
    }, []);

    useEffect(() => { refresh(); }, [refresh]);

    // Poll only while something is in flight, derived during render so the interval is created
    // and torn down by the pending state itself.
    const hasPending = generations.some((g) => !isTerminal(g.status));
    useEffect(() => {
        if (!hasPending) return undefined;
        const interval = setInterval(refresh, 5000);
        return () => clearInterval(interval);
    }, [hasPending, refresh]);

    function handleCreated(generation) {
        setGenerations((current) => [generation, ...current]);
    }

    async function handleDelete(id) {
        // Optimistic: drop it from the list immediately; the DELETE is idempotent so a
        // failure just means it reappears on the next refresh.
        setGenerations((current) => current.filter((g) => g.id !== id));
        try {
            await api.deleteGeneration(id);
        } catch {
            refresh();
        }
    }

    return (
        <div className="studio">
            <div className="studio-head">
                <h1>Studio</h1>
                <p>Generate images and clips. Pick <strong>Free</strong> for no-cost, no-key generation.</p>
            </div>

            <PolloKeyNotice status={status} />

            <nav className="tabs">
                {TABS.map((t) => (
                    <button
                        key={t.id}
                        className={tab === t.id ? 'tab tab-active' : 'tab'}
                        onClick={() => setTab(t.id)}
                    >
                        {t.label}
                    </button>
                ))}
            </nav>

            {/* Movie, Script and Knowledge have their own full-width layout and no
                image-generation gallery. */}
            {tab === 'movie' && <MovieStudio status={status} />}
            {tab === 'script' && <ScriptStudio status={status} />}
            {tab === 'knowledge' && <KnowledgeStudio status={status} />}

            {(tab === 'image' || tab === 'animate' || tab === 'clip') && (
                <div className="layout">
                    <section className="controls">
                        {tab === 'image' && <ImageStudio status={status} onCreated={handleCreated} />}
                        {tab === 'animate' && <AnimatePanel status={status} onCreated={handleCreated} />}
                        {tab === 'clip' && <QuickClipPanel status={status} onCreated={handleCreated} />}
                    </section>

                    <section className="gallery">
                        <div className="gallery-head">
                            <h2>Your generations</h2>
                            <button className="link-button" onClick={refresh}>Refresh</button>
                        </div>

                        {galleryError && <p className="error">{galleryError}</p>}

                        {!galleryError && generations.length === 0 && (
                            <p className="empty">Nothing yet. Submit something on the left.</p>
                        )}

                        <div className="grid">
                            {generations.map((g) => (
                                <GenerationCard key={g.id} generation={g} onDelete={handleDelete} />
                            ))}
                        </div>
                    </section>
                </div>
            )}
        </div>
    );
}

/** Shown only when the server reports no Pollo key — the one thing that blocks real output. */
function PolloKeyNotice({ status }) {
    if (!status || status.polloConfigured) return null;

    return (
        <div className="banner banner-warn">
            <strong>No Pollo API key yet.</strong> Generations are recorded but will fail until a key is set on the server:
            <code>dotnet user-secrets set "Pollo:ApiKey" "&lt;key&gt;" --project AIVIDEO.Server</code>
            Get one at <a href="https://api.pollo.ai/api-keys" target="_blank" rel="noreferrer">api.pollo.ai/api-keys</a>, then restart the server.
        </div>
    );
}
