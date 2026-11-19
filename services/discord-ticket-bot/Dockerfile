FROM node:22-alpine
ENV NODE_ENV=production
WORKDIR /app
COPY package*.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY src ./src
RUN mkdir -p /app/transcripts && chown -R node:node /app
USER node
VOLUME ["/app/transcripts"]
CMD ["node", "src/index.js"]
