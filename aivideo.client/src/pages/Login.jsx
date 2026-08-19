import { useState } from 'react';
import { Link, useNavigate, useLocation } from 'react-router-dom';
import { api } from '../api';
import { useAuth } from '../auth.jsx';

export default function Login() {
    const { login } = useAuth();
    const navigate = useNavigate();
    const location = useLocation();
    // Return the user to wherever they were headed before being bounced to login.
    const from = location.state?.from ?? '/studio';

    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    async function submit(event) {
        event.preventDefault();
        setBusy(true);
        setError(null);
        try {
            const auth = await api.login({ email, password });
            login(auth);
            navigate(from, { replace: true });
        } catch (err) {
            setError(err.message);
        } finally {
            setBusy(false);
        }
    }

    return (
        <div className="auth-page">
            <div className="auth-card fade-up">
                <h1>Welcome back</h1>
                <p className="auth-sub">Sign in to your VoxReel studio.</p>

                <form onSubmit={submit}>
                    <label>
                        <span>Email</span>
                        <input type="email" value={email} autoComplete="email" required
                            onChange={(e) => setEmail(e.target.value)} placeholder="you@example.com" />
                    </label>
                    <label>
                        <span>Password</span>
                        <input type="password" value={password} autoComplete="current-password" required
                            onChange={(e) => setPassword(e.target.value)} placeholder="••••••••" />
                    </label>

                    {error && <p className="error">{error}</p>}

                    <button type="submit" className="primary big full" disabled={busy}>
                        {busy ? 'Signing in…' : 'Sign in'}
                    </button>
                </form>

                <p className="auth-alt">
                    New here? <Link to="/register">Create an account</Link>
                </p>
            </div>
        </div>
    );
}
