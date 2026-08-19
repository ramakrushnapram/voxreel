import { useRef, useState } from 'react';
import { api } from '../api';

/**
 * Chooses a source image either by uploading a file or by pasting a public URL.
 *
 * Both routes exist because of a real constraint rather than convenience: Pollo fetches
 * source images from its own servers, so an uploaded file is only usable when the server
 * is publicly reachable (Storage:PublicBaseUrl). When it isn't, the URL route is the only
 * one that works — so the picker says so instead of letting the user hit a confusing error.
 */
export default function ImagePicker({ value, onChange, publicBaseUrlConfigured }) {
    const [mode, setMode] = useState('upload');
    const [uploading, setUploading] = useState(false);
    const [error, setError] = useState(null);
    const fileInput = useRef(null);

    async function handleFile(event) {
        const file = event.target.files?.[0];
        if (!file) return;

        setUploading(true);
        setError(null);

        try {
            const asset = await api.uploadImage(file);
            onChange({ assetId: asset.id, imageUrl: null, previewUrl: asset.url, fileName: file.name });
        } catch (err) {
            setError(err.message);
        } finally {
            setUploading(false);
        }
    }

    function handleUrl(event) {
        const url = event.target.value.trim();
        onChange(url ? { assetId: null, imageUrl: url, previewUrl: url, fileName: null } : null);
    }

    function clear() {
        onChange(null);
        setError(null);
        if (fileInput.current) fileInput.current.value = '';
    }

    return (
        <div className="picker">
            <div className="picker-tabs">
                <button
                    type="button"
                    className={mode === 'upload' ? 'chip chip-active' : 'chip'}
                    onClick={() => setMode('upload')}
                >
                    Upload a file
                </button>
                <button
                    type="button"
                    className={mode === 'url' ? 'chip chip-active' : 'chip'}
                    onClick={() => setMode('url')}
                >
                    Paste a public URL
                </button>
            </div>

            {mode === 'upload' && (
                <>
                    <input
                        ref={fileInput}
                        type="file"
                        accept="image/png,image/jpeg,image/jpg,image/webp"
                        onChange={handleFile}
                        disabled={uploading}
                    />
                    {uploading && <p className="hint">Uploading…</p>}
                    {!publicBaseUrlConfigured && (
                        <p className="warn-inline">
                            Storage:PublicBaseUrl is not set, so Pollo cannot fetch an uploaded file.
                            Uploads are stored locally and previewed here, but generation will be
                            rejected — use a public URL, or expose this server with a tunnel.
                        </p>
                    )}
                </>
            )}

            {mode === 'url' && (
                <input
                    type="url"
                    placeholder="https://example.com/frame.png"
                    defaultValue={value?.imageUrl ?? ''}
                    onChange={handleUrl}
                />
            )}

            {error && <p className="error-inline">{error}</p>}

            {value?.previewUrl && (
                <div className="preview">
                    <img src={value.previewUrl} alt="Selected source" />
                    <div className="preview-meta">
                        <span>{value.fileName ?? 'Linked image'}</span>
                        <button type="button" className="link-button" onClick={clear}>Remove</button>
                    </div>
                </div>
            )}
        </div>
    );
}
