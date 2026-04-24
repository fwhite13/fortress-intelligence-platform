FROM node:22-alpine
# Install LibreOffice for DOCX post-processing (field code resolution, PDF export)
RUN apk add --no-cache \
    libreoffice \
    libreoffice-writer \
    font-noto \
    font-noto-cjk \
    ttf-dejavu \
    fontconfig
WORKDIR /app
COPY services/proposal-generator/package*.json ./
RUN npm ci --only=production
COPY services/proposal-generator/src ./src
EXPOSE 3000
CMD ["node", "src/server.js"]
