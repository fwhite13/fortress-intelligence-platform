/** FAIT color tokens — single source of truth */
export const theme = {
  colors: {
    // Backgrounds
    bgPrimary: '#1a2332',    // Navy — main background
    bgSecondary: '#0f1720',  // Darker navy — message area
    bgElevated: '#243447',   // Slightly lighter — cards, input
    bgHover: '#2e3f54',      // Hover state

    // Accents
    gold: '#d4af37',         // FAIT gold — assistant label, highlights
    goldDim: '#a88a28',      // Dimmed gold — secondary actions

    // Text
    textPrimary: '#e8edf3',  // Near-white — primary content
    textSecondary: '#8899aa', // Muted — timestamps, labels
    textMuted: '#556677',    // Very muted — placeholders

    // Semantic
    error: '#e74c3c',        // Error red
    errorBg: '#2d1515',      // Error background
    warning: '#f39c12',      // Warning orange
    success: '#27ae60',      // Success green

    // Borders
    border: '#2e3f54',
    borderFocus: '#d4af37',
  },

  spacing: {
    xs: '4px',
    sm: '8px',
    md: '12px',
    lg: '16px',
    xl: '24px',
  },

  radius: {
    sm: '4px',
    md: '8px',
    lg: '12px',
    xl: '16px',
  },

  font: {
    family: "'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif",
    sizeXs: '11px',
    sizeSm: '12px',
    sizeMd: '14px',
    sizeLg: '16px',
    weightNormal: '400',
    weightMedium: '500',
    weightSemiBold: '600',
  },
} as const;

export type Theme = typeof theme;
