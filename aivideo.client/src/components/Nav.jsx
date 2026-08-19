import { Link, NavLink, useNavigate } from 'react-router-dom';
import { useAuth } from '../auth.jsx';

/** Site header: brand always goes home; the right side depends on whether you are signed in. */
export default function Nav() {
    const { isAuthenticated, user, logout } = useAuth();
    const navigate = useNavigate();

    function handleLogout() {
        logout();
        navigate('/', { replace: true });
    }

    return (
        <header className="nav">
            <Link to="/" className="brand">
                <span className="brand-mark">◇</span>
                <span className="brand-name">VoxReel</span>
            </Link>

            <nav className="nav-links">
                {isAuthenticated ? (
                    <>
                        <NavLink to="/studio" className={({ isActive }) => isActive ? 'nav-link nav-link-active' : 'nav-link'}>
                            Studio
                        </NavLink>
                        <span className="nav-user">{user?.displayName ?? user?.email}</span>
                        <button className="ghost" onClick={handleLogout}>Sign out</button>
                    </>
                ) : (
                    <>
                        <NavLink to="/login" className="nav-link">Sign in</NavLink>
                        <Link to="/register" className="primary">Get started</Link>
                    </>
                )}
            </nav>
        </header>
    );
}
