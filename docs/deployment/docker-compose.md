# Docker Compose Deployment

This deployment runs five containers:

- `frontend`: the TanStack Start web application, served by its bundled Cloudflare-Worker-compatible runtime;
- `api`: the public .NET API;
- `worker`: the background and RabbitMQ consumer service;
- `postgres`, `redis`, and `rabbitmq`: required stateful dependencies.

The official product name shown by the web application is **ساپیو - دستیار هوشمند بازار**.

## 1. Build the release images on a local build machine

The main `docker-compose.yml` is deliberately **runtime-only**. It never builds application
images and has `pull_policy: never`, so `docker compose up` on the server cannot contact
`mcr.microsoft.com`, npm, or Docker Hub for application images. This avoids the DNS timeout shown
when a server tries to build the API, Worker, or frontend itself.

Build on a machine with reliable internet access. The images must match the server CPU
architecture. For a usual Linux x86-64 server, use `linux/amd64` even when building on Windows or
an ARM workstation:

```bash
export IMAGE_TAG=2026.07.28-3
export TARGET_PLATFORM=linux/amd64

docker buildx build --platform "$TARGET_PLATFORM" --load -f docker/api.Dockerfile -t financial-copilot-api:$IMAGE_TAG .
docker buildx build --platform "$TARGET_PLATFORM" --load -f docker/worker.Dockerfile -t financial-copilot-worker:$IMAGE_TAG .
docker buildx build --platform "$TARGET_PLATFORM" --load \
  --build-arg VITE_FINANCIAL_COPILOT_API_BASE_URL=https://tseai.avidaweb.com \
  -f docker/frontend.Dockerfile -t financial-copilot-frontend:$IMAGE_TAG .
```

Use the real public API URL in the frontend build argument. It is compiled into the browser bundle.

If the local build machine has the same CPU architecture as the server, the equivalent Compose
command is available through the build-only override:

```bash
docker compose -f docker-compose.yml -f docker-compose.local-build.yml build
```

Use `buildx --platform` above when the architectures differ.

Pull the PostgreSQL, Redis, and RabbitMQ runtime images on the local build machine, then save all
six required images in one archive. This is required when the server has no registry access:

```bash
docker pull postgres:17-alpine
docker pull redis:7.4-alpine
docker pull rabbitmq:4-management-alpine
docker save --output financial-copilot-$IMAGE_TAG.tar \
  financial-copilot-api:$IMAGE_TAG \
  financial-copilot-worker:$IMAGE_TAG \
  financial-copilot-frontend:$IMAGE_TAG \
  postgres:17-alpine redis:7.4-alpine rabbitmq:4-management-alpine
```

Copy the archive, `docker-compose.yml`, `.env`, and any reverse-proxy configuration to the server:

```bash
scp -P 22033 financial-copilot-$IMAGE_TAG.tar \
  root@185.126.203.173:/opt/sapio/financial-copilot/
scp -P 22033 docker-compose.yml root@185.126.203.173:/opt/sapio/financial-copilot/
scp -P 22033 .env root@185.126.203.173:/opt/sapio/financial-copilot/
```

Do not copy `docker-compose.local-build.yml` to the server. It is only for a build machine.
Connect to the server with `ssh -p 22033 deploy@SERVER`.

## 2. Prepare the server

Install Docker Engine and the Docker Compose plugin. Open only the ports required by your
deployment:

- frontend: `8080` by default;
- API: `5074` by default, only when it must be reachable directly;
- RabbitMQ management: `15672`, preferably restricted to administrators or a private network.

For public production traffic, put a TLS reverse proxy such as Caddy, Nginx, or Traefik in front
of the frontend and API. Set the two public HTTPS origins before building the frontend image.

## 3. Configure secrets and public URLs

From the repository root, create the ignored deployment file:

```bash
cp .env.docker.example .env
```

Edit `.env` and set strong, unique values for `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, and
`JWT_SIGNING_KEY`. Set both public URLs correctly:

```text
FRONTEND_PUBLIC_ORIGIN=https://tse.avidaweb.com
FRONTEND_API_BASE_URL=https://tseai.avidaweb.com
```

`FRONTEND_API_BASE_URL` is embedded into the frontend image during its build, so rebuild the
frontend whenever this address changes. Do not use the internal Docker hostname `api` here: the
browser cannot resolve it.

Set `IMAGE_TAG` to the exact tag used during the local image build, for example:

```text
IMAGE_TAG=2026.07.28-1
```

Set `OPENAI_API_KEY` and any enabled provider credentials only in `.env` or your server secret
manager. Keep `NADPCO_SCHEDULED_SYNC_ENABLED=false` until provider credentials and data-sync
operations have been validated.

## 4. Load and start all services on the server

On the server, load the transferred archive before starting Compose:

```bash
cd /opt/sapio/financial-copilot
docker load --input financial-copilot-2026.07.28-3.tar
docker compose up -d --no-build
docker compose ps
```

Replace the archive name with the release tag you built. If an image was not loaded, Compose fails
immediately instead of trying to fetch it from the internet.

The API applies EF Core migrations at startup. Start only one API replica during migration. After
the API is healthy, the Worker starts consuming RabbitMQ messages with the bounded
`DATA_SYNC_CONSUMER_COUNT` configured in `.env`.

## 5. Verify each project

### API

Verify the API container and health endpoint:

```bash
docker compose logs --tail=100 api
curl -f http://127.0.0.1:5074/health
```

The API listens inside Docker on port `8080` and is mapped to `API_PORT` on the server.

### Worker

Check that the Worker connects to RabbitMQ and starts the configured competing consumers:

```bash
docker compose logs --tail=100 worker
```

Look for the startup message reporting the data-sync consumer count and queue name. Do not raise
`DATA_SYNC_CONSUMER_COUNT` without considering upstream-provider rate limits and database load.

### Frontend

Open `http://SERVER_IP:8080`, or your configured HTTPS frontend domain. Confirm that browser
requests go to `FRONTEND_API_BASE_URL`, and that this origin appears in `FRONTEND_PUBLIC_ORIGIN`
for API CORS. The frontend is server-rendered by the TanStack Start Worker build; it is not a
static Nginx site.

## 6. Operate and update

View all logs:

```bash
docker compose logs -f
```

For each release, build and export a new image archive on the local build machine, transfer it,
then load and recreate services on the server:

```bash
docker load --input financial-copilot-NEW_TAG.tar
IMAGE_TAG=NEW_TAG docker compose up -d --no-build --remove-orphans
```

Persistent PostgreSQL, Redis, and RabbitMQ data use named Docker volumes. Do not run
`docker compose down -v` in production unless you intend to delete all persisted data.

## 7. Backups and security

- Back up PostgreSQL regularly with `pg_dump` and test restoration.
- Restrict or disable the RabbitMQ management port from public networks.
- Terminate TLS at a reverse proxy and keep `FRONTEND_PUBLIC_ORIGIN` and
  `FRONTEND_API_BASE_URL` on HTTPS.
- Rotate all credentials that may have appeared in prior local configuration files before a
  production deployment.
- Keep `.env` out of source control; it is ignored by this repository.
