import React, { useEffect, useRef, useState } from 'react';

interface SlashCommand {
  name: string;
  description: string;
  prompt: string;
}

const COMMANDS: SlashCommand[] = [
  {
    name: 'summarize',
    description: 'Summarize the current slide content',
    prompt: 'Please summarize the content of this slide. Describe what it covers, key points, and any notable data or claims.',
  },
  {
    name: 'improve',
    description: 'Suggest improvements for the selected shape text',
    prompt: 'Please review the selected shape text and suggest improvements. Focus on clarity, conciseness, and impact.',
  },
  {
    name: 'bullets',
    description: 'Convert selected shape text to bullet points',
    prompt: 'Please convert the selected shape text into clear, concise bullet points. Apply to shape when ready.',
  },
  {
    name: 'expand',
    description: 'Expand the selected shape text with more detail',
    prompt: 'Please expand the selected shape text with more detail and supporting context. Apply to shape when ready.',
  },
];

interface SlashCommandPickerProps {
  query: string;
  onSelect: (prompt: string, name?: string) => void;
  onClose: () => void;
}

const SlashCommandPicker: React.FC<SlashCommandPickerProps> = ({ query, onSelect, onClose }) => {
  const filtered = COMMANDS.filter((c) => c.name.startsWith(query.toLowerCase()));
  const [activeIndex, setActiveIndex] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    setActiveIndex(0);
  }, [query]);

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (filtered.length === 0) return;
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setActiveIndex((i) => (i + 1) % filtered.length);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setActiveIndex((i) => (i - 1 + filtered.length) % filtered.length);
      } else if (e.key === 'Enter') {
        e.preventDefault();
        onSelect(filtered[activeIndex].prompt, filtered[activeIndex].name);
      } else if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener('keydown', handleKey);
    return () => window.removeEventListener('keydown', handleKey);
  }, [filtered, activeIndex, onSelect, onClose]);

  if (filtered.length === 0) return null;

  return (
    <div
      ref={listRef}
      role="listbox"
      aria-label="Slash commands"
      style={{
        position: 'absolute',
        bottom: '100%',
        left: 0,
        right: 0,
        background: '#0f1720',
        border: '1px solid #2e3f54',
        borderRadius: '8px',
        boxShadow: '0 -4px 16px rgba(0,0,0,0.4)',
        overflow: 'hidden',
        zIndex: 1000,
        marginBottom: '4px',
      }}
    >
      <div
        style={{
          padding: '6px 10px',
          borderBottom: '1px solid #2e3f54',
          fontSize: '10px',
          color: '#556677',
          letterSpacing: '0.08em',
          textTransform: 'uppercase',
          fontWeight: '600',
        }}
      >
        Commands
      </div>

      {filtered.map((cmd, idx) => (
        <div
          key={cmd.name}
          role="option"
          aria-selected={idx === activeIndex}
          onClick={() => onSelect(cmd.prompt, cmd.name)}
          onMouseEnter={() => setActiveIndex(idx)}
          style={{
            padding: '8px 12px',
            cursor: 'pointer',
            background: idx === activeIndex ? '#1a2e45' : 'transparent',
            borderBottom: idx < filtered.length - 1 ? '1px solid #1a2332' : 'none',
            display: 'flex',
            flexDirection: 'column',
            gap: '2px',
            transition: 'background 0.1s',
          }}
        >
          <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
            <span
              style={{
                color: '#d4af37',
                fontWeight: '600',
                fontSize: '13px',
                fontFamily: 'monospace',
              }}
            >
              /{cmd.name}
            </span>
          </div>
          <span style={{ color: '#8899aa', fontSize: '11px', lineHeight: 1.3 }}>
            {cmd.description}
          </span>
        </div>
      ))}
    </div>
  );
};

export default SlashCommandPicker;
