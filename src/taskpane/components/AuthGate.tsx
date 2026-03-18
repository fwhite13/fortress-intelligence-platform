// Wraps App. Shows sign-in UI if not authenticated. Shows App once signed in.

import React, { useState, useEffect } from 'react';
import { getStoredToken, getStoredUser, signIn, FaitUser } from '../services/authService';
import App from '../App';

const AuthGate: React.FC = () => {
  const [checking, setChecking]   = useState(true);
  const [user, setUser]           = useState<FaitUser | null>(null);
  const [signingIn, setSigningIn] = useState(false);
  const [error, setError]         = useState<string | null>(null);

  useEffect(() => {
    (async () => {
      const token = await getStoredToken();
      if (token) {
        const storedUser = await getStoredUser();
        setUser(storedUser);
      }
      setChecking(false);
    })();
  }, []);

  const handleSignIn = async () => {
    setSigningIn(true);
    setError(null);
    const result = await signIn();
    setSigningIn(false);
    if (result.success && result.user) {
      setUser(result.user);
    } else {
      setError(result.error ?? 'Sign-in failed');
    }
  };

  if (checking) {
    return (
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center',
                    height: '100vh', background: '#1a2332' }}>
        <span style={{ color: '#d4af37', fontFamily: 'Inter, sans-serif', fontSize: '14px' }}>
          Loading FAIT…
        </span>
      </div>
    );
  }

  if (!user) {
    return (
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center',
                    justifyContent: 'center', height: '100vh', background: '#1a2332',
                    fontFamily: 'Inter, sans-serif', padding: '24px', gap: '16px' }}>
        <div style={{ color: '#d4af37', fontSize: '20px', fontWeight: 600 }}>FAIT</div>
        <div style={{ color: 'rgba(248,250,252,0.7)', fontSize: '13px', textAlign: 'center' }}>
          Sign in with your Fortress AM account to continue.
        </div>
        {error && (
          <div style={{ color: '#f87171', fontSize: '12px', textAlign: 'center' }}>{error}</div>
        )}
        <button
          onClick={handleSignIn}
          disabled={signingIn}
          style={{ background: '#d4af37', color: '#1a2332', border: 'none', borderRadius: '6px',
                   padding: '10px 24px', fontWeight: 600, fontSize: '14px', cursor: 'pointer',
                   opacity: signingIn ? 0.7 : 1 }}>
          {signingIn ? 'Opening sign-in…' : 'Sign in with Microsoft'}
        </button>
      </div>
    );
  }

  return <App user={user} />;
};

export default AuthGate;
