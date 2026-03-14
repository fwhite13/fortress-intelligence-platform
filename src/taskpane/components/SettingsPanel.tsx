import React, { useState, useEffect } from 'react';
import { getApiKey, setApiKey } from '../services/storage';
import { sendChat } from '../services/faitApi';

interface SettingsPanelProps {
  onKeySet: (key: string) => void;
}

const SettingsPanel: React.FC<SettingsPanelProps> = ({ onKeySet }) => {
  const [inputKey, setInputKey] = useState('');
  const [testing, setTesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [hasExistingKey, setHasExistingKey] = useState(false);

  useEffect(() => {
    getApiKey().then((k) => setHasExistingKey(!!k));
  }, []);

  const handleSaveAndTest = async () => {
    const trimmed = inputKey.trim();
    if (!trimmed) {
      setError('Please enter an API key');
      return;
    }

    setTesting(true);
    setError(null);

    try {
      // Test the key by sending a minimal ping
      await sendChat('ping', trimmed);
      // Success — save and switch to chat
      await setApiKey(trimmed);
      onKeySet(trimmed);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      if (msg === 'INVALID_KEY') {
        setError('Invalid API key — double-check and try again');
      } else if (msg === 'TIMEOUT') {
        setError('Connection timed out — check your network');
      } else {
        setError('FAIT service unavailable — try again later');
      }
    } finally {
      setTesting(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') handleSaveAndTest();
  };

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        background: '#1a2332',
        fontFamily: 'Inter, sans-serif',
      }}
    >
      {/* Header */}
      <div
        style={{
          padding: '16px',
          borderBottom: '1px solid #2e3f54',
          background: '#0f1720',
        }}
      >
        <div style={{ color: '#d4af37', fontWeight: '600', fontSize: '16px' }}>
          🏰 FAIT for Excel
        </div>
        <div style={{ color: '#8899aa', fontSize: '12px', marginTop: '2px' }}>
          Settings
        </div>
      </div>

      {/* Body */}
      <div style={{ flex: 1, padding: '16px', display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <div>
          <h2 style={{ color: '#e8edf3', fontSize: '14px', fontWeight: '600', marginBottom: '8px' }}>
            FAIT API Key
          </h2>
          <p style={{ color: '#8899aa', fontSize: '12px', lineHeight: 1.5, marginBottom: '12px' }}>
            Enter your FAIT API key to connect. Contact IT for your key or check
            your onboarding email.
          </p>

          <input
            type="password"
            value={inputKey}
            onChange={(e) => setInputKey(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Paste your API key here…"
            autoComplete="off"
            style={{
              width: '100%',
              background: '#243447',
              border: `1px solid ${error ? '#e74c3c' : '#2e3f54'}`,
              borderRadius: '8px',
              color: '#e8edf3',
              fontFamily: 'Inter, sans-serif',
              fontSize: '13px',
              padding: '10px 12px',
              outline: 'none',
              marginBottom: '8px',
            }}
            onFocus={(e) => { if (!error) e.target.style.borderColor = '#d4af37'; }}
            onBlur={(e) => { if (!error) e.target.style.borderColor = '#2e3f54'; }}
          />

          {error && (
            <div
              role="alert"
              style={{
                color: '#e74c3c',
                fontSize: '12px',
                marginBottom: '8px',
                padding: '6px 10px',
                background: '#2d1515',
                borderRadius: '4px',
                border: '1px solid #e74c3c',
              }}
            >
              ⚠ {error}
            </div>
          )}

          <button
            onClick={handleSaveAndTest}
            disabled={testing}
            style={{
              width: '100%',
              background: testing ? '#243447' : '#d4af37',
              border: 'none',
              borderRadius: '8px',
              color: testing ? '#8899aa' : '#1a2332',
              cursor: testing ? 'not-allowed' : 'pointer',
              fontFamily: 'Inter, sans-serif',
              fontWeight: '600',
              fontSize: '14px',
              padding: '10px',
              transition: 'background 0.15s',
            }}
          >
            {testing ? 'Testing connection…' : 'Save & Test Connection'}
          </button>
        </div>

        {hasExistingKey && (
          <div style={{ borderTop: '1px solid #2e3f54', paddingTop: '12px' }}>
            <button
              onClick={() => onKeySet('__USE_EXISTING__')}
              style={{
                background: 'none',
                border: 'none',
                color: '#d4af37',
                cursor: 'pointer',
                fontSize: '12px',
                fontFamily: 'Inter, sans-serif',
                textDecoration: 'underline',
                padding: 0,
              }}
            >
              ← Back to chat (use existing key)
            </button>
          </div>
        )}

        <div
          style={{
            borderTop: '1px solid #2e3f54',
            paddingTop: '12px',
            color: '#556677',
            fontSize: '11px',
            lineHeight: 1.5,
          }}
        >
          <p>Your API key is stored securely in OfficeRuntime.storage and never sent anywhere except FAIT.</p>
        </div>
      </div>
    </div>
  );
};

export default SettingsPanel;
