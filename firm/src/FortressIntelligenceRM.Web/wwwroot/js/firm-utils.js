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
window.firmMindmap = {
    _instance: null,

    async render(containerId, mindmapJson) {
        // Load mind-elixir from CDN if not already present
        if (!window.MindElixir) {
            await new Promise((resolve, reject) => {
                const s = document.createElement('script');
                s.src = 'https://cdn.jsdelivr.net/npm/mind-elixir@4/dist/MindElixir.umd.js';
                s.onload = resolve;
                s.onerror = reject;
                document.head.appendChild(s);
            });
        }

        const container = document.getElementById(containerId);
        if (!container) return;

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
            editable: false,
            theme: MindElixir.theme.dark
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
