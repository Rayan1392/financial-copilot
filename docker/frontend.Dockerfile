FROM node:22-bookworm-slim AS build
# The build stage mirrors the repository layout: the release history page bundles the repository-root
# CHANGELOG.md (src/frontend/changelog.vite-plugin.ts reads ../../CHANGELOG.md), and the build fails without it.
WORKDIR /repo/src/frontend

ARG VITE_FINANCIAL_COPILOT_API_BASE_URL
ARG VITE_FINANCIAL_COPILOT_INTERNAL_API_BASE_URL
# Feature 153: automatic phone redirects to /m. Build-time; off unless the build sets it to "true".
ARG VITE_MOBILE_AUTO_REDIRECT=false
ENV VITE_FINANCIAL_COPILOT_API_BASE_URL=$VITE_FINANCIAL_COPILOT_API_BASE_URL
ENV VITE_FINANCIAL_COPILOT_INTERNAL_API_BASE_URL=$VITE_FINANCIAL_COPILOT_INTERNAL_API_BASE_URL
ENV VITE_MOBILE_AUTO_REDIRECT=$VITE_MOBILE_AUTO_REDIRECT

COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY CHANGELOG.md /repo/CHANGELOG.md
COPY src/frontend/ ./
RUN npm run build

FROM node:22-bookworm-slim AS runtime
WORKDIR /app
ENV NODE_ENV=production

# This TanStack Start build targets Cloudflare Workers. It emits a Worker server bundle in
# dist/server and client assets in dist/client; it does not emit a standalone index.html for Nginx.
COPY --from=build /repo/src/frontend/node_modules ./node_modules
COPY --from=build /repo/src/frontend/dist ./dist
EXPOSE 80
CMD ["./node_modules/.bin/wrangler", "dev", "--config", "dist/server/wrangler.json", "--ip", "0.0.0.0", "--port", "80", "--local"]

