window.downloadBase64 = function (fileName, mimeType, base64String) {
    const link = document.createElement('a');
    link.href = `data:${mimeType};base64,${base64String}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

window.toggleSidebarClass = function () {
    document.body.classList.toggle('sidebar-open');
};
