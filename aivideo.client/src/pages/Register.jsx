import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth.jsx';

export default function Register() {
    const { login } = useAuth();
    const navigate = useNavigate();

    const [displayName, setDisplayName] = useState('');
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);
        try {
            // Registration returns a token, so a new user lands straight in the studio.
            const auth = await api.register({ displayName, email, password });
            login(auth);
            navigate('/studio', { replace: true });
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="auth-page">
            <div className="auth-card fade-up">
                <h1>Create your account</h1>
                <p className="auth-sub">Start generating in under a minute.</p>

                <form onSubmit={submit}>
                    <label>
                        <span>Name</span>
                        <input type="text" value={displayName} autoComplete="name" required minLength={2}
                            onChange={(e) => setDisplayName(e.target.value)} placeholder="Ada Lovelace" />
                    </label>
                    <label>
                        <span>Email</span>
                        <input type="email" value={email} autoComplete="email" required
                            onChange={(e) => setEmail(e.target.value)} placeholder="you@example.com" />
                    </label>
                    <label>
                        <span>Password</span>
                        <input type="password" value={password} autoComplete="new-password" required minLength={8}
                            onChange={(e) => setPassword(e.target.value)} placeholder="At least 8 characters" />
                    </label>

                    {error && <p className="error">{error}</p>}

                    <button type="submit" className="primary big full" disabled={busy}>
                        {busy ? 'Creating…' : 'Create account'}
                    </button>
                </form>

                <p className="auth-alt">
                    Already have an account? <Link to="/login">Sign in</Link>
                </p>
            </div>
        </div>
    );
}
