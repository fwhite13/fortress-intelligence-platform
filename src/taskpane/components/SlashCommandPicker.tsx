import React, { useEffect, useRef, useState } from 'react';

interface SlashCommand {
  name: string;
  description: string;
  prompt: string;
}

const COMMANDS: SlashCommand[] = [
  {
    name: 'report',
    description: 'Generate an analysis report sheet from selected data',
    prompt: '__REPORT_COMMAND__',
  },
  {
    name: 'audit',
    description: 'Scan for formula errors, hardcoded values, circular refs',
    prompt:
      'Please audit this spreadsheet for issues. Check for: formula errors (#REF!, #VALUE!, etc.), hardcoded numbers in formula columns, potential circular references, and inconsistent data types in columns. Report all findings with cell addresses.',
  },
  {
    name: 'clean',
    description: 'Remove duplicates, fix casing, trim whitespace',
    prompt:
      'Please analyze the selected range for data quality issues. Identify: duplicate rows, inconsistent casing (e.g. "new york" vs "New York"), leading/trailing whitespace in text cells, and blank rows/columns within the data. Return a suggestions JSON block with specific cell fixes.',
  },
  {
    name: 'summarize',
    description: "Describe this sheet's data structure and content",
    prompt:
      'Please summarize this spreadsheet. Describe: what data it contains, how many rows/columns, what each column represents, the date range if applicable, key metrics or totals visible, and any notable patterns or anomalies.',
  },
  {
    name: 'format',
    description: 'Apply investment banking style formatting',
    prompt:
      'Please apply investment banking style formatting to this spreadsheet. Return a cf_spec JSON block for the selected range that: uses blue fill (#DCE6F1) for input/hardcoded cells, leaves formula cells with no fill, adds borders, and applies number formatting for currency/percentage cells where appropriate.',
  },
];

interface SlashCommandPickerProps {
  query: string; // text after the slash (for filtering)
  onSelect: (prompt: string, name?: string) => void;
  onClose: () => void;
}

const SlashCommandPicker: React.FC<SlashCommandPickerProps> = ({ query, onSelect, onClose }) => {
  const filtered = COMMANDS.filter((c) => c.name.startsWith(query.toLowerCase()));
  const [activeIndex, setActiveIndex] = useState(0);
  const listRef = useRef<HTMLDivElement>(null);

  // Reset active index when filter changes
  useEffect(() => {
    setActiveIndex(0);
  }, [query]);

  // Keyboard navigation
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
      {/* Header */}
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
          <span
            style={{
              color: '#8899aa',
              fontSize: '11px',
              lineHeight: 1.3,
            }}
          >
            {cmd.description}
          </span>
        </div>
      ))}
    </div>
  );
};

export default SlashCommandPicker;
