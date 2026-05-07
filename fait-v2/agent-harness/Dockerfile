FROM node:20-slim

# Install system dependencies
RUN apt-get update && apt-get install -y \
    curl \
    git \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Install AWS CLI v2 (for S3 workspace access)
RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o /tmp/awscli.zip \
    && apt-get install -y unzip \
    && unzip /tmp/awscli.zip -d /tmp \
    && /tmp/aws/install \
    && rm -rf /tmp/awscli.zip /tmp/aws

# Install Claude Code CLI
RUN npm install -g @anthropic-ai/claude-code

# Set up app directory
WORKDIR /app
COPY package.json ./
RUN npm install --production
COPY harness-server.js ./

# Create workspace mount point (EFS will mount here)
RUN mkdir -p /workspace

# Expose HTTP shim port
EXPOSE 3000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:3000/health || exit 1

CMD ["node", "harness-server.js"]
