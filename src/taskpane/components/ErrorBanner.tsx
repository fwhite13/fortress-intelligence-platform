import React from 'react';

interface ErrorBannerProps {
  message: string;
  onDismiss: () => void;
}

const ErrorBanner: React.FC<ErrorBannerProps> = ({ message, onDismiss }) => (
  <div
    role="alert"
    style={{
      display: 'flex',
      alignItems: 'flex-start',
      justifyContent: 'space-between',
      gap: '8px',
      padding: '8px 12px',
      background: '#2d1515',
      border: '1px solid #e74c3c',
      borderRadius: '6px',
      margin: '4px 8px',
      fontSize: '12px',
      color: '#e74c3c',
      lineHeight: 1.4,
      animation: 'fadeIn 0.2s ease-out',
    }}
  >
    <span>⚠ {message}</span>
    <button
      onClick={onDismiss}
      aria-label="Dismiss error"
      style={{
        background: 'none',
        border: 'none',
        color: '#e74c3c',
        cursor: 'pointer',
        fontSize: '14px',
        lineHeight: 1,
        flexShrink: 0,
        padding: '0 2px',
      }}
    >
      ×
    </button>
  </div>
);

export default ErrorBanner;
