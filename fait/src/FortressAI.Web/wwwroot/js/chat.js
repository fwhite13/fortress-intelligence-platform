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

    downloadTextFile: function(filename, content) {
        const blob = new Blob([content], { type: 'text/plain' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },

    copyToClipboard: function(text) {
        return navigator.clipboard.writeText(text);
    },

    readFileAsBase64: function(fileIndex) {
        return new Promise((resolve, reject) => {
            const input = document.getElementById('attachment-input');
            if (!input || !input.files[fileIndex]) { resolve(''); return; }
            const reader = new FileReader();
            reader.onload = (e) => {
                const dataUrl = e.target.result;
                const base64 = dataUrl.split(',')[1];
                resolve(base64);
            };
            reader.onerror = () => resolve('');
            reader.readAsDataURL(input.files[fileIndex]);
        });
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
    },

    initSidebarResize: function(dragHandle, dotNetRef) {
        if (!dragHandle) return;
        let dragging = false;
        let startX = 0;
        let startWidth = 0;

        const onMouseMove = function(e) {
            if (!dragging) return;
            const now = Date.now();
            if (now - onMouseMove._last < 16) return;
            onMouseMove._last = now;
            const delta = startX - e.clientX;
            const newWidth = Math.min(
                Math.max(startWidth + delta, 280),
                Math.round(window.innerWidth * 0.5)
            );
            dotNetRef.invokeMethodAsync('UpdateSidebarWidth', newWidth);
        };
        onMouseMove._last = 0;

        const onMouseUp = function() {
            if (dragging) {
                dragging = false;
                document.body.style.cursor = '';
                document.body.style.userSelect = '';
            }
        };

        dragHandle.addEventListener('mousedown', function(e) {
            dragging = true;
            startX = e.clientX;
            startWidth = dragHandle.parentElement ? dragHandle.parentElement.offsetWidth : 320;
            document.body.style.cursor = 'col-resize';
            document.body.style.userSelect = 'none';
            e.preventDefault();
        });

        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);

        dragHandle._cleanupResize = function() {
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);
        };
    },

    disposeSidebarResize: function(dragHandle) {
        if (dragHandle && dragHandle._cleanupResize) {
            dragHandle._cleanupResize();
            delete dragHandle._cleanupResize;
        }
    },

    setupDragDrop: function(elementId, dotNetRef) {
        const el = document.getElementById(elementId);
        if (!el) return;
        el.addEventListener('dragover', (e) => { e.preventDefault(); dotNetRef.invokeMethodAsync('OnDragOver'); });
        el.addEventListener('dragleave', () => dotNetRef.invokeMethodAsync('OnDragLeave'));
        el.addEventListener('drop', async (e) => {
            e.preventDefault();
            const files = Array.from(e.dataTransfer.files);
            const result = await Promise.all(files.map(f => new Promise((resolve) => {
                const reader = new FileReader();
                reader.onload = () => resolve({
                    name: f.name,
                    contentType: f.type || 'application/octet-stream',
                    base64: reader.result.split(',')[1]
                });
                reader.readAsDataURL(f);
            })));
            dotNetRef.invokeMethodAsync('HandleDroppedFiles', result);
        });
    },

    startTaskTimer: function(startTimestampMs) {
        if (window._taskTimerInterval) clearInterval(window._taskTimerInterval);
        window._taskTimerInterval = setInterval(function() {
            var elapsed = Date.now() - startTimestampMs;
            var totalSecs = Math.floor(elapsed / 1000);
            var mins = Math.floor(totalSecs / 60).toString().padStart(2, '0');
            var secs = (totalSecs % 60).toString().padStart(2, '0');
            var el = document.getElementById('task-timer-display');
            if (el) el.textContent = mins + ':' + secs;
        }, 500);
    },

    stopTaskTimer: function() {
        if (window._taskTimerInterval) {
            clearInterval(window._taskTimerInterval);
            window._taskTimerInterval = null;
        }
        var el = document.getElementById('task-timer-display');
        if (el) el.textContent = '00:00';
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
