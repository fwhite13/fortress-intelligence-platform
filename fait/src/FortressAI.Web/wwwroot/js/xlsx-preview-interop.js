function base64ToUint8Array(base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
}

window.xlsxPreviewInterop = {
    getSheetNames: function(base64) {
        const data = base64ToUint8Array(base64);
        const workbook = XLSX.read(data, { type: 'array' });
        return workbook.SheetNames;
    },
    renderSheet: function(base64, sheetName, containerId) {
        const data = base64ToUint8Array(base64);
        const workbook = XLSX.read(data, { type: 'array' });
        const worksheet = workbook.Sheets[sheetName];
        const html = XLSX.utils.sheet_to_html(worksheet, { editable: false });
        const container = document.getElementById(containerId);
        if (container) {
            container.innerHTML = html;
        }
    }
};
