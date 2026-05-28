window.docxPreviewInterop = {
    render: async function (arrayBuffer, containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        // docx-preview exposes docx.renderAsync globally via CDN
        await docx.renderAsync(arrayBuffer, container, null, {
            className: 'docx-preview-content',
            inWrapper: true,
            ignoreWidth: false,
            ignoreHeight: false,
            ignoreFonts: false,
            breakPages: true,
            useBase64URL: false
        });
    }
};
