FROM node:22-bookworm-slim AS build
WORKDIR /app

ARG VITE_FINANCIAL_COPILOT_API_BASE_URL
ENV VITE_FINANCIAL_COPILOT_API_BASE_URL=$VITE_FINANCIAL_COPILOT_API_BASE_URL

COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY src/frontend/ ./
RUN npm run build

FROM node:22-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production

# This TanStack Start build targets Cloudflare Workers. It emits a Worker server bundle in
# dist/server and client assets in dist/client; it does not emit a standalone index.html for Nginx.
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
EXPOSE 80
CMD ["./node_modules/.bin/wrangler", "dev", "--config", "dist/server/wrangler.json", "--ip", "0.0.0.0", "--port", "80", "--local"]

