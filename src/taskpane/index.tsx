import { createRoot } from 'react-dom/client';
import AuthGate from './components/AuthGate';
import './styles/global.css';

/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Office: any;
/* eslint-enable @typescript-eslint/no-explicit-any */

Office.onReady(() => {
  const container = document.getElementById('root');
  if (!container) throw new Error('Root element not found');
  const root = createRoot(container);
  root.render(<AuthGate />);
});
