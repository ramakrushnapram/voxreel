import { currentToken } from './auth.jsx';

/**
 * Thin API client. Every call attaches the bearer token (when present) and routes errors
 * through one place: the server returns RFC7807 ProblemDetails, whose `detail` is written
 * for humans, so it is surfaced verbatim rather than replaced with a generic message.
 *
 * A 401 means the token is missing or expired. `onUnauthorized` lets the app react (clear
 * the session and bounce to login) without every caller having to check.
 */
let onUnauthorized = null;
export function setUnauthorizedHandler(fn) { onUnauthorized = fn; }

async function request(path, options = {}) {
    const token = currentToken();
    const headers = { 'Content-Type': 'application/json', ...(options.headers ?? {}) };
    if (token) headers.Authorization = `Bearer ${token}`;

    const response = await fetch(path, { ...options, headers });

    if (response.status === 401) {
        onUnauthorized?.();
        throw new Error('Your session has expired. Please sign in again.');
    }

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
    // ---- Auth ----
    register: (body) => request('/api/auth/register', { method: 'POST', body: JSON.stringify(body) }),
    login: (body) => request('/api/auth/login', { method: 'POST', body: JSON.stringify(body) }),

    // ---- System ----
    status: () => request('/api/system/status'),

    // ---- Generations ----
    listGenerations: (take = 50) => request(`/api/generations?take=${take}`),
    getGeneration: (id) => request(`/api/generations/${id}`),
    textToVideo: (body) => request('/api/generations/text-to-video', { method: 'POST', body: JSON.stringify(body) }),
    imageToVideo: (body) => request('/api/generations/image-to-video', { method: 'POST', body: JSON.stringify(body) }),
    generateImage: (body) => request('/api/generations/image', { method: 'POST', body: JSON.stringify(body) }),

    /** Multipart upload — Content-Type is omitted so the browser sets the boundary. */
    uploadImage: async (file) => {
        const form = new FormData();
        form.append('file', file);

        const token = currentToken();
        const headers = {};
        if (token) headers.Authorization = `Bearer ${token}`;

        const response = await fetch('/api/assets/upload', { method: 'POST', body: form, headers });
        if (response.status === 401) { onUnauthorized?.(); throw new Error('Your session has expired. Please sign in again.'); }

        const text = await response.text();
        const payload = text ? safeParse(text) : null;
        if (!response.ok) throw new Error(payload?.detail || payload?.title || `Upload failed (${response.status})`);
        return payload;
    },
};

export const TERMINAL_STATUSES = ['Succeeded', 'Failed', 'Cancelled'];
export const isTerminal = (status) => TERMINAL_STATUSES.includes(status);
