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

export default function GenerationCard({ generation }) {
    const { status, kind, prompt, model, failMessage, assets, costUsd, length, resolution } = generation;

    // ASP.NET Core serialises with camelCase, so asset kinds arrive as `kind`.
    const video = assets.find((a) => a.kind === 'Video');
    const image = assets.find((a) => a.kind === 'Image');
    const thumbnail = assets.find((a) => a.kind === 'Thumbnail');

    return (
        <article className={`card card-${status.toLowerCase()}`}>
            <header className="card-head">
                <span className={`badge badge-${status.toLowerCase()}`}>
                    {STATUS_LABELS[status] ?? status}
                </span>
                <span className="card-kind">{kind}</span>
            </header>

            <div className="card-media">
                {video && (
                    <video
                        src={video.url}
                        controls
                        preload="metadata"
                        poster={thumbnail?.url}
                    />
                )}
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

            <footer className="card-foot">
                <span>{model}</span>
                {length && <span>{length}s</span>}
                {resolution && <span>{resolution}</span>}
                {costUsd != null && <span>${Number(costUsd).toFixed(4)}</span>}
            </footer>
        </article>
    );
}
