import React, { useState, useEffect } from 'react';
import { getApiKey } from './services/storage';
import ChatPanel from './components/ChatPanel';
import SettingsPanel from './components/SettingsPanel';

const App: React.FC = () => {
  const [apiKey, setApiKey] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [showSettings, setShowSettings] = useState(false);

  useEffect(() => {
    getApiKey().then((key) => {
      setApiKey(key);
      setLoading(false);
    });
  }, []);

  const handleKeySet = async (key: string) => {
    if (key === '__USE_EXISTING__') {
      // User clicked "back to chat" — reload the existing key
      const existing = await getApiKey();
      setApiKey(existing);
    } else {
      setApiKey(key);
    }
    setShowSettings(false);
  };

  const handleOpenSettings = () => {
    setShowSettings(true);
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

  if (!apiKey || showSettings) {
    return <SettingsPanel onKeySet={handleKeySet} />;
  }

  return <ChatPanel apiKey={apiKey} onOpenSettings={handleOpenSettings} />;
};

export default App;
