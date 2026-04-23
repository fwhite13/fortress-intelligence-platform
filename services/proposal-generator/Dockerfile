FROM node:22-alpine
WORKDIR /app
COPY services/proposal-generator/package*.json ./
RUN npm ci --only=production
COPY services/proposal-generator/src ./src
EXPOSE 3000
CMD ["node", "src/server.js"]
