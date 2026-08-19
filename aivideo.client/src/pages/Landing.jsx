import { Link } from 'react-router-dom';
import { useAuth } from '../auth.jsx';

const FEATURES = [
    { icon: '🖼️', title: 'Image Studio', body: 'Generate stills from a prompt, or upload one and describe the change. Powered by Nano Banana Pro and friends.' },
    { icon: '🎬', title: 'Animate', body: 'Turn any still into motion. Describe the camera move and let the model bring it to life.' },
    { icon: '⚡', title: 'Quick Clip', body: 'A prompt straight to video. Pick a hero model for the money shots, B-roll for everything else.' },
    { icon: '🎙️', title: 'Long-form (coming)', body: 'Script → scenes → narration → a full 10–30 minute video, assembled and ready for YouTube.' },
];

/** Marketing home. The primary call to action changes once you are signed in. */
export default function Landing() {
    const { isAuthenticated } = useAuth();

    return (
        <div className="landing">
            <section className="hero">
                <span className="eyebrow">AI Film &amp; Animation Studio</span>
                <h1>Make movies and animation with AI.</h1>
                <p className="lede">
                    VoxReel turns an idea into a finished film. Generate images, animate them into scenes,
                    and stitch everything into a full narrated video — from a single prompt to a movie,
                    all in one place.
                </p>
                <div className="hero-cta">
                    {isAuthenticated ? (
                        <Link to="/studio" className="primary big">Open the studio</Link>
                    ) : (
                        <>
                            <Link to="/register" className="primary big">Get started free</Link>
                            <Link to="/login" className="ghost big">Sign in</Link>
                        </>
                    )}
                </div>
                <p className="hero-note">No credit card to sign up. Bring your own Pollo API key to generate.</p>
            </section>

            <section className="features">
                {FEATURES.map((f) => (
                    <div className="feature" key={f.title}>
                        <div className="feature-icon">{f.icon}</div>
                        <h3>{f.title}</h3>
                        <p>{f.body}</p>
                    </div>
                ))}
            </section>

            <section className="strip">
                <div>
                    <h2>Built for real movies, not just clips</h2>
                    <p>
                        A single AI clip maxes out at 15 seconds. VoxReel is designed around that: it
                        scripts, plans scenes, narrates, and stitches many clips into one continuous
                        film — the way real long-form video is actually made.
                    </p>
                </div>
                {!isAuthenticated && (
                    <Link to="/register" className="primary big">Create your account</Link>
                )}
            </section>
        </div>
    );
}
