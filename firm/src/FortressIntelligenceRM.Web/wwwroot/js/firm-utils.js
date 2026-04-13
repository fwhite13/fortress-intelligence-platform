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
