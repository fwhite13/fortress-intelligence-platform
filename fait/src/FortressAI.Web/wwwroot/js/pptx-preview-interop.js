// pptx-preview-interop.js — PDF.js wrapper for PPTX preview (ADO#4569)
window.pptxPreviewInterop = {
    _pdfDoc: null,
    _currentPage: 1,

    render: async function(base64, containerId) {
        function base64ToUint8Array(b64) {
            const binary = atob(b64);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            return bytes;
        }

        const data = base64ToUint8Array(base64);
        const loadingTask = pdfjsLib.getDocument({ data });
        this._pdfDoc = await loadingTask.promise;
        this._currentPage = 1;
        await this._renderPage(containerId, 1);
        return this._pdfDoc.numPages;
    },

    renderPage: async function(containerId, pageNum) {
        if (!this._pdfDoc) return;
        this._currentPage = pageNum;
        await this._renderPage(containerId, pageNum);
    },

    getPageCount: function() {
        return this._pdfDoc ? this._pdfDoc.numPages : 0;
    },

    _renderPage: async function(containerId, pageNum) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const page = await this._pdfDoc.getPage(pageNum);
        const viewport = page.getViewport({ scale: 1.5 });
        let canvas = container.querySelector('canvas');
        if (!canvas) {
            canvas = document.createElement('canvas');
            container.innerHTML = '';
            container.appendChild(canvas);
        }
        canvas.height = viewport.height;
        canvas.width = viewport.width;
        const ctx = canvas.getContext('2d');
        await page.render({ canvasContext: ctx, viewport }).promise;
    }
};
