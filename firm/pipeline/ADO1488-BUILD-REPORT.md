# Build Report — ADO#1488
**Pre-bake Whisper large-v3 into vpbot Docker image**

---

## What was built
Added `ENV HF_HOME` and a `RUN python3` pre-bake step to Stage 2 of the vpbot Dockerfile so that the Whisper large-v3 model is downloaded into the image at build time, eliminating the runtime HuggingFace Hub download that fails in Fargate's private VPC.

---

## Files changed
- `Dockerfile` (skunkworks repo) — 5 lines added in Stage 2 (production runtime)
  - `ENV HF_HOME=/app/.cache/huggingface` — sets cache path before download
  - `RUN python3 -c "from faster_whisper import WhisperModel; WhisperModel('large-v3', device='cpu', compute_type='int8')"` — triggers model download at build time
  - Comment and echo confirmation line

---

## Commit
- **Repo:** `/home/fredw/projects/skunkworks/meeting-assistant/firm-vpbot/`
- **Hash:** `4a9b780`
- **Message:** `fix(ADO#1488): pre-bake Whisper large-v3 into Docker image — no runtime HF Hub download`

---

## Placement verification
```
RUN pip3 install --no-cache-dir --break-system-packages faster-whisper   ← existing

# Pre-bake Whisper large-v3 model (avoids runtime HF Hub download in Fargate VPC)
ENV HF_HOME=/app/.cache/huggingface                                       ← NEW
RUN python3 -c "from faster_whisper import WhisperModel; ..."             ← NEW
    && echo "Whisper large-v3 pre-baked successfully"

# Set up virtual display and audio environment                            ← existing
ENV DISPLAY=:99
...
COPY --from=builder /app/node_modules ./node_modules                      ← existing
```
ENV is set before RUN. Pre-bake is after pip install. Pre-bake is before COPY --from=builder. ✅

---

## Syntax validation
`docker build --check .` returned: **"Check complete, no warnings found."**

---

## Parallelization used
No — single-file, single CC session.

## CC sessions run
1 CC run (sonnet). CC made the insertion at lines 71-73 as specified.

---

## Acceptance criteria
- [x] `ENV HF_HOME=/app/.cache/huggingface` added to Dockerfile Stage 2 — line 72
- [x] `RUN python3 -c "from faster_whisper import WhisperModel; WhisperModel('large-v3', device='cpu', compute_type='int8')"` added after pip install, before COPY --from=builder — lines 73-74
- [x] No TypeScript changes
- [x] No C# changes
- [x] Dockerfile syntax validated (docker build --check: no warnings)

---

## Known edge cases / things Clint should scrutinize
- **Build time:** The actual `docker build` will download ~3GB for the Whisper large-v3 model. Rhodey should expect a long build. This is expected and correct.
- **Image size:** Final image will be significantly larger (~3GB+). This is the trade-off for offline inference in Fargate.
- **HF_HOME at runtime:** The `ENV HF_HOME=/app/.cache/huggingface` is baked into the image. At runtime, faster-whisper will check this path first and find the pre-baked model. No task def changes needed.
- **`--break-system-packages`:** Already present on the pip3 install line (Ubuntu 24.04 requirement). Not touched.

---

## How to test locally
Rhodey handles the full `docker build`. After build completes, verify:
```bash
docker run --rm <image> python3 -c "from faster_whisper import WhisperModel; m = WhisperModel('large-v3', device='cpu', compute_type='int8'); print('Model loaded from cache:', m)"
```
Should load instantly without any network calls.

---

_Tony Stark — BUILD cycle 1 — 2026-04-01_
