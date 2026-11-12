// WI #9139 — chunked recording upload for the /upload page (UploadRecording.razor).
//
// The file never crosses the Blazor circuit: this script slices it into parts (size from the server, 8 MB)
// and sends them with fetch to FIRM's own endpoints, which forward them to S3 multipart. The browser never
// talks to S3 and never receives AWS credentials.
//
//   POST   /api/uploads/recording/init
//   PUT    /api/uploads/recording/{uploadId}/part/{n}     (each part retried 3 times with backoff)
//   POST   /api/uploads/recording/{uploadId}/complete
//   DELETE /api/uploads/recording/{uploadId}               (cancel)
//
// A part that still fails after its retries pauses the upload instead of restarting it: "Resume" (or the
// browser coming back online) continues from that part. Nothing here logs file names, titles or bytes.
window.firmUpload = (function () {
    const RETRIES = 3;                 // retries per part, after the first attempt
    const BACKOFF_MS = [1000, 3000, 8000];
    const ACCEPT_EXT = ['.m4a', '.mp3', '.wav', '.aac', '.ogg', '.opus', '.flac', '.caf', '.mp4', '.m4b'];

    let current = null;                // { file, uploadId, partSize, partCount, nextPart, sent, meta, abort, paused }
    let dotnet = null;
    let wakeLock = null;

    function call(method, ...args) {
        // The Blazor circuit can drop on a phone (screen lock, network change). The upload itself must not
        // depend on these callbacks, so failures are ignored and the plain fallback element is updated too.
        try { if (dotnet) dotnet.invokeMethodAsync(method, ...args).catch(() => { }); } catch (e) { /* ignore */ }
    }

    function fallback(text, href) {
        const el = document.getElementById('firm-upload-fallback');
        if (!el) return;
        el.hidden = false;
        el.textContent = text;
        if (href) {
            const a = document.createElement('a');
            a.href = href;
            a.textContent = ' Open meeting';
            el.appendChild(a);
        }
    }

    function pad(n) { return String(n).padStart(2, '0'); }

    // "yyyy-MM-ddTHH:mm" in the browser's local time, for <input type="datetime-local">.
    function toLocalInput(ms) {
        const d = new Date(ms);
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }

    function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

    async function readError(res) {
        try { const j = await res.json(); if (j && j.error) return j.error; } catch (e) { /* ignore */ }
        if (res.status === 401) return 'Your session has expired. Sign in again and retry.';
        if (res.status === 413) return 'A part of the file was too large for the server.';
        return `The server returned an error (${res.status}).`;
    }

    async function requestWakeLock() {
        try { if ('wakeLock' in navigator) wakeLock = await navigator.wakeLock.request('screen'); } catch (e) { wakeLock = null; }
    }
    function releaseWakeLock() {
        try { if (wakeLock) wakeLock.release(); } catch (e) { /* ignore */ }
        wakeLock = null;
    }

    async function putPart(n) {
        const start = (n - 1) * current.partSize;
        const blob = current.file.slice(start, Math.min(start + current.partSize, current.file.size));
        let lastError = 'Network error';
        for (let attempt = 0; attempt <= RETRIES; attempt++) {
            if (current.abort.signal.aborted) throw new DOMException('Aborted', 'AbortError');
            if (attempt > 0) await sleep(BACKOFF_MS[Math.min(attempt - 1, BACKOFF_MS.length - 1)]);
            try {
                const res = await fetch(`/api/uploads/recording/${current.uploadId}/part/${n}`, {
                    method: 'PUT',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/octet-stream' },
                    body: blob,
                    signal: current.abort.signal
                });
                if (res.ok) return blob.size;
                lastError = await readError(res);
                // 4xx other than 408/429 will not get better by retrying (wrong size, gone, not yours).
                if (res.status >= 400 && res.status < 500 && res.status !== 408 && res.status !== 429) {
                    const err = new Error(lastError); err.fatal = true; throw err;
                }
            } catch (e) {
                if (e.name === 'AbortError' || e.fatal) throw e;
                lastError = navigator.onLine === false ? 'You are offline.' : 'Network error';
            }
        }
        throw new Error(lastError);
    }

    async function run() {
        const c = current;
        c.paused = false;
        call('OnResumed');
        await requestWakeLock();
        try {
            while (c.nextPart <= c.partCount) {
                const bytes = await putPart(c.nextPart);
                c.sent += bytes;
                c.nextPart++;
                call('OnProgress', c.sent, c.file.size);
            }
            call('OnFinishing');
            let res, attempt = 0;
            while (true) {
                try {
                    res = await fetch(`/api/uploads/recording/${c.uploadId}/complete`, {
                        method: 'POST',
                        credentials: 'same-origin',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify(c.meta),
                        signal: c.abort.signal
                    });
                    if (res.ok || (res.status !== 409 && res.status < 500) || attempt >= RETRIES) break;
                } catch (e) {
                    if (e.name === 'AbortError' || attempt >= RETRIES) throw e;
                }
                await sleep(BACKOFF_MS[Math.min(attempt, BACKOFF_MS.length - 1)]);
                attempt++;
            }
            if (!res.ok) {
                const err = new Error(await readError(res)); err.fatal = true; throw err;
            }
            const done = await res.json();
            current = null;
            releaseWakeLock();
            call('OnDone', done.meetingId, done.url);
            fallback('Uploaded. The recording is being transcribed.', done.url);
        } catch (e) {
            releaseWakeLock();
            if (e.name === 'AbortError') return;
            if (e.fatal) {
                current = null;
                call('OnFailed', e.message || 'Upload failed.');
                fallback(e.message || 'Upload failed.');
                return;
            }
            // Transient: keep the upload so it resumes from the failed part, not from the start.
            c.paused = true;
            call('OnPaused', (e.message || 'Network error') + ' The upload is paused and will continue from where it stopped.');
        }
    }

    window.addEventListener('online', () => { if (current && current.paused) run(); });

    return {
        acceptedExtensions: ACCEPT_EXT,

        // Reads name/size/type/last-modified of the picked file and hands them to the component.
        register: function (inputId, dotnetRef) {
            dotnet = dotnetRef;
            const input = document.getElementById(inputId);
            if (!input || input.dataset.firmUploadBound) return;
            input.dataset.firmUploadBound = '1';
            input.addEventListener('change', () => {
                const f = input.files && input.files[0];
                if (!f) { call('OnFileCleared'); return; }
                const dot = f.name.lastIndexOf('.');
                const stem = dot > 0 ? f.name.substring(0, dot) : f.name;
                const ext = dot > 0 ? f.name.substring(dot).toLowerCase() : '';
                call('OnFileSelected', stem, ext, f.size, f.type || '', toLocalInput(f.lastModified || Date.now()));
            });
        },

        unregister: function () { dotnet = null; },

        // meta: { title, recordedAtLocal: "yyyy-MM-ddTHH:mm" }. Returns null on start, or an error string.
        start: async function (inputId, meta) {
            if (current) return 'An upload is already in progress.';
            const input = document.getElementById(inputId);
            const file = input && input.files && input.files[0];
            if (!file) return 'Choose a file first.';
            const fb = document.getElementById('firm-upload-fallback');
            if (fb) { fb.hidden = true; fb.textContent = ''; }

            const recorded = meta && meta.recordedAtLocal ? new Date(meta.recordedAtLocal) : new Date(file.lastModified || Date.now());
            const recordedAt = isNaN(recorded.getTime()) ? new Date().toISOString() : recorded.toISOString();
            const title = (meta && meta.title) || '';

            let res;
            try {
                res = await fetch('/api/uploads/recording/init', {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ fileName: file.name, contentType: file.type || '', size: file.size, title, recordedAt })
                });
            } catch (e) {
                return navigator.onLine === false ? 'You are offline.' : 'Could not reach FIRM. Check your connection and try again.';
            }
            if (!res.ok) return await readError(res);
            const init = await res.json();
            current = {
                file,
                uploadId: init.uploadId,
                partSize: init.partSize,
                partCount: init.partCount,
                nextPart: 1,
                sent: 0,
                meta: { title, recordedAt },
                abort: new AbortController(),
                paused: false
            };
            call('OnProgress', 0, file.size);
            run();
            return null;
        },

        resume: function () { if (current && current.paused) run(); },

        cancel: async function () {
            const c = current;
            current = null;
            releaseWakeLock();
            if (!c) return;
            c.abort.abort();
            try {
                await fetch(`/api/uploads/recording/${c.uploadId}`, { method: 'DELETE', credentials: 'same-origin' });
            } catch (e) { /* the S3 lifecycle rule cleans up */ }
        },

        reset: function (inputId) {
            const input = document.getElementById(inputId);
            if (input) input.value = '';
        },

        isBusy: function () { return !!current; }
    };
})();
