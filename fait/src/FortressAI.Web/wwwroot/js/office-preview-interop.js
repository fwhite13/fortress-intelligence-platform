window.officePreviewInterop = {
    render: async function(base64, fileType, containerId, sheetName) {
        console.log('[office-preview] render: fileType=', fileType, 'containerId=', containerId, 'typeof docx=', typeof docx);
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
        } else if (fileType === "xlsx") {
            console.warn('[office-preview] render() called with fileType=xlsx — use renderSheet instead');
        }
    },
    getSheetNames: function(base64) {
        console.log('[office-preview] getSheetNames: typeof XLSX=', typeof XLSX, 'base64 length=', base64 ? base64.length : 0);
        try {
            const workbook = XLSX.read(base64, { type: 'base64' });
            return workbook.SheetNames;
        } catch (e) {
            console.error('[office-preview] getSheetNames failed:', e);
            throw e;
        }
    },
    renderSheet: function(base64, sheetName, containerId) {
        console.log('[office-preview] renderSheet: sheetName=', sheetName, 'containerId=', containerId);
        try {
            const workbook = XLSX.read(base64, { type: 'base64' });
            const worksheet = workbook.Sheets[sheetName];
            const html = XLSX.utils.sheet_to_html(worksheet, { editable: false });
            const container = document.getElementById(containerId);
            if (container) {
                container.innerHTML = html;
                console.log('[office-preview] Rendered sheet', sheetName, 'in', containerId);
            } else {
                console.error('[office-preview] Container not found:', containerId);
            }
        } catch (e) {
            console.error('[office-preview] renderSheet failed:', e);
            throw e;
        }
    }
};
