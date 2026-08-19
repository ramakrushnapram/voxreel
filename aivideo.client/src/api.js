/**
 * Thin API client. Every call goes through `request` so error handling stays in one place:
 * the server returns RFC7807 ProblemDetails on failure, and its `detail` is written for
 * humans (it explains, for example, exactly why an uploaded image cannot reach Pollo).
 * Surfacing that text verbatim is far more useful than a generic "request failed".
 */
async function request(path, options = {}) {
    const response = await fetch(path, {
        headers: { 'Content-Type': 'application/json', ...(options.headers ?? {}) },
        ...options,
    });

    const text = await response.text();
    const payload = text ? safeParse(text) : null;

    if (!response.ok) {
        const detail = payload?.detail || payload?.title || text || `Request failed (${response.status})`;
        throw new Error(detail);
    }

    return payload;
}

function safeParse(text) {
    try {
        return JSON.parse(text);
    } catch {
        return null;
    }
}

export const api = {
    status: () => request('/api/system/status'),

    listGenerations: (take = 50) => request(`/api/generations?take=${take}`),

    getGeneration: (id) => request(`/api/generations/${id}`),

    textToVideo: (body) =>
        request('/api/generations/text-to-video', { method: 'POST', body: JSON.stringify(body) }),

    imageToVideo: (body) =>
        request('/api/generations/image-to-video', { method: 'POST', body: JSON.stringify(body) }),

    generateImage: (body) =>
        request('/api/generations/image', { method: 'POST', body: JSON.stringify(body) }),

    /** Multipart upload — the Content-Type header is omitted so the browser sets the boundary. */
    uploadImage: async (file) => {
        const form = new FormData();
        form.append('file', file);

        const response = await fetch('/api/assets/upload', { method: 'POST', body: form });
        const text = await response.text();
        const payload = text ? safeParse(text) : null;

        if (!response.ok) {
            throw new Error(payload?.detail || payload?.title || `Upload failed (${response.status})`);
        }

        return payload;
    },
};

/** Statuses the server will not move away from — polling can stop here. */
export const TERMINAL_STATUSES = ['Succeeded', 'Failed', 'Cancelled'];

export const isTerminal = (status) => TERMINAL_STATUSES.includes(status);
