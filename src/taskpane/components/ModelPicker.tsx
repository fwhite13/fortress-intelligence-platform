import React from 'react';

interface ModelPickerProps {
  model: 'haiku' | 'sonnet';
  onChange: (model: 'haiku' | 'sonnet') => void;
}

const ModelPicker: React.FC<ModelPickerProps> = ({ model, onChange }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
    <label
      htmlFor="model-picker"
      style={{ fontSize: '11px', color: '#8899aa', whiteSpace: 'nowrap' }}
    >
      Model:
    </label>
    <select
      id="model-picker"
      value={model}
      onChange={(e) => onChange(e.target.value as 'haiku' | 'sonnet')}
      style={{
        background: '#243447',
        border: '1px solid #2e3f54',
        borderRadius: '4px',
        color: '#e8edf3',
        fontSize: '12px',
        padding: '2px 6px',
        cursor: 'pointer',
        fontFamily: 'Inter, sans-serif',
      }}
    >
      <option value="haiku">Haiku (fast)</option>
      <option value="sonnet">Sonnet (best)</option>
    </select>
  </div>
);

export default ModelPicker;
