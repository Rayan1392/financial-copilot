FROM node:22-alpine AS build
WORKDIR /app

ARG VITE_FINANCIAL_COPILOT_API_BASE_URL
ENV VITE_FINANCIAL_COPILOT_API_BASE_URL=$VITE_FINANCIAL_COPILOT_API_BASE_URL

COPY src/frontend/package.json src/frontend/package-lock.json ./
RUN npm ci
COPY src/frontend/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY docker/frontend.nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/client /usr/share/nginx/html
EXPOSE 80

