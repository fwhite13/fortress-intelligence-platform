window.firmUtils = {
    formatLocalTime: function(isoUtc, options) {
        if (!isoUtc) return '';
        const normalized = isoUtc.endsWith('Z') ? isoUtc : isoUtc.replace(/\+00:00$/, '') + 'Z';
        const d = new Date(normalized);
        return new Intl.DateTimeFormat(undefined, options || {
            weekday: 'short', month: 'short', day: 'numeric',
            hour: 'numeric', minute: '2-digit', hour12: true
        }).format(d);
    },
    formatLocalTimeOnly: function(isoUtc) {
        if (!isoUtc) return '';
        const normalized = isoUtc.endsWith('Z') ? isoUtc : isoUtc.replace(/\+00:00$/, '') + 'Z';
        const d = new Date(normalized);
        return new Intl.DateTimeFormat(undefined, {
            hour: 'numeric', minute: '2-digit', hour12: true
        }).format(d);
    },
    formatLocalDateTime: function(isoUtc) {
        if (!isoUtc) return '';
        const normalized = isoUtc.endsWith('Z') ? isoUtc : isoUtc.replace(/\+00:00$/, '') + 'Z';
        const d = new Date(normalized);
        return new Intl.DateTimeFormat(undefined, {
            month: 'short', day: 'numeric', year: 'numeric',
            hour: 'numeric', minute: '2-digit', hour12: true
        }).format(d);
    }
};

// ── Mind Map (mind-elixir) ────────────────────────────────────────────────────
// Loaded lazily when a mind map tab is first shown.
// Uses ES module dynamic import (v4 has no UMD build).
window.firmMindmap = {
    _instance: null,
    _MindElixir: null,

    async render(containerId, mindmapJson) {
        // Load mind-elixir via ES module dynamic import if not already loaded
        if (!window.firmMindmap._MindElixir) {
            const mod = await import('https://cdn.jsdelivr.net/npm/mind-elixir@4/dist/MindElixir.js');
            window.firmMindmap._MindElixir = mod.default;
        }
        const MindElixir = window.firmMindmap._MindElixir;

        const container = document.getElementById(containerId);
        if (!container) throw new Error('Mind map container not found: ' + containerId);

        // Destroy previous instance if any
        if (window.firmMindmap._instance) {
            try { window.firmMindmap._instance.destroy?.(); } catch {}
            window.firmMindmap._instance = null;
        }
        container.innerHTML = '';

        // Convert FIRM mindmap JSON to mind-elixir nodeData format
        const data = window.firmMindmap._toMindElixirData(mindmapJson);

        const me = new MindElixir({
            el: '#' + containerId,
            direction: MindElixir.LEFT,
            draggable: true,
            contextMenu: false,
            toolBar: true,
            keypress: false,
        });

        me.init(data);
        window.firmMindmap._instance = me;
    },

    _toMindElixirData(mindmapJson) {
        const json = (typeof mindmapJson === 'string') ? JSON.parse(mindmapJson) : mindmapJson;
        const convert = (node) => ({
            id: node.id || ('n' + Math.random().toString(36).slice(2)),
            topic: node.label || node.topic || '',
            children: (node.children || []).map(convert)
        });
        return {
            nodeData: {
                id: 'root',
                topic: json.title || 'Meeting',
                children: (json.nodes || []).map(convert)
            }
        };
    }
};
