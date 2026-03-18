FROM node:20-alpine
WORKDIR /app
COPY package*.json ./
RUN npm ci --production
COPY dist/ ./dist/
COPY migrations/ ./migrations/
COPY cli/ ./cli/
ENV PORT=3100
EXPOSE 3100
CMD ["node", "dist/server.js"]
