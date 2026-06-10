FROM public.ecr.aws/docker/library/node:20-slim

# Install LibreOffice 24.x from TDF (The Document Foundation) — Debian apt ships 7.x which
# does not correctly honour custom paperWidth/paperHeight from XLSX pageSetup.
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    gnupg \
    ca-certificates \
    libgl1 \
    libglib2.0-0 \
    libsm6 \
    libxrender1 \
    libxext6 \
    libxinerama1 \
    libfontconfig1 \
    libfreetype6 \
    libcairo2 \
    && rm -rf /var/lib/apt/lists/* \
    && wget -q https://download.documentfoundation.org/libreoffice/stable/24.2.7/deb/x86_64/LibreOffice_24.2.7_Linux_x86-64_deb.tar.gz -O /tmp/lo.tar.gz \
    && tar -xzf /tmp/lo.tar.gz -C /tmp \
    && dpkg -i /tmp/LibreOffice_24.2.7*/DEBS/*.deb \
    && rm -rf /tmp/lo.tar.gz /tmp/LibreOffice_24.2.7* \
    && ln -sf /opt/libreoffice24.2/program/soffice /usr/local/bin/libreoffice \
    && ln -sf /opt/libreoffice24.2/program/soffice /usr/local/bin/soffice

WORKDIR /app
COPY package.json ./
RUN npm install --production
COPY server.js ./

EXPOSE 3001
CMD ["node", "server.js"]
