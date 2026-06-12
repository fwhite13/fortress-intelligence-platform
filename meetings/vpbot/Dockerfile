# firm-vpbot — Fortress VP Meeting Bot
# Multi-stage build for production deployment

# Stage 1: Build
FROM node:20-slim AS builder

WORKDIR /app

# Install build dependencies
COPY package*.json ./
RUN npm ci

# Copy source and build
COPY tsconfig.json ./
COPY src ./src
RUN npm run build

# Stage 2: Production runtime
# Using Docker Hub Ubuntu + manual Playwright install (MCR unreachable from this host)
FROM ubuntu:24.04

# Prevent interactive prompts
ENV DEBIAN_FRONTEND=noninteractive

WORKDIR /app

# Install Node.js 20 + system deps for Playwright + audio
RUN apt-get update && apt-get install -y \
    curl \
    ca-certificates \
    gnupg \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_20.x nodistro main" > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update && apt-get install -y \
    nodejs \
    pulseaudio \
    xvfb \
    ffmpeg \
    dbus \
    # Playwright Chromium dependencies
    libnss3 \
    libatk1.0-0t64 \
    libatk-bridge2.0-0t64 \
    libcups2t64 \
    libdrm2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libpango-1.0-0 \
    libcairo2 \
    libasound2t64 \
    libatspi2.0-0t64 \
    fonts-liberation \
    libgl1 \
    && rm -rf /var/lib/apt/lists/*

# ML/Whisper deps removed (ADO#1841) — transcription now handled by firm-transcriber Batch job

# Set up virtual display and audio environment
ENV DISPLAY=:99
ENV PULSE_SERVER=unix:/var/run/pulse/native

# Copy node_modules and built files from builder
COPY --from=builder /app/node_modules ./node_modules
COPY --from=builder /app/dist ./dist
COPY --from=builder /app/package*.json ./

# Install Playwright Chromium browser
RUN npx playwright install chromium

# Create recordings directory
RUN mkdir -p /app/recordings && chmod 777 /app/recordings

# Create PulseAudio system-wide config
RUN mkdir -p /etc/pulse /var/run/pulse && \
    chown pulse:pulse /var/run/pulse && \
    # Add root to pulse-access group so root can connect to the PA daemon
    usermod -aG pulse-access root && \
    echo '#!/bin/bash\n\
set -e\n\
\n\
# Start virtual display\n\
Xvfb :99 -screen 0 1920x1080x24 &\n\
sleep 1\n\
\n\
# Start D-Bus (required for PulseAudio system mode)\n\
mkdir -p /run/dbus\n\
dbus-daemon --system --fork 2>/dev/null || true\n\
\n\
# Start PulseAudio in system mode (required when running as root)\n\
mkdir -p /var/run/pulse\n\
chown pulse:pulse /var/run/pulse\n\
pulseaudio --system --disallow-exit --disallow-module-loading=0 \\\n\
  --exit-idle-time=-1 --daemonize=yes\n\
sleep 2\n\
\n\
# Create a virtual audio sink for capturing browser audio\n\
pactl load-module module-null-sink sink_name=virtual_out sink_properties=device.description=VirtualOutput\n\
pactl set-default-sink virtual_out\n\
\n\
# Verify PulseAudio is running\n\
if pactl info >/dev/null 2>&1; then\n\
  echo "[start.sh] PulseAudio is running"\n\
  pactl list short sinks || true\n\
  pactl list short sources || true\n\
else\n\
  echo "[start.sh] WARNING: PulseAudio failed to start"\n\
fi\n\
\n\
# Start the application\n\
exec node dist/index.js\n\
' > /app/start.sh && chmod +x /app/start.sh

# Environment variables (override at runtime)
ENV PORT=3500
ENV AWS_REGION=us-east-1
ENV BOT_NAME="Fortress Notetaker"
ENV RECORDINGS_DIR=/app/recordings

EXPOSE 3500

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD touch /app/recordings/.healthcheck && rm /app/recordings/.healthcheck || exit 1

CMD ["/app/start.sh"]
