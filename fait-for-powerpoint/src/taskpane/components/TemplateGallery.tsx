import React from 'react';
import type { PptTemplateSpec } from '../services/pptSpecParser';

interface TemplateGalleryProps {
  spec: PptTemplateSpec;
  onInsert: (templateId: string, keepSourceFormatting: boolean) => void;
  onReject: () => void;
  loading?: boolean;
}

const TemplateGallery: React.FC<TemplateGalleryProps> = ({
  spec,
  onInsert,
  onReject,
  loading = false,
}) => (
  <div
    style={{
      padding: '10px 12px',
      borderTop: '1px solid #2e3f54',
      background: '#0f1720',
      flexShrink: 0,
      display: 'flex',
      flexDirection: 'column',
      gap: '8px',
    }}
  >
    <div style={{ fontSize: '11px', fontWeight: '600', color: '#d4af37' }}>
      🗂 Slide Templates from FORGE
    </div>
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: '6px',
        maxHeight: '200px',
        overflowY: 'auto',
      }}
    >
      {spec.templates.map((t) => (
        <div
          key={t.id}
          style={{
            background: '#131e2b',
            border: '1px solid #2e3f54',
            borderRadius: '4px',
            padding: '8px 10px',
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'flex-start',
            gap: '8px',
          }}
        >
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: '12px', fontWeight: '600', color: '#e8edf3' }}>
              {t.name}
            </div>
            <div style={{ fontSize: '11px', color: '#778899', marginTop: '2px' }}>
              {t.description}
            </div>
          </div>
          <button
            onClick={() => onInsert(t.id, t.keepSourceFormatting)}
            disabled={loading}
            style={{
              background: '#d4af37',
              color: '#0f1720',
              border: 'none',
              borderRadius: '3px',
              padding: '4px 10px',
              fontSize: '11px',
              fontWeight: '700',
              cursor: loading ? 'not-allowed' : 'pointer',
              opacity: loading ? 0.6 : 1,
              flexShrink: 0,
            }}
          >
            {loading ? '…' : '+ Insert'}
          </button>
        </div>
      ))}
    </div>
    <button
      onClick={onReject}
      style={{
        background: '#2e3f54',
        color: '#e8edf3',
        border: 'none',
        borderRadius: '4px',
        padding: '5px 10px',
        fontSize: '11px',
        cursor: 'pointer',
        alignSelf: 'flex-start',
      }}
    >
      Cancel
    </button>
  </div>
);

export default TemplateGallery;
