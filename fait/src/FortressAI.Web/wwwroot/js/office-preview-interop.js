window.officePreviewInterop = {
    render: async function(base64, fileType, containerId, sheetName) {
        console.log('[office-preview] render: fileType=', fileType, 'containerId=', containerId);
        if (fileType === "docx") {
            const container = document.getElementById(containerId);
            if (!container) {
                console.error('[office-preview] Container not found:', containerId);
                return;
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
                console.log('[office-preview] DOCX rendered successfully in', containerId);
            } catch (e) {
                console.error('[office-preview] DOCX render failed:', e);
                throw e;
            }
        }
    }
};
