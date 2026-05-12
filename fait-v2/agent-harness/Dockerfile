FROM node:20-slim

# Install system dependencies
RUN apt-get update && apt-get install -y \
    curl \
    git \
    ca-certificates \
    unzip \
    && rm -rf /var/lib/apt/lists/*

# Python runtime + artifact generation libraries
RUN apt-get update && apt-get install -y \
    python3 \
    python3-pip \
    python3-venv \
    && rm -rf /var/lib/apt/lists/*

RUN pip3 install --no-cache-dir --break-system-packages \
    pandas \
    openpyxl \
    python-pptx \
    matplotlib \
    plotly \
    kaleido \
    reportlab \
    Pillow \
    requests

# Install AWS CLI v2 (for S3 workspace access)
RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o /tmp/awscli.zip \
    && unzip /tmp/awscli.zip -d /tmp \
    && /tmp/aws/install \
    && rm -rf /tmp/awscli.zip /tmp/aws

# Install Claude Code CLI
RUN npm install -g @anthropic-ai/claude-code

# Set up app directory
WORKDIR /app
COPY package.json ./
RUN npm install --production

# Install Stitch MCP globally
RUN npm install -g stitch-mcp
COPY harness-server.js ./

# Create non-root user for Claude Code CLI compatibility (refuses to run as root)
RUN groupadd -r harness && useradd -r -g harness -m -d /home/harness harness
# Ensure npm global binaries (claude, stitch-mcp) are accessible to the new user
ENV PATH="/usr/local/bin:/usr/local/lib/node_modules/.bin:${PATH}"
RUN chown -R harness:harness /app

# Create workspace mount point (EFS will mount here)
RUN mkdir -p /workspace

# Expose HTTP shim port
EXPOSE 3000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:3000/health || exit 1

USER harness
CMD ["node", "harness-server.js"]
