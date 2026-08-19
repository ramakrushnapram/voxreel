import { useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useNavigate, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth.jsx';
import { setUnauthorizedHandler } from './api';
import Nav from './components/Nav';
import Landing from './pages/Landing';
import Login from './pages/Login';
import Register from './pages/Register';
import Studio from './pages/Studio';
import './App.css';

export default function App() {
    return (
        <BrowserRouter>
            <AuthProvider>
                <Shell />
            </AuthProvider>
        </BrowserRouter>
    );
}

function Shell() {
    const { ready, logout } = useAuth();
    const navigate = useNavigate();

    // A 401 from anywhere clears the session and returns to login. Registered once, here,
    // so no individual component has to handle expiry.
    useEffect(() => {
        setUnauthorizedHandler(() => {
            logout();
            navigate('/login', { replace: true });
        });
        return () => setUnauthorizedHandler(null);
    }, [logout, navigate]);

    // Hold rendering until the persisted token has been validated, so a protected route does
    // not flash the login page before /api/auth/me confirms an existing session.
    if (!ready) {
        return <div className="boot"><div className="spinner" /></div>;
    }

    return (
        <>
            <Nav />
            <main className="page">
                <Routes>
                    <Route path="/" element={<Landing />} />
                    <Route path="/login" element={<Login />} />
                    <Route path="/register" element={<Register />} />
                    <Route path="/studio" element={<RequireAuth><Studio /></RequireAuth>} />
                    <Route path="*" element={<Navigate to="/" replace />} />
                </Routes>
            </main>
            <footer className="site-foot">
                <span>VoxReel · AI video studio</span>
                <span>Created by Ramakrishna</span>
            </footer>
        </>
    );
}

/** Gate for authenticated routes; remembers where you were headed so login can return you there. */
function RequireAuth({ children }) {
    const { isAuthenticated } = useAuth();
    const location = useLocation();

    if (!isAuthenticated) {
        return <Navigate to="/login" replace state={{ from: location.pathname }} />;
    }
    return children;
}
