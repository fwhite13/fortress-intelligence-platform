window.docxPreviewInterop = {
    render: async function (base64, containerId) {
        console.log('[docxPreview] render called containerId=', containerId);
        const container = document.getElementById(containerId);
        if (!container) {
            console.error('[docxPreview] Container not found:', containerId);
            return;
        }
        console.log('[docxPreview] container element found, rendering...');
        try {
            // Blazor Server passes byte[] as base64 string via JSInterop
            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);

            // docx-preview exposes docx.renderAsync globally via CDN
            await docx.renderAsync(bytes.buffer, container, null, {
                className: 'docx-preview-content',
                inWrapper: true,
                ignoreWidth: false,
                ignoreHeight: false,
                ignoreFonts: false,
                breakPages: true,
                useBase64URL: false
            });
            console.log('[docxPreview] Rendered successfully in', containerId);
        } catch (e) {
            console.error('[docxPreview] render failed:', e);
            throw e;
        }
    }
};
