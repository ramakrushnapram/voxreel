import { useState } from 'react';
import { api } from '../api';

/**
 * Local-LLM script writer (the text half of the long-form pipeline). Topic in, narration
 * script out. If the user has uploaded documents, the RAG toggle grounds the script in them.
 */
export default function ScriptStudio({ status }) {
    const [topic, setTopic] = useState('');
    const [minutes, setMinutes] = useState(3);
    const [useRag, setUseRag] = useState(false);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);
    const [result, setResult] = useState(null);
    const [copied, setCopied] = useState(false);

    const ollamaReady = status?.ollamaAvailable ?? false;

    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);
        setResult(null);
        try {
            const res = await api.generateScript({ topic: topic.trim(), targetMinutes: Number(minutes), useRag });
            setResult(res);
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(false);
        }
    }

    async function copy() {
        await navigator.clipboard.writeText(result.script);
        setCopied(true);
        setTimeout(() => setCopied(false), 1500);
    }

    return (
        <div className="script-layout">
            <form className="panel" onSubmit={submit}>
                <div className="panel-head">
                    <h2>Script Writer</h2>
                    <p>Turn a topic into a narration script with the local LLM. Free, runs on your machine.</p>
                </div>

                {!ollamaReady && (
                    <div className="banner banner-warn">
                        <strong>Ollama isn't running.</strong> Install it from ollama.com, then
                        <code>ollama pull llama3.2</code>
                        Scripts activate automatically once it's up.
                    </div>
                )}

                <label>
                    <span>Topic</span>
                    <textarea rows={3} value={topic} placeholder="The history of the lighthouse at Cape Hatteras"
                        onChange={(e) => setTopic(e.target.value)} />
                </label>

                <div className="row">
                    <label>
                        <span>Length (minutes)</span>
                        <select value={minutes} onChange={(e) => setMinutes(e.target.value)}>
                            {[1, 2, 3, 5, 8, 10, 15, 20].map((m) => <option key={m} value={m}>{m} min</option>)}
                        </select>
                    </label>
                </div>

                <label className="check">
                    <input type="checkbox" checked={useRag} onChange={(e) => setUseRag(e.target.checked)} />
                    <span>
                        Ground in my documents (RAG)
                        <em>Uses the reference material from the Knowledge tab.</em>
                    </span>
                </label>

                {error && <p className="error">{error}</p>}

                <button type="submit" className="primary" disabled={!topic.trim() || busy || !ollamaReady}>
                    {busy ? 'Writing…' : 'Write script'}
                </button>
            </form>

            <div className="script-output">
                {!result && <p className="empty">Your script will appear here.</p>}
                {result && (
                    <div className="panel">
                        <div className="gallery-head">
                            <h2>Script — {result.wordCount} words{result.groundingChunksUsed > 0 ? ` · grounded in ${result.groundingChunksUsed} excerpt(s)` : ''}</h2>
                            <button className="link-button" onClick={copy}>{copied ? 'Copied!' : 'Copy'}</button>
                        </div>
                        <div className="script-text">{result.script}</div>
                    </div>
                )}
            </div>
        </div>
    );
}
