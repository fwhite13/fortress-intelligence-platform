window.docxPreviewInterop = {
    render: async function (base64, containerId) {
        // Self-healing CDN load: if @latest drifted or script tag raced, load pinned version
        if (typeof docx === 'undefined') {
            console.warn('[docx-preview] docx global not found — loading pinned CDN script');
            await new Promise((resolve, reject) => {
                const s = document.createElement('script');
                s.src = 'https://cdn.jsdelivr.net/npm/docx-preview@0.3.7/dist/docx-preview.min.js';
                s.onload = resolve;
                s.onerror = () => reject(new Error('[docx-preview] Failed to load CDN script'));
                document.head.appendChild(s);
            });
        }
        console.log('[docx-preview] render called, containerId=', containerId, 'base64 length=', base64 ? base64.length : 0);
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('[docx-preview] Container not found:', containerId);
            throw new Error('[docx-preview] Container not found: ' + containerId);
        }
        try {
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            await docx.renderAsync(bytes.buffer, container, null, {
                className: 'docx-preview-content',
                inWrapper: true,
                ignoreWidth: false,
                ignoreHeight: false,
                ignoreFonts: false,
                breakPages: true,
                useBase64URL: false
            });
            console.log('[docx-preview] Rendered successfully in', containerId);
        } catch (e) {
            console.error('[docx-preview] render failed:', e);
            throw e;
        }
    }
};
