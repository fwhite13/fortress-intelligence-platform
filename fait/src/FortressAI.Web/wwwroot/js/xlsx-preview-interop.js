window.xlsxPreviewInterop = {
    getSheetNames: function (base64) {
        try {
            // Blazor Server passes byte[] as base64 string via JSInterop;
            // SheetJS reads base64 directly with { type: 'base64' }
            const workbook = XLSX.read(base64, { type: 'base64' });
            return workbook.SheetNames;
        } catch (e) {
            console.error('[xlsxPreview] getSheetNames failed:', e);
            throw e;
        }
    },
    renderSheet: function (base64, sheetName, containerId) {
        console.log('[xlsx-preview] renderSheet called, sheetName=', sheetName, 'containerId=', containerId);
        try {
            const workbook = XLSX.read(base64, { type: 'base64' });
            const worksheet = workbook.Sheets[sheetName];
            const html = XLSX.utils.sheet_to_html(worksheet, { editable: false });
            const container = document.getElementById(containerId);
            if (container) {
                container.innerHTML = html;
                console.log('[xlsxPreview] Rendered sheet', sheetName, 'in', containerId);
            } else {
                console.error('[xlsxPreview] Container not found:', containerId);
            }
        } catch (e) {
            console.error('[xlsxPreview] renderSheet failed:', e);
            throw e;
        }
    }
};
