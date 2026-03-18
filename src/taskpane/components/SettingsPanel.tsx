import React, { useState, useEffect } from 'react';
import { setApiKey } from '../services/storage';
import { sendChat, fetchKbList, fetchProjectList } from '../services/faitApi';
import { getAuthHeader, clearAuth } from '../services/authService';
import type { FaitUser } from '../services/authService';
import { saveSetting } from '../services/settings';
import ModelPicker from './ModelPicker';
import type { KbInfo, ProjectInfo } from '../services/faitApi';
import { loadNamedRanges, syncRegistry, removeNamedRange, renameNamedRange } from '../services/namedRangeStorage';
import { deleteNamedRange, renameWorkbookNamedRange, listWorkbookNamedRanges } from '../services/excelWriter';
import type { FaitNamedRange } from '../services/namedRangeStorage';

interface SettingsPanelProps {
  onClose: () => void;
  user?: FaitUser;
  // Sprint 8: Named ranges — optional props, panel loads its own data if not provided
  namedRanges?: FaitNamedRange[];
  onDeleteNamedRange?: (name: string) => Promise<void>;
  onRenameNamedRange?: (oldName: string, newName: string) => Promise<void>;
}

const SettingsPanel: React.FC<SettingsPanelProps> = ({
  onClose,
  user,
  namedRanges: namedRangesProp,
  onDeleteNamedRange,
  onRenameNamedRange,
}) => {
  // ── Advanced: AppKey section ─────────────────────────────────────────────────
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [inputKey, setInputKey] = useState('');
  const [testing, setTesting] = useState(false);
  const [keyError, setKeyError] = useState<string | null>(null);
  const [keySuccess, setKeySuccess] = useState(false);

  // ── KB toggles section ──────────────────────────────────────────────────────
  const [kbList, setKbList] = useState<KbInfo[]>([]);
  const [kbToggles, setKbToggles] = useState<Record<string, boolean>>({});
  const [kbLoading, setKbLoading] = useState(false);

  // ── Projects section ────────────────────────────────────────────────────────
  const [projects, setProjects] = useState<ProjectInfo[]>([]);
  const [selectedProject, setSelectedProject] = useState<string>('');
  const [projectsLoading, setProjectsLoading] = useState(false);

  // ── Model section ───────────────────────────────────────────────────────────
  const [model, setModel] = useState<'haiku' | 'sonnet'>('sonnet');

  // ── Sprint 8: Named Ranges section ────────────────────────────────────────
  const [localNamedRanges, setLocalNamedRanges] = useState<FaitNamedRange[]>(namedRangesProp ?? []);
  const [renamingName, setRenamingName] = useState<string | null>(null);
  const [renameValue, setRenameValue] = useState('');
  const [renameLoading, setRenameLoading] = useState(false);
  const [rangeActionError, setRangeActionError] = useState<string | null>(null);

  // Derive which list to show: prop-provided or locally loaded
  const displayedRanges = namedRangesProp ?? localNamedRanges;

  // ── Load persisted values on mount ─────────────────────────────────────────
  useEffect(() => {
    /* eslint-disable @typescript-eslint/no-explicit-any */
    const storage = (window as any).OfficeRuntime?.storage;
    /* eslint-enable @typescript-eslint/no-explicit-any */
    if (!storage) return;

    Promise.all([
      storage.getItem('fait_model').catch(() => null),
      storage.getItem('fait_project_id').catch(() => null),
      storage.getItem('fait_kb_corp').catch(() => null),
      storage.getItem('fait_kb_team').catch(() => null),
    ]).then(([storedModel, storedProject, corpToggle, teamToggle]) => {
      if (storedModel === 'haiku' || storedModel === 'sonnet') setModel(storedModel);
      if (storedProject) setSelectedProject(storedProject);
      setKbToggles({
        corp: corpToggle !== 'false',
        team: teamToggle === 'true',
      });
    });
  }, []);

  // ── Fetch KB list + projects using current auth header ─────────────────────
  useEffect(() => {
    let cancelled = false;
    getAuthHeader().then((hdr) => {
      if (cancelled) return;
      if (!Object.keys(hdr).length) return;

      setKbLoading(true);
      fetchKbList(hdr)
        .then((list) => {
          if (cancelled) return;
          setKbList(list);
          setKbToggles((prev) => {
            const next = { ...prev };
            for (const kb of list) {
              if (!(kb.id in next)) {
                next[kb.id] = kb.alwaysOn || kb.type === 'corp';
              }
            }
            return next;
          });
        })
        .finally(() => { if (!cancelled) setKbLoading(false); });

      setProjectsLoading(true);
      fetchProjectList(hdr)
        .then((list) => { if (!cancelled) setProjects(list); })
        .finally(() => { if (!cancelled) setProjectsLoading(false); });
    });
    return () => { cancelled = true; };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Sprint 8: Load named ranges and sync with workbook on panel open ──────
  useEffect(() => {
    loadNamedRanges().then(async (ranges) => {
      if (!namedRangesProp) {
        setLocalNamedRanges(ranges);
      }
      const liveNames = await listWorkbookNamedRanges();
      if (liveNames.length > 0) {
        await syncRegistry(liveNames);
        const cleaned = await loadNamedRanges();
        if (!namedRangesProp) {
          setLocalNamedRanges(cleaned);
        }
      }
    }).catch(() => null);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  const handleDeleteRange = async (name: string) => {
    setRangeActionError(null);
    try {
      if (onDeleteNamedRange) {
        await onDeleteNamedRange(name);
      } else {
        await deleteNamedRange(name);
        await removeNamedRange(name);
        setLocalNamedRanges((prev) => prev.filter((r) => r.name !== name));
      }
    } catch {
      setRangeActionError('Delete failed.');
    }
  };

  const handleRenameRange = async (oldName: string, newName: string) => {
    setRenameLoading(true);
    setRangeActionError(null);
    try {
      if (onRenameNamedRange) {
        await onRenameNamedRange(oldName, newName);
      } else {
        await renameWorkbookNamedRange(oldName, newName);
        await renameNamedRange(oldName, newName);
        setLocalNamedRanges((prev) =>
          prev.map((r) => (r.name === oldName ? { ...r, name: newName } : r))
        );
      }
      setRenamingName(null);
    } catch {
      setRangeActionError('Rename failed — name may already exist or be invalid.');
    } finally {
      setRenameLoading(false);
    }
  };

  // ── Handlers ────────────────────────────────────────────────────────────────
  const handleSaveAndTest = async () => {
    const trimmed = inputKey.trim();
    if (!trimmed) {
      setKeyError('Please enter an API key');
      return;
    }
    setTesting(true);
    setKeyError(null);
    setKeySuccess(false);
    try {
      await sendChat('ping', { 'x-api-key': trimmed });
      await setApiKey(trimmed);
      setKeySuccess(true);
    } catch (e) {
      const msg = e instanceof Error ? e.message : 'Unknown error';
      if (msg === 'INVALID_KEY') {
        setKeyError('Invalid API key — double-check and try again');
      } else if (msg === 'TIMEOUT') {
        setKeyError('Connection timed out — check your network');
      } else {
        setKeyError('FAIT service unavailable — try again later');
      }
    } finally {
      setTesting(false);
    }
  };

  const handleSignOut = async () => {
    await clearAuth();
    window.location.reload();
  };

  const handleKbToggle = async (id: string, value: boolean) => {
    setKbToggles((prev) => ({ ...prev, [id]: value }));
    await saveSetting(`fait_kb_${id}`, String(value)).catch(() => null);
  };

  const handleProjectChange = async (id: string) => {
    setSelectedProject(id);
    await saveSetting('fait_project_id', id).catch(() => null);
  };

  const handleModelChange = async (m: 'haiku' | 'sonnet') => {
    setModel(m);
    await saveSetting('fait_model', m).catch(() => null);
  };

  // ── Styles ──────────────────────────────────────────────────────────────────
  const sectionStyle: React.CSSProperties = {
    background: '#1e2d3e',
    borderRadius: '8px',
    padding: '14px 16px',
    display: 'flex',
    flexDirection: 'column',
    gap: '10px',
  };

  const sectionHeadingStyle: React.CSSProperties = {
    color: '#d4af37',
    fontSize: '12px',
    fontWeight: '700',
    letterSpacing: '0.06em',
    textTransform: 'uppercase',
    marginBottom: '2px',
  };

  const labelStyle: React.CSSProperties = {
    color: '#8899aa',
    fontSize: '12px',
    lineHeight: 1.4,
  };

  const toggleRowStyle: React.CSSProperties = {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: '6px 0',
    borderBottom: '1px solid #2e3f54',
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
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '10px 12px',
          borderBottom: '1px solid #2e3f54',
          background: '#0f1720',
          flexShrink: 0,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <span style={{ color: '#d4af37', fontWeight: '700', fontSize: '14px' }}>🏰 FAIT</span>
          <span style={{ color: '#556677', fontSize: '11px' }}>Settings</span>
        </div>
        <button
          onClick={onClose}
          title="Back to chat"
          aria-label="Back to chat"
          style={{
            background: 'none',
            border: 'none',
            color: '#8899aa',
            cursor: 'pointer',
            fontSize: '13px',
            padding: '2px 6px',
            borderRadius: '4px',
            fontFamily: 'Inter, sans-serif',
          }}
        >
          ← Chat
        </button>
      </div>

      {/* Scrollable body */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          padding: '12px',
          display: 'flex',
          flexDirection: 'column',
          gap: '12px',
        }}
      >
        {/* ── Section: Account ─────────────────────────────── */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Account</div>
          {user ? (
            <>
              <div style={{ color: '#e8edf3', fontSize: '13px' }}>
                Signed in as <span style={{ color: '#d4af37', fontWeight: 600 }}>{user.name}</span>
              </div>
              <div style={{ color: '#556677', fontSize: '11px' }}>{user.email}</div>
              <button
                onClick={handleSignOut}
                style={{
                  background: '#2d1515',
                  border: '1px solid #4a1515',
                  borderRadius: '6px',
                  color: '#e07070',
                  cursor: 'pointer',
                  fontFamily: 'Inter, sans-serif',
                  fontWeight: '600',
                  fontSize: '12px',
                  padding: '7px 12px',
                  alignSelf: 'flex-start',
                }}
              >
                Sign out
              </button>
            </>
          ) : (
            <div style={{ color: '#556677', fontSize: '12px' }}>Not signed in via Entra.</div>
          )}
        </div>

        {/* ── Section: Knowledge Bases ─────────────────────── */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Knowledge Bases</div>

          {kbLoading && (
            <div style={{ color: '#556677', fontSize: '12px' }}>Loading…</div>
          )}

          {!kbLoading && kbList.length === 0 && (
            <div style={{ color: '#556677', fontSize: '12px' }}>
              No knowledge bases configured.
            </div>
          )}

          {kbList.map((kb, idx) => (
            <div
              key={kb.id}
              style={{
                ...toggleRowStyle,
                borderBottom: idx < kbList.length - 1 ? '1px solid #2e3f54' : 'none',
              }}
            >
              <div>
                <div style={{ color: '#e8edf3', fontSize: '13px', fontWeight: '500' }}>
                  {kb.name}
                </div>
                {kb.alwaysOn && (
                  <div style={{ color: '#556677', fontSize: '11px' }}>Always on</div>
                )}
              </div>
              {/* Toggle switch */}
              <button
                role="switch"
                aria-checked={kb.alwaysOn ? true : (kbToggles[kb.id] ?? false)}
                disabled={kb.alwaysOn}
                onClick={() => !kb.alwaysOn && handleKbToggle(kb.id, !(kbToggles[kb.id] ?? false))}
                title={kb.alwaysOn ? 'Always enabled' : (kbToggles[kb.id] ? 'Disable' : 'Enable')}
                style={{
                  width: '36px',
                  height: '20px',
                  borderRadius: '10px',
                  border: 'none',
                  cursor: kb.alwaysOn ? 'default' : 'pointer',
                  background: (kb.alwaysOn || kbToggles[kb.id]) ? '#d4af37' : '#2e3f54',
                  position: 'relative',
                  flexShrink: 0,
                  opacity: kb.alwaysOn ? 0.7 : 1,
                  transition: 'background 0.2s',
                  padding: 0,
                }}
              >
                <span
                  style={{
                    display: 'block',
                    width: '14px',
                    height: '14px',
                    borderRadius: '50%',
                    background: '#fff',
                    position: 'absolute',
                    top: '3px',
                    left: (kb.alwaysOn || kbToggles[kb.id]) ? '19px' : '3px',
                    transition: 'left 0.2s',
                  }}
                />
              </button>
            </div>
          ))}
        </div>

        {/* ── Section: Active Project ──────────────────────── */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Active Project</div>
          <p style={labelStyle}>
            Select a project to include its knowledge base in searches.
          </p>

          {projectsLoading ? (
            <div style={{ color: '#556677', fontSize: '12px' }}>Loading projects…</div>
          ) : (
            <select
              value={selectedProject}
              onChange={(e) => handleProjectChange(e.target.value)}
              style={{
                width: '100%',
                background: '#243447',
                border: '1px solid #2e3f54',
                borderRadius: '6px',
                color: '#e8edf3',
                fontFamily: 'Inter, sans-serif',
                fontSize: '13px',
                padding: '8px 10px',
                cursor: 'pointer',
                outline: 'none',
              }}
              onFocus={(e) => { e.target.style.borderColor = '#d4af37'; }}
              onBlur={(e) => { e.target.style.borderColor = '#2e3f54'; }}
            >
              <option value="">— None —</option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          )}
        </div>

        {/* ── Section: Model ───────────────────────────────── */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Model</div>
          <p style={labelStyle}>
            Sonnet is more capable; Haiku is faster and cheaper.
          </p>
          <ModelPicker model={model} onChange={handleModelChange} />
        </div>

        {/* ── Section: Named Ranges ─────────────────────────────────────── */}
        <div style={sectionStyle}>
          <div style={sectionHeadingStyle}>Named Ranges</div>
          <p style={labelStyle}>
            Ranges FAIT has written to this workbook. Reference them by name in your prompts.
          </p>

          {rangeActionError && (
            <div style={{ color: '#e07070', fontSize: '11px', padding: '4px 0' }}>
              {rangeActionError}
            </div>
          )}

          {displayedRanges.length === 0 ? (
            <p style={{ ...labelStyle, color: '#445566' }}>
              No named ranges yet. After writing a table to the sheet, FAIT will offer to name the range.
            </p>
          ) : (
            displayedRanges.map((r, idx) => (
              <div
                key={r.name}
                style={{
                  ...toggleRowStyle,
                  borderBottom: idx < displayedRanges.length - 1 ? '1px solid #2e3f54' : 'none',
                  flexDirection: 'column' as const,
                  alignItems: 'flex-start' as const,
                  gap: '6px',
                  padding: '8px 0',
                }}
              >
                {renamingName === r.name ? (
                  <div style={{ width: '100%', display: 'flex', gap: '6px' }}>
                    <input
                      value={renameValue}
                      onChange={(e) => setRenameValue(e.target.value)}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') void handleRenameRange(r.name, renameValue.trim());
                        if (e.key === 'Escape') setRenamingName(null);
                      }}
                      autoFocus
                      style={{
                        flex: 1,
                        background: '#1a2332',
                        border: '1px solid #2e3f54',
                        borderRadius: '4px',
                        color: '#e8edf3',
                        padding: '4px 8px',
                        fontSize: '12px',
                        outline: 'none',
                      }}
                    />
                    <button
                      disabled={renameLoading}
                      onClick={() => void handleRenameRange(r.name, renameValue.trim())}
                      style={{
                        background: '#d4af37',
                        color: '#0f1720',
                        border: 'none',
                        borderRadius: '4px',
                        padding: '4px 8px',
                        fontSize: '11px',
                        fontWeight: '600',
                        cursor: 'pointer',
                      }}
                    >
                      {renameLoading ? '…' : 'OK'}
                    </button>
                    <button
                      onClick={() => setRenamingName(null)}
                      style={{
                        background: '#2e3f54',
                        color: '#e8edf3',
                        border: 'none',
                        borderRadius: '4px',
                        padding: '4px 6px',
                        fontSize: '11px',
                        cursor: 'pointer',
                      }}
                    >
                      ✕
                    </button>
                  </div>
                ) : (
                  <div style={{ width: '100%', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                    <div>
                      <div style={{ color: '#e8edf3', fontSize: '12px', fontWeight: '600', fontFamily: 'monospace' }}>
                        {r.name}
                      </div>
                      <div style={{ color: '#556677', fontSize: '11px', marginTop: '1px' }}>
                        {r.address}
                      </div>
                    </div>
                    <div style={{ display: 'flex', gap: '4px', flexShrink: 0 }}>
                      <button
                        title="Rename"
                        onClick={() => {
                          setRenamingName(r.name);
                          setRenameValue(r.name);
                          setRangeActionError(null);
                        }}
                        style={{
                          background: '#1e2d3e',
                          color: '#8899aa',
                          border: '1px solid #2e3f54',
                          borderRadius: '4px',
                          padding: '3px 6px',
                          fontSize: '11px',
                          cursor: 'pointer',
                        }}
                      >
                        ✏
                      </button>
                      <button
                        title="Delete"
                        onClick={() => void handleDeleteRange(r.name)}
                        style={{
                          background: '#2d1515',
                          color: '#e07070',
                          border: '1px solid #4a1515',
                          borderRadius: '4px',
                          padding: '3px 6px',
                          fontSize: '11px',
                          cursor: 'pointer',
                        }}
                      >
                        🗑
                      </button>
                    </div>
                  </div>
                )}
              </div>
            ))
          )}
        </div>

        {/* ── Section: Advanced (AppKey fallback) ───────────── */}
        <div style={sectionStyle}>
          <button
            onClick={() => setShowAdvanced((v) => !v)}
            style={{
              background: 'none',
              border: 'none',
              color: '#556677',
              cursor: 'pointer',
              fontFamily: 'Inter, sans-serif',
              fontSize: '12px',
              fontWeight: '700',
              letterSpacing: '0.06em',
              textTransform: 'uppercase',
              textAlign: 'left',
              padding: 0,
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
            }}
          >
            <span>{showAdvanced ? '▾' : '▸'}</span> Advanced
          </button>

          {showAdvanced && (
            <>
              <p style={labelStyle}>
                AppKey fallback — for CI/testing only. Enter a FAIT API key to use instead of Entra auth.
              </p>

              <input
                type="password"
                value={inputKey}
                onChange={(e) => { setInputKey(e.target.value); setKeySuccess(false); }}
                onKeyDown={(e) => { if (e.key === 'Enter') handleSaveAndTest(); }}
                placeholder="Paste API key here…"
                autoComplete="off"
                style={{
                  width: '100%',
                  background: '#243447',
                  border: `1px solid ${keyError ? '#e74c3c' : '#2e3f54'}`,
                  borderRadius: '6px',
                  color: '#e8edf3',
                  fontFamily: 'Inter, sans-serif',
                  fontSize: '13px',
                  padding: '8px 10px',
                  outline: 'none',
                  boxSizing: 'border-box',
                }}
                onFocus={(e) => { if (!keyError) e.target.style.borderColor = '#d4af37'; }}
                onBlur={(e) => { if (!keyError) e.target.style.borderColor = '#2e3f54'; }}
              />

              {keyError && (
                <div
                  role="alert"
                  style={{
                    color: '#e74c3c',
                    fontSize: '12px',
                    padding: '6px 10px',
                    background: '#2d1515',
                    borderRadius: '4px',
                    border: '1px solid #e74c3c',
                  }}
                >
                  ⚠ {keyError}
                </div>
              )}

              {keySuccess && (
                <div
                  style={{
                    color: '#4caf50',
                    fontSize: '12px',
                    padding: '6px 10px',
                    background: '#152d15',
                    borderRadius: '4px',
                    border: '1px solid #4caf50',
                  }}
                >
                  ✓ Key saved and verified
                </div>
              )}

              <button
                onClick={handleSaveAndTest}
                disabled={testing}
                style={{
                  width: '100%',
                  background: testing ? '#243447' : '#d4af37',
                  border: 'none',
                  borderRadius: '6px',
                  color: testing ? '#8899aa' : '#1a2332',
                  cursor: testing ? 'not-allowed' : 'pointer',
                  fontFamily: 'Inter, sans-serif',
                  fontWeight: '600',
                  fontSize: '13px',
                  padding: '9px',
                  transition: 'background 0.15s',
                }}
              >
                {testing ? 'Testing connection…' : 'Save & Test Connection'}
              </button>
            </>
          )}
        </div>

        {/* Footer note */}
        <div
          style={{
            color: '#445566',
            fontSize: '11px',
            lineHeight: 1.5,
            padding: '0 4px',
          }}
        >
          Settings are stored in OfficeRuntime.storage on this device.
        </div>
      </div>
    </div>
  );
};

export default SettingsPanel;
