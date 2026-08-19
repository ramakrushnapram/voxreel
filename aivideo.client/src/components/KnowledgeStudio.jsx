import { useCallback, useEffect, useState } from 'react';
import { api } from '../api';

/**
 * RAG document manager. Paste or upload reference material; it's chunked and embedded (via
 * the local Ollama embedding model) so the Script Writer can ground output in it.
 */
export default function KnowledgeStudio({ status }) {
    const [docs, setDocs] = useState([]);
    const [name, setName] = useState('');
    const [text, setText] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const ollamaReady = status?.ollamaAvailable ?? false;

    const refresh = useCallback(async () => {
        try {
            setDocs(await api.listDocuments());
        } catch {
            /* listing is best-effort; ingest errors are what matter */
        }
    }, []);

    useEffect(() => { refresh(); }, [refresh]);

    async function add(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);
        try {
            await api.createDocument(name.trim() || 'Untitled', text.trim());
            setName('');
            setText('');
            await refresh();
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(false);
        }
    }

    async function remove(id) {
        setDocs((d) => d.filter((x) => x.id !== id));
        try { await api.deleteDocument(id); } catch { refresh(); }
    }

    return (
        <div className="layout">
            <form className="panel" onSubmit={add}>
                <div className="panel-head">
                    <h2>Knowledge (RAG)</h2>
                    <p>Add reference material so scripts are grounded in your own facts and tone.</p>
                </div>

                {!ollamaReady && (
                    <div className="banner banner-warn">
                        <strong>Ollama isn't running.</strong> Embeddings need it. Install it, then
                        <code>ollama pull nomic-embed-text</code>
                    </div>
                )}

                <label>
                    <span>Name</span>
                    <input type="text" value={name} placeholder="Cape Hatteras notes"
                        onChange={(e) => setName(e.target.value)} />
                </label>
                <label>
                    <span>Text</span>
                    <textarea rows={8} value={text} placeholder="Paste a transcript, notes, or a style guide…"
                        onChange={(e) => setText(e.target.value)} />
                </label>

                {error && <p className="error">{error}</p>}

                <button type="submit" className="primary" disabled={!text.trim() || busy || !ollamaReady}>
                    {busy ? 'Embedding…' : 'Add to knowledge base'}
                </button>
            </form>

            <section className="gallery">
                <div className="gallery-head">
                    <h2>Your documents</h2>
                    <button className="link-button" onClick={refresh}>Refresh</button>
                </div>
                {docs.length === 0 && <p className="empty">No documents yet.</p>}
                <div className="doc-list">
                    {docs.map((d) => (
                        <div className="doc" key={d.id}>
                            <div>
                                <div className="doc-name">{d.name}</div>
                                <div className="doc-meta">{d.chunkCount} chunk(s)</div>
                            </div>
                            <button className="act act-delete" onClick={() => remove(d.id)}>🗑 Delete</button>
                        </div>
                    ))}
                </div>
            </section>
        </div>
    );
}
