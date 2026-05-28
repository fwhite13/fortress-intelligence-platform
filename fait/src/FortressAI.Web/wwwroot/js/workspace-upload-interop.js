window.workspaceUploadInterop = {
    // Returns true if any of the DataTransferItems is a directory entry
    hasDirectoryItem: function (inputElementId) {
        const input = document.getElementById(inputElementId);
        if (!input || !input.files) return false;
        const dt = input._lastDropEvent;
        if (!dt || !dt.dataTransfer || !dt.dataTransfer.items) return false;
        for (let i = 0; i < dt.dataTransfer.items.length; i++) {
            const item = dt.dataTransfer.items[i];
            if (item.webkitGetAsEntry && item.webkitGetAsEntry()?.isDirectory) return true;
        }
        return false;
    },
    // Attach a dragover+drop listener to a drop zone element that stores the last drop event
    attachDropListener: function (dropZoneId, inputId, dotnetRef) {
        const zone = document.getElementById(dropZoneId);
        if (!zone) return;
        zone.addEventListener('dragover', e => { e.preventDefault(); });
        zone.addEventListener('drop', e => {
            e.preventDefault();
            const input = document.getElementById(inputId);
            if (input) input._lastDropEvent = e;

            // Check for directory items
            let hasDir = false;
            if (e.dataTransfer && e.dataTransfer.items) {
                for (let i = 0; i < e.dataTransfer.items.length; i++) {
                    const item = e.dataTransfer.items[i];
                    if (item.webkitGetAsEntry && item.webkitGetAsEntry()?.isDirectory) {
                        hasDir = true;
                        break;
                    }
                }
            }
            if (hasDir) {
                dotnetRef.invokeMethodAsync('OnFolderDropDetected');
                return;
            }
            // Transfer files to the hidden input (works in Chrome/Edge/Firefox)
            try {
                const dt2 = new DataTransfer();
                if (e.dataTransfer.files) {
                    for (let f of e.dataTransfer.files) dt2.items.add(f);
                }
                input.files = dt2.files;
                input.dispatchEvent(new Event('change', { bubbles: true }));
            } catch (err) {
                console.warn('[workspaceUploadInterop] Could not set files on input:', err);
            }
        });
    }
};
