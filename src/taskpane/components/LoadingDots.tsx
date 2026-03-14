import React from 'react';

const dotStyle = (delay: string): React.CSSProperties => ({
  display: 'inline-block',
  width: '6px',
  height: '6px',
  borderRadius: '50%',
  backgroundColor: '#d4af37',
  margin: '0 2px',
  animation: 'bounce 1.4s infinite ease-in-out',
  animationDelay: delay,
});

const LoadingDots: React.FC = () => (
  <div style={{
    display: 'flex',
    alignItems: 'center',
    gap: '6px',
    padding: '8px 12px',
    color: '#8899aa',
    fontSize: '12px',
    fontStyle: 'italic',
  }}>
    <span>FAIT is thinking</span>
    <span style={dotStyle('0s')} />
    <span style={dotStyle('0.2s')} />
    <span style={dotStyle('0.4s')} />
  </div>
);

export default LoadingDots;
