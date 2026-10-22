FROM nvcr.io/nvidia/cuda:12.1.0-cudnn8-runtime-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV PYTHONUNBUFFERED=1
ENV HF_HOME=/app/.cache/huggingface

RUN apt-get update && apt-get install -y \
    python3 python3-pip ffmpeg git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip3 install --no-cache-dir -r requirements.txt \
    --extra-index-url https://download.pytorch.org/whl/cu126

# ctranslate2 >=4.5 (faster-whisper) needs cuDNN 9 / cuBLAS 12; the base image ships cuDNN 8, so point the
# loader at the nvidia-cudnn-cu12 / nvidia-cublas-cu12 wheels pulled in by torch 2.8.0+cu126
ENV LD_LIBRARY_PATH=/usr/local/lib/python3.10/dist-packages/nvidia/cudnn/lib:/usr/local/lib/python3.10/dist-packages/nvidia/cublas/lib:${LD_LIBRARY_PATH}

# Pre-bake ALL pyannote model weights into image
# Pipeline.from_pretrained only downloads config; weights need explicit snapshot_download
# HF terms accepted by Fred on 2026-04-17 for segmentation-3.0; HF terms also accepted for speaker-diarization-3.1
# (upgraded from speaker-diarization-community-1 — same segmentation-3.0 backbone, pure PyTorch, no onnxruntime)
ARG HF_TOKEN
RUN HF_HOME=/app/.cache/huggingface python3 -c "\
from huggingface_hub import snapshot_download; \
token = '${HF_TOKEN}'; \
print('Downloading pyannote/speaker-diarization-3.1...'); \
snapshot_download('pyannote/speaker-diarization-3.1', token=token, local_dir=None); \
print('Downloading pyannote/segmentation-3.0...'); \
snapshot_download('pyannote/segmentation-3.0', token=token, local_dir=None); \
print('pyannote model weights cached successfully'); \
"

# ADO#8232: pyannote/embedding (voice embeddings for speaker name resolution) is a separately gated repo.
# Non-fatal so the image still builds before its terms are accepted — the job then downloads at runtime
# or falls back to roster-timing speaker resolution.
RUN HF_HOME=/app/.cache/huggingface python3 -c "\
from huggingface_hub import snapshot_download; \
token = '${HF_TOKEN}'; \
snapshot_download('pyannote/embedding', token=token, local_dir=None); \
print('pyannote/embedding cached successfully'); \
" || echo "WARNING: pyannote/embedding pre-bake failed (accept its HF terms) — will download at runtime"

# Pre-bake the WhisperX English word-alignment model (torchaudio wav2vec2 bundle) so jobs
# don't depend on download.pytorch.org at runtime. WhisperX's VAD model ships in the wheel.
RUN python3 -c "\
import torchaudio; \
torchaudio.pipelines.WAV2VEC2_ASR_BASE_960H.get_model(); \
print('wav2vec2 alignment model cached successfully'); \
"

COPY transcribe.py .

ENTRYPOINT ["python3", "transcribe.py"]
