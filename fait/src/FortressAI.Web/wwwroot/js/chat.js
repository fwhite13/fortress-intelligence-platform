window.fortressChat = {
    _scrollListener: null,
    _dotNetRef: null,

    scrollToBottom: function () {
        const container = document.getElementById('chat-messages');
        if (container) {
            requestAnimationFrame(() => {
                container.scrollTop = container.scrollHeight;
            });
        }
    },

    initScrollListener: function (dotNetRef) {
        const container = document.getElementById('chat-messages');
        if (!container) return;

        // Clean up any existing listener
        this.removeScrollListener();

        this._dotNetRef = dotNetRef;
        this._scrollListener = () => {
            const atBottom = container.scrollTop + container.clientHeight >= container.scrollHeight - 150;
            const pill = document.getElementById('jump-to-bottom-pill');
            if (pill) {
                if (atBottom) {
                    pill.classList.remove('visible');
                } else {
                    pill.classList.add('visible');
                }
            }
        };

        container.addEventListener('scroll', this._scrollListener, { passive: true });
    },

    removeScrollListener: function () {
        const container = document.getElementById('chat-messages');
        if (container && this._scrollListener) {
            container.removeEventListener('scroll', this._scrollListener);
        }
        this._scrollListener = null;
        this._dotNetRef = null;
    },

    jumpToBottom: function () {
        const container = document.getElementById('chat-messages');
        if (container) {
            container.scrollTo({
                top: container.scrollHeight,
                behavior: 'smooth'
            });
        }
    },

    highlightCode: function () {
        document.querySelectorAll('pre code:not(.hljs)').forEach((block) => {
            if (window.hljs) {
                hljs.highlightElement(block);
            }
        });
    },

    autoResize: function (element) {
        element.style.height = 'auto';
        element.style.height = Math.min(element.scrollHeight, 200) + 'px';
    }
};

// Auto-resize chat input on typing
document.addEventListener('input', function (e) {
    if (e.target.id === 'chat-input-field') {
        window.fortressChat.autoResize(e.target);
    }
});

window.scrollElementIntoView = function(el) {
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
};
