import React, { useState, useEffect } from 'react';
import { loadSettings } from './services/settings';
import ChatPanel from './components/ChatPanel';
import SettingsPanel from './components/SettingsPanel';

const App: React.FC = () => {
  const [apiKey, setApiKey] = useState<string>('');
  const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');
  const [kbToggles, setKbToggles] = useState<Record<string, boolean>>({ corp: true, team: false });
  const [projectId, setProjectId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [showSettings, setShowSettings] = useState(false);

  useEffect(() => {
    loadSettings().then((s) => {
      setApiKey(s.apiKey ?? '');
      setModel(s.model);
      setKbToggles(s.kbToggles);
      setProjectId(s.projectId);
      // If no API key, open settings automatically
      if (!s.apiKey) setShowSettings(true);
      setLoading(false);
    });
  }, []);

  const handleKeyChange = (key: string) => {
    setApiKey(key);
    setShowSettings(false);
  };

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
        apiKey={apiKey}
        onKeyChange={handleKeyChange}
      />
    );
  }

  return (
    <ChatPanel
      apiKey={apiKey}
      model={model}
      kbToggles={kbToggles}
      projectId={projectId}
      onOpenSettings={() => setShowSettings(true)}
    />
  );
};

export default App;
