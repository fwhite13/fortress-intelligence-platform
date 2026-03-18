import React, { useState, useEffect } from 'react';
import { loadSettings } from './services/settings';
import { getAuthHeader } from './services/authService';
import type { FaitUser } from './services/authService';
import ChatPanel from './components/ChatPanel';
import SettingsPanel from './components/SettingsPanel';

interface AppProps {
  user: FaitUser;
}

const App: React.FC<AppProps> = ({ user }) => {
  const [authHeader, setAuthHeader] = useState<Record<string, string>>({});
  const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');
  const [kbToggles, setKbToggles] = useState<Record<string, boolean>>({ corp: true, team: false });
  const [projectId, setProjectId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [showSettings, setShowSettings] = useState(false);

  useEffect(() => {
    Promise.all([loadSettings(), getAuthHeader()]).then(([s, hdr]) => {
      setAuthHeader(hdr);
      setModel(s.model);
      setKbToggles(s.kbToggles);
      setProjectId(s.projectId);
      setLoading(false);
    });
  }, []);

  if (loading) {
    return (
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          height: '100vh',
          background: '#1a2332',
        }}
      >
        <div style={{ color: '#d4af37', fontFamily: 'Inter, sans-serif', fontSize: '14px' }}>
          Loading FAIT…
        </div>
      </div>
    );
  }

  if (showSettings) {
    return (
      <SettingsPanel
        onClose={() => setShowSettings(false)}
        user={user}
      />
    );
  }

  return (
    <ChatPanel
      authHeader={authHeader}
      model={model}
      kbToggles={kbToggles}
      projectId={projectId}
      onOpenSettings={() => setShowSettings(true)}
    />
  );
};

export default App;
