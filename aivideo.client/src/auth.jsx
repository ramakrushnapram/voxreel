import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';

const STORAGE_KEY = 'voxreel.auth';
const AuthContext = createContext(null);

/**
 * Session state, persisted to localStorage so a refresh keeps you signed in.
 *
 * The token is the single source of truth: it is attached to every API call by
 * `authFetch`, and on load we call /api/auth/me to confirm it is still valid before
 * trusting the cached user — an expired token should log you out, not silently 401
 * every request.
 */
export function AuthProvider({ children }) {
    const [session, setSession] = useState(() => read());
    const [ready, setReady] = useState(false);

    // Validate a persisted token once on load.
    useEffect(() => {
        let cancelled = false;

        async function verify() {
            if (!session?.token) {
                setReady(true);
                return;
            }

            try {
                const res = await fetch('/api/auth/me', {
                    headers: { Authorization: `Bearer ${session.token}` },
                });
                if (!res.ok) throw new Error('stale');
                const user = await res.json();
                if (!cancelled) setSession((s) => ({ ...s, user }));
            } catch {
                if (!cancelled) clear();
            } finally {
                if (!cancelled) setReady(true);
            }
        }

        verify();
        return () => { cancelled = true; };
        // Intentionally runs once: re-validating on every session change would loop.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, []);

    const persist = useCallback((value) => {
        setSession(value);
        if (value) localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
        else localStorage.removeItem(STORAGE_KEY);
    }, []);

    const login = useCallback((auth) => {
        persist({ token: auth.token, expiresUtc: auth.expiresUtc, user: auth.user });
    }, [persist]);

    const logout = useCallback(() => persist(null), [persist]);

    const value = useMemo(() => ({
        ready,
        token: session?.token ?? null,
        user: session?.user ?? null,
        isAuthenticated: Boolean(session?.token),
        login,
        logout,
    }), [ready, session, login, logout]);

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
    const ctx = useContext(AuthContext);
    if (!ctx) throw new Error('useAuth must be used within <AuthProvider>');
    return ctx;
}

function read() {
    try {
        const raw = localStorage.getItem(STORAGE_KEY);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;
    }
}

function clear() {
    localStorage.removeItem(STORAGE_KEY);
}

/** Current token, read straight from storage. Lets non-React modules (api.js) attach auth. */
export function currentToken() {
    return read()?.token ?? null;
}
