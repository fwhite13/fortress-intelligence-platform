# node:20 (full bookworm) instead of node:20-slim — LO 24.8 has deep system lib deps
# (libdbus-1, libcups2, libx11, libpango, libgtk, etc.) that slim strips out.
# Easier to start full than chase missing libs one build at a time.
FROM public.ecr.aws/docker/library/node:20

# Install LibreOffice from TDF archive — Debian bookworm apt ships 7.x which does not
# correctly honour custom paperWidth/paperHeight from XLSX pageSetup. TDF ships 24.x.
RUN apt-get update && apt-get install -y --no-install-recommends \
    wget \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && wget -q https://downloadarchive.documentfoundation.org/libreoffice/old/24.8.5.2/deb/x86_64/LibreOffice_24.8.5.2_Linux_x86-64_deb.tar.gz -O /tmp/lo.tar.gz \
    && tar -xzf /tmp/lo.tar.gz -C /tmp \
    && dpkg -i /tmp/LibreOffice_24.8.5.2*/DEBS/*.deb \
    && rm -rf /tmp/lo.tar.gz /tmp/LibreOffice_24.8.5.2* \
    && ln -sf /opt/libreoffice24.8/program/soffice /usr/local/bin/libreoffice \
    && ln -sf /opt/libreoffice24.8/program/soffice /usr/local/bin/soffice

WORKDIR /app
COPY package.json ./
RUN npm install --production
COPY server.js ./

EXPOSE 3001
CMD ["node", "server.js"]
