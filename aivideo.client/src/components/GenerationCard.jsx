import { useState } from 'react';
import { isTerminal } from '../api';

const STATUS_LABELS = {
    Pending: 'Queued',
    Submitted: 'Submitted to Pollo',
    Processing: 'Generating',
    Downloading: 'Downloading result',
    Succeeded: 'Ready',
    Failed: 'Failed',
    Cancelled: 'Cancelled',
};

export default function GenerationCard({ generation, onDelete }) {
    const { id, status, kind, prompt, model, failMessage, assets, costUsd, length, resolution } = generation;
    const [deleting, setDeleting] = useState(false);

    // ASP.NET Core serialises with camelCase, so asset kinds arrive as `kind`.
    const video = assets.find((a) => a.kind === 'Video');
    const image = assets.find((a) => a.kind === 'Image');
    const thumbnail = assets.find((a) => a.kind === 'Thumbnail');
    const downloadable = video ?? image;

    // A stable, human-friendly filename derived from the prompt, so saved files aren't GUIDs.
    const ext = video ? 'mp4' : 'jpg';
    const base = (prompt ?? 'voxreel').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 40) || 'voxreel';

    async function handleDelete() {
        setDeleting(true);
        try {
            await onDelete(id);
        } finally {
            setDeleting(false);
        }
    }

    return (
        <article className={`card card-${status.toLowerCase()}`}>
            <header className="card-head">
                <span className={`badge badge-${status.toLowerCase()}`}>
                    {STATUS_LABELS[status] ?? status}
                </span>
                <span className="card-kind">{kind}</span>
            </header>

            <div className="card-media">
                {video && <video src={video.url} controls preload="metadata" poster={thumbnail?.url} />}
                {!video && image && <img src={image.url} alt={prompt ?? 'Generated image'} />}
                {!video && !image && !isTerminal(status) && (
                    <div className="placeholder">
                        <div className="spinner" />
                        <p>This usually takes a minute or two.</p>
                    </div>
                )}
                {!video && !image && status === 'Failed' && (
                    <div className="placeholder placeholder-failed">
                        <p>{failMessage ?? 'Generation failed.'}</p>
                    </div>
                )}
            </div>

            {prompt && <p className="card-prompt">{prompt}</p>}

            <div className="card-actions">
                {downloadable && (
                    <a className="act act-download" href={downloadable.url} download={`${base}.${ext}`}>
                        ↓ Download
                    </a>
                )}
                {downloadable && (
                    <a className="act" href={downloadable.url} target="_blank" rel="noreferrer">
                        ⛶ Open
                    </a>
                )}
                {onDelete && (
                    <button className="act act-delete" onClick={handleDelete} disabled={deleting}>
                        {deleting ? 'Removing…' : '🗑 Delete'}
                    </button>
                )}
            </div>

            <footer className="card-foot">
                <span>{model}</span>
                {length && <span>{length}s</span>}
                {resolution && <span>{resolution}</span>}
                {costUsd != null && <span>${Number(costUsd).toFixed(4)}</span>}
            </footer>
        </article>
    );
}
