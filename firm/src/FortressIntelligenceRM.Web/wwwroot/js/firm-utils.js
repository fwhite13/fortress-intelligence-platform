window.firmUtils = {
    formatLocalTime: function(isoUtc, options) {
        const d = new Date(isoUtc.endsWith('Z') ? isoUtc : isoUtc + 'Z');
        return new Intl.DateTimeFormat(undefined, options || {
            weekday: 'short', month: 'short', day: 'numeric',
            hour: 'numeric', minute: '2-digit', hour12: true
        }).format(d);
    },
    formatLocalTimeOnly: function(isoUtc) {
        const d = new Date(isoUtc.endsWith('Z') ? isoUtc : isoUtc + 'Z');
        return new Intl.DateTimeFormat(undefined, {
            hour: 'numeric', minute: '2-digit', hour12: true
        }).format(d);
    },
    formatLocalDateTime: function(isoUtc) {
        const d = new Date(isoUtc.endsWith('Z') ? isoUtc : isoUtc + 'Z');
        return new Intl.DateTimeFormat(undefined, {
            month: 'short', day: 'numeric', year: 'numeric',
            hour: 'numeric', minute: '2-digit', hour12: true
        }).format(d);
    }
};
