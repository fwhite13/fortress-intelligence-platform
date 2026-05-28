window.xlsxPreviewInterop = {
    getSheetNames: function (arrayBuffer) {
        const data = new Uint8Array(arrayBuffer);
        const workbook = XLSX.read(data, { type: 'array' });
        return workbook.SheetNames;
    },

    renderSheet: function (arrayBuffer, sheetName, containerId) {
        const data = new Uint8Array(arrayBuffer);
        const workbook = XLSX.read(data, { type: 'array' });
        const worksheet = workbook.Sheets[sheetName];
        if (!worksheet) return;
        const html = XLSX.utils.sheet_to_html(worksheet, { editable: false });
        const container = document.getElementById(containerId);
        if (container) container.innerHTML = html;
    }
};
