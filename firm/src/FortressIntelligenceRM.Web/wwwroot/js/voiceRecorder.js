// WI #8298 — microphone capture for voice enrollment (SettingsDialog → VoiceEnrollmentPanel).
window.voiceRecorder = {
  mediaRecorder: null,
  chunks: [],
  blob: null,
  blobUrl: null,
  audio: null,

  start: async function (dotnetRef) {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    const mimeType = MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm' : '';
    window.voiceRecorder.chunks = [];
    window.voiceRecorder.mediaRecorder = new MediaRecorder(stream, mimeType ? { mimeType } : undefined);
    window.voiceRecorder.mediaRecorder.ondataavailable = e => {
      if (e.data && e.data.size > 0) window.voiceRecorder.chunks.push(e.data);
    };
    window.voiceRecorder.mediaRecorder.onstop = async () => {
      stream.getTracks().forEach(t => t.stop());
      window.voiceRecorder.reset();
      const blob = new Blob(window.voiceRecorder.chunks, { type: 'audio/webm' });
      window.voiceRecorder.blob = blob;
      window.voiceRecorder.blobUrl = URL.createObjectURL(blob);
      // Stream reference rather than byte[] — a 30s sample exceeds SignalR's 32 KB message limit.
      await dotnetRef.invokeMethodAsync('OnRecordingComplete', DotNet.createJSStreamReference(blob));
    };
    window.voiceRecorder.mediaRecorder.start(100);
  },

  stop: function () {
    if (window.voiceRecorder.mediaRecorder && window.voiceRecorder.mediaRecorder.state !== 'inactive')
      window.voiceRecorder.mediaRecorder.stop();
  },

  play: function () {
    if (!window.voiceRecorder.blobUrl) return;
    if (window.voiceRecorder.audio) window.voiceRecorder.audio.pause();
    window.voiceRecorder.audio = new Audio(window.voiceRecorder.blobUrl);
    window.voiceRecorder.audio.play();
  },

  reset: function () {
    if (window.voiceRecorder.audio) window.voiceRecorder.audio.pause();
    if (window.voiceRecorder.blobUrl) URL.revokeObjectURL(window.voiceRecorder.blobUrl);
    window.voiceRecorder.audio = null;
    window.voiceRecorder.blob = null;
    window.voiceRecorder.blobUrl = null;
  }
};
