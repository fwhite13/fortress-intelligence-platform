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
    },

    // --- Dictation / Speech Recognition ---
    _recognition: null,
    _isRecording: false,
    _dotNetRecordingRef: null,

    startDictation: function(dotNetRef) {
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SpeechRecognition) return false;

        this._dotNetRecordingRef = dotNetRef;
        this._recognition = new SpeechRecognition();
        this._recognition.continuous = true;
        this._recognition.interimResults = true;
        this._recognition.lang = 'en-US';

        this._finalTranscript = '';
        this._recognition.onresult = (event) => {
            let interimText = '';
            for (let i = event.resultIndex; i < event.results.length; i++) {
                const transcript = event.results[i][0].transcript;
                if (event.results[i].isFinal) {
                    this._finalTranscript += transcript + ' ';
                } else {
                    interimText += transcript;
                }
            }
            if (dotNetRef) {
                dotNetRef.invokeMethodAsync('OnSpeechResult', this._finalTranscript.trim(), interimText);
            }
        };

        this._recognition.onerror = (event) => {
            if (dotNetRef) dotNetRef.invokeMethodAsync('OnSpeechError', event.error);
        };

        this._recognition.onend = () => {
            this._isRecording = false;
            if (dotNetRef) dotNetRef.invokeMethodAsync('OnSpeechEnded');
        };

        this._recognition.start();
        this._isRecording = true;
        return true;
    },

    stopDictation: function() {
        if (this._recognition) {
            this._recognition.stop();
            this._recognition = null;
        }
        this._isRecording = false;
    },

    isSpeechRecognitionSupported: function() {
        return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
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
