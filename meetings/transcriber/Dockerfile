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
    --extra-index-url https://download.pytorch.org/whl/cu121

# Pre-bake ALL pyannote model weights into image
# Pipeline.from_pretrained only downloads config; weights need explicit snapshot_download
# HF terms accepted by Fred on 2026-04-17 for speaker-diarization-3.1 and segmentation-3.0
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

COPY transcribe.py .

ENTRYPOINT ["python3", "transcribe.py"]
