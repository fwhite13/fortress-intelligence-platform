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
            let finalText = '';
            let interimText = '';
            for (let i = 0; i < event.results.length; i++) {
                const transcript = event.results[i][0].transcript;
                if (event.results[i].isFinal) {
                    finalText += transcript + ' ';
                } else {
                    interimText += transcript;
                }
            }
            this._finalTranscript = finalText;
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
    },

    copyArtifact: function(artifactId) {
        const rawEl = document.getElementById(artifactId + '-raw');
        if (rawEl) {
            navigator.clipboard.writeText(rawEl.textContent || '').then(() => {
                const btn = document.querySelector(`#${artifactId} .artifact-btn`);
                if (btn) {
                    const orig = btn.textContent;
                    btn.textContent = 'Copied!';
                    setTimeout(() => { btn.textContent = orig; }, 1500);
                }
            }).catch(() => {
                const range = document.createRange();
                range.selectNodeContents(rawEl);
                const sel = window.getSelection();
                if (sel) { sel.removeAllRanges(); sel.addRange(range); }
                document.execCommand('copy');
            });
        }
    },

    downloadArtifact: function(artifactId, title, ext) {
        const rawEl = document.getElementById(artifactId + '-raw');
        if (!rawEl) return;
        const content = rawEl.textContent || '';
        const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = (title || 'artifact') + '.' + (ext || 'txt');
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    toggleArtifact: function(artifactId) {
        const contentEl = document.getElementById(artifactId + '-content');
        const btn = document.querySelector(`#${artifactId} .artifact-actions .artifact-btn:last-child`);
        if (contentEl) {
            const isHidden = contentEl.style.display === 'none';
            contentEl.style.display = isHidden ? 'block' : 'none';
            if (btn) btn.innerHTML = isHidden ? '&#x25B2;' : '&#x25BC;';
        }
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
