window.drawerEscapeHandler = null;

window.registerDrawerEscape = function (dotnetRef) {
    window.drawerEscapeHandler = function (e) {
        if (e.key === 'Escape') {
            dotnetRef.invokeMethodAsync('CloseDrawerFromJs');
        }
    };
    document.addEventListener('keydown', window.drawerEscapeHandler);
};

window.unregisterDrawerEscape = function () {
    if (window.drawerEscapeHandler) {
        document.removeEventListener('keydown', window.drawerEscapeHandler);
        window.drawerEscapeHandler = null;
    }
};
