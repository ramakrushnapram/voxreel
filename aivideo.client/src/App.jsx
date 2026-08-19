import { useCallback, useEffect, useState } from 'react';
import { api, isTerminal } from './api';
import ImageStudio from './components/ImageStudio';
import AnimatePanel from './components/AnimatePanel';
import QuickClipPanel from './components/QuickClipPanel';
import GenerationCard from './components/GenerationCard';
import './App.css';

const TABS = [
    { id: 'image', label: 'Image Studio' },
    { id: 'animate', label: 'Animate' },
    { id: 'clip', label: 'Quick Clip' },
];

export default function App() {
    const [tab, setTab] = useState('image');
    const [status, setStatus] = useState(null);
    const [generations, setGenerations] = useState([]);
    const [loadError, setLoadError] = useState(null);

    useEffect(() => {
        api.status().then(setStatus).catch((err) => setLoadError(err.message));
    }, []);

    const refresh = useCallback(async () => {
        try {
            setGenerations(await api.listGenerations());
            setLoadError(null);
        } catch (err) {
            setLoadError(err.message);
        }
    }, []);

    useEffect(() => { refresh(); }, [refresh]);

    // Poll only while something is actually in flight. Renders take minutes, so a permanent
    // interval would be mostly wasted requests once the queue drains. Deriving this during
    // render (rather than reading a ref) means the interval is created and torn down by the
    // pending state itself, with no ref mutation during render.
    const hasPending = generations.some((g) => !isTerminal(g.status));

    useEffect(() => {
        if (!hasPending) return undefined;

        const interval = setInterval(refresh, 5000);
        return () => clearInterval(interval);
    }, [hasPending, refresh]);

    function handleCreated(generation) {
        setGenerations((current) => [generation, ...current]);
    }

    return (
        <div className="app">
            <header className="app-head">
                <div>
                    <h1>VoxReel Studio</h1>
                    <p className="tagline">AI image, animation, and long-form video — powered by Pollo AI</p>
                </div>
            </header>

            <StatusBanner status={status} loadError={loadError} />

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

            <main className="layout">
                <section className="controls">
                    {tab === 'image' && <ImageStudio status={status} onCreated={handleCreated} />}
                    {tab === 'animate' && <AnimatePanel status={status} onCreated={handleCreated} />}
                    {tab === 'clip' && <QuickClipPanel status={status} onCreated={handleCreated} />}
                </section>

                <section className="gallery">
                    <div className="gallery-head">
                        <h2>Recent generations</h2>
                        <button className="link-button" onClick={refresh}>Refresh</button>
                    </div>

                    {generations.length === 0 && (
                        <p className="empty">Nothing generated yet. Submit something on the left.</p>
                    )}

                    <div className="grid">
                        {generations.map((g) => <GenerationCard key={g.id} generation={g} />)}
                    </div>
                </section>
            </main>
        </div>
    );
}

/**
 * Surfaces the two configuration problems that otherwise present as a confusing failure
 * on the first generation attempt, rather than as an obvious setup step.
 */
function StatusBanner({ status, loadError }) {
    if (loadError) {
        return <div className="banner banner-error"><strong>Cannot reach the API.</strong> {loadError}</div>;
    }

    if (!status) return null;

    const problems = [];

    if (!status.polloConfigured) {
        problems.push(
            <li key="pollo">
                <strong>No Pollo API key.</strong> Generation will fail until one is set:
                <code>dotnet user-secrets set "Pollo:ApiKey" "&lt;key&gt;" --project AIVIDEO.Server</code>
                Get a key at <a href="https://api.pollo.ai/api-keys" target="_blank" rel="noreferrer">api.pollo.ai/api-keys</a>.
            </li>
        );
    }

    if (!status.databaseReachable) {
        problems.push(
            <li key="db">
                <strong>PostgreSQL unreachable.</strong> {status.databaseError}
                Check <code>ConnectionStrings:Default</code> in appsettings.json.
            </li>
        );
    }

    if (problems.length === 0) {
        return (
            <div className="banner banner-ok">
                Connected. Clips are capped at {status.maxClipSeconds}s each —
                long-form runtimes are produced by assembling many of them.
            </div>
        );
    }

    return <div className="banner banner-warn"><ul>{problems}</ul></div>;
}
