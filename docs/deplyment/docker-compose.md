# Docker Compose Deployment

This deployment runs five containers:

- `frontend`: the web application, served by Nginx;
- `api`: the public .NET API;
- `worker`: the background and RabbitMQ consumer service;
- `postgres`, `redis`, and `rabbitmq`: required stateful dependencies.

The official product name shown by the web application is **ساپیو - دستیار هوشمند بازار**.

## 1. Prepare the server

Install Docker Engine and the Docker Compose plugin. Open only the ports required by your
deployment:

- frontend: `8080` by default;
- API: `5074` by default, only when it must be reachable directly;
- RabbitMQ management: `15672`, preferably restricted to administrators or a private network.

For public production traffic, put a TLS reverse proxy such as Caddy, Nginx, or Traefik in front
of the frontend and API. Set the two public HTTPS origins before building the frontend image.

## 2. Configure secrets and public URLs

From the repository root, create the ignored deployment file:

```bash
cp .env.docker.example .env
```

Edit `.env` and set strong, unique values for `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, and
`JWT_SIGNING_KEY`. Set both public URLs correctly:

```text
FRONTEND_PUBLIC_ORIGIN=https://app.example.com
FRONTEND_API_BASE_URL=https://api.example.com
```

`FRONTEND_API_BASE_URL` is embedded into the frontend image during its build, so rebuild the
frontend whenever this address changes. Do not use the internal Docker hostname `api` here: the
browser cannot resolve it.

Set `OPENAI_API_KEY` and any enabled provider credentials only in `.env` or your server secret
manager. Keep `NADPCO_SCHEDULED_SYNC_ENABLED=false` until provider credentials and data-sync
operations have been validated.

## 3. Build and start all services

Run from the repository root:

```bash
docker compose build
docker compose up -d
docker compose ps
```

The API applies EF Core migrations at startup. Start only one API replica during migration. After
the API is healthy, the Worker starts consuming RabbitMQ messages with the bounded
`DATA_SYNC_CONSUMER_COUNT` configured in `.env`.

## 4. Verify each project

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
for API CORS.

## 5. Operate and update

View all logs:

```bash
docker compose logs -f
```

After pulling application changes, rebuild and recreate services:

```bash
docker compose build
docker compose up -d --remove-orphans
```

Persistent PostgreSQL, Redis, and RabbitMQ data use named Docker volumes. Do not run
`docker compose down -v` in production unless you intend to delete all persisted data.

## 6. Backups and security

- Back up PostgreSQL regularly with `pg_dump` and test restoration.
- Restrict or disable the RabbitMQ management port from public networks.
- Terminate TLS at a reverse proxy and keep `FRONTEND_PUBLIC_ORIGIN` and
  `FRONTEND_API_BASE_URL` on HTTPS.
- Rotate all credentials that may have appeared in prior local configuration files before a
  production deployment.
- Keep `.env` out of source control; it is ignored by this repository.
