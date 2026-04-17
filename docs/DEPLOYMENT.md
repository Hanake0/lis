# Deployment Guide

Production replication guide for Lis on a single Linux host, reachable over a public domain.

> For local development, see the root `README.md` Quick Start — it uses the repo's bundled `docker-compose.yml` with a local Postgres container. This guide is for **running Lis as an always-on service** with TLS and managed data.

## Topology

Two Docker Compose stacks sharing a `proxy` bridge network:

| Stack | Services | Role |
|---|---|---|
| `services/` | `caddy`, `gowa` | TLS reverse proxy + WhatsApp gateway |
| `lis/` | `lis` | .NET 10 agent (Semantic Kernel + Anthropic) |

Data flow:

```
WhatsApp <──> GOWA ──webhook──> Lis (Anthropic, tools)
                ▲
                │ https
              Caddy ── your.domain.com
```

**Persistent data lives on managed Postgres (Neon recommended):** both the `lis` application database and the `gowa` session database. Nothing is stored in a local Postgres container.

## Final directory layout

```
<deploy-root>
├── services/
│   ├── docker-compose.yml
│   ├── Caddyfile
│   ├── .env
│   ├── config/              # Caddy config volume (created by Caddy)
│   ├── data/                # Caddy data volume — TLS certs live here
│   └── gowa-data/           # GOWA WhatsApp session (IMPORTANT — back this up)
└── lis/
    ├── docker-compose.yml
    ├── .env
    └── lis/                 # git clone of this repo
        └── Lis.Api/Dockerfile
```

---

## Prerequisites

On the target host:

- Linux with Docker Engine + Compose v2 (`docker compose version` must work).
- Public IP, with a DNS A/AAAA record pointing your chosen domain at it.
- Ports **80/tcp**, **443/tcp**, **443/udp** open to the internet (Caddy ACME + HTTP/3).

Accounts / keys:

- **[Neon](https://neon.tech)** (or any managed Postgres with `pgvector`) — create a project, then create two databases with separate roles:
  - database `lis`, role `lis` (used by the Lis app — requires `vector` extension)
  - database `gowa`, role `gowa` (used by GOWA)
  - For each, copy the pooled connection string.
- **Anthropic** credentials — an API key (`sk-ant-api03-...`) or a long-lived OAuth token from `claude setup-token` (`sk-ant-oat01-...`).
- *(optional)* **OpenAI** API key — only if you want vector search over memories.
- *(optional)* **Brave Search** API key — only if you want the `web_search` tool.

---

## Step 1 — Create `services/`

```bash
mkdir -p services/{config,data,gowa-data}
cd services
```

### `services/docker-compose.yml`

```yaml
services:
  caddy:
    image: caddy:2-alpine
    restart: always
    ports:
      - "80:80"
      - "443:443"
      - "443:443/udp"
    networks:
      - proxy
    env_file:
      - .env
    volumes:
      - ./config:/config/caddy
      - ./data:/data/caddy
      - ./Caddyfile:/etc/caddy/Caddyfile:ro

  gowa:
    image: aldinokemal2104/go-whatsapp-web-multidevice:v8.3.3
    restart: always
    networks:
      - proxy
    env_file:
      - .env
    volumes:
      - ./gowa-data:/app/storages

networks:
  proxy:
    name: proxy
    driver: bridge
```

Notes:
- This file **creates** the `proxy` network (not marked `external` here). The Lis stack attaches to it as `external`.
- `gowa` publishes no host ports; it's reached via the Docker network as `gowa:3000` (and through Caddy from the public side).
- **Pin the GOWA image tag** — upgrades can break the webhook payload shape.

### `services/Caddyfile`

```caddyfile
{
    email YOUR_ACME_EMAIL@example.com
}

{$GOWA_DOMAIN} {
    reverse_proxy gowa:{$APP_PORT}
}
```

Replace `YOUR_ACME_EMAIL@example.com` with the address Let's Encrypt should register against. `{$GOWA_DOMAIN}` and `{$APP_PORT}` come from `services/.env`.

### `services/.env`

Generate your own secrets for the marked fields (`openssl rand -hex 16`). `WHATSAPP_WEBHOOK_SECRET` and `APP_BASIC_AUTH` **must match** the matching keys in `lis/.env`.

```env
# Caddy
GOWA_DOMAIN=your.domain.com

# GOWA — App
APP_PORT=3000
APP_HOST=0.0.0.0
APP_DEBUG=false
APP_OS=Chrome
# Basic auth (user1:pass1,user2:pass2) — protects the GOWA web UI and REST API.
APP_BASIC_AUTH=lis:CHANGE_ME_STRONG_PASSWORD
# Base path for subpath deployment (e.g. /whatsapp) — leave empty for root.
APP_BASE_PATH=
APP_TRUSTED_PROXIES=

# GOWA — Database (managed Postgres)
# Paste the pooled connection string for the `gowa` database.
DB_URI=postgres://gowa:PASSWORD@HOST/gowa?sslmode=require&channel_binding=require

# GOWA — WhatsApp
WHATSAPP_AUTO_REPLY=false
WHATSAPP_AUTO_MARK_READ=false
WHATSAPP_AUTO_DOWNLOAD_MEDIA=true
WHATSAPP_ACCOUNT_VALIDATION=true
WHATSAPP_PRESENCE_ON_CONNECT=available
WHATSAPP_AUTO_REJECT_CALL=true

# GOWA — Webhook (points at the Lis container on the proxy network)
WHATSAPP_WEBHOOK=http://lis:3010/webhook/whatsapp
WHATSAPP_WEBHOOK_SECRET=CHANGE_ME_WEBHOOK_SECRET
WHATSAPP_WEBHOOK_EVENTS=message
WHATSAPP_WEBHOOK_INSECURE_SKIP_VERIFY=true

# GOWA — Chatwoot (disabled)
CHATWOOT_ENABLED=false
```

---

## Step 2 — Clone Lis and create `lis/`

```bash
mkdir -p lis
cd lis
git clone <repo-url> lis   # clones into ./lis/
```

### `lis/docker-compose.yml`

```yaml
services:
  lis:
    mem_limit: 512m
    build:
      context: ./lis/
      dockerfile: ./Lis.Api/Dockerfile
    container_name: lis
    restart: unless-stopped
    env_file: .env
    # pid: host + privileged are ONLY required when LIS_EXEC_HOST=true.
    # Drop both if you don't need the agent to shell out to the host.
    pid: host
    privileged: true
    networks:
      - proxy
    ports:
      - "3010"

networks:
  proxy:
    external: true
```

Notes:
- `container_name: lis` matters — GOWA's webhook URL (`http://lis:3010/...`) resolves by container name inside the `proxy` network.
- `pid: host` + `privileged: true` are required **only** because `LIS_EXEC_HOST=true` lets the agent shell out to the host via `nsenter`. If you don't want that capability, set `LIS_EXEC_HOST=false` and drop both fields — safer container.
- `networks.proxy.external: true` — this stack fails to start if the services stack hasn't created the `proxy` network yet. Always bring `services` up first.
- `ports: - "3010"` (no left side) publishes 3010 to a random host port for optional debugging — not needed for the webhook flow.

### `lis/.env`

Start from the canonical [`.env.example`](../.env.example) at the repo root. Values that differ on a production host:

```env
# App — memory-tuned GC for the 512m container limit
ASPNETCORE_URLS=http://+:3010
DOTNET_gcServer=0
DOTNET_GCHeapHardLimit=0x10000000
DOTNET_GCDynamicAdaptationMode=1

# Database — managed Postgres connection string (pgvector required)
DATABASE_URL=Host=HOST;Database=lis;Username=lis;Password=PASSWORD;SSL Mode=Require;Channel Binding=Require

# Channel — GOWA (public URL via Caddy)
GOWA_ENABLED=true
GOWA_BASE_URL=https://your.domain.com
GOWA_DEVICE_ID=lis
# MUST equal APP_BASIC_AUTH in services/.env
GOWA_BASIC_AUTH=lis:CHANGE_ME_STRONG_PASSWORD
# MUST equal WHATSAPP_WEBHOOK_SECRET in services/.env
GOWA_WEBHOOK_SECRET=CHANGE_ME_WEBHOOK_SECRET

# Shell execution on the host (requires pid:host + privileged in compose)
LIS_EXEC_HOST=true
```

Every other key — model, context budget, compaction tuning, memory embeddings, etc. — lives in [`.env.example`](../.env.example). Copy the example and override only what's different for your deployment.

---

## Step 3 — Boot order

The **services stack must come up first**, because it owns the `proxy` network that the Lis stack declares as external.

```bash
cd services
docker compose up -d

cd ../lis
docker compose up -d --build       # first run builds the .NET image (slow — pulls Playwright/Chromium)
```

Verify three containers are running:

```bash
docker ps
# Expected: services-caddy-1, services-gowa-1, lis — all Up
```

Tail logs during first boot:

```bash
docker logs services-caddy-1 -f     # ACME issues a cert for GOWA_DOMAIN
docker logs services-gowa-1 -f      # DB connect, HTTP server up
docker logs lis -f                  # EF migrations, Anthropic client init
```

---

## Step 4 — Database migrations (Lis)

Lis uses EF Core migrations; the Lis container applies them on startup. On the first boot, confirm they succeeded by tailing `docker logs lis -f` until you see `Application started`.

For manual control (requires the .NET 10 SDK and `dotnet-ef` on the host):

```bash
dotnet tool install --global dotnet-ef --version 10.*      # once, if missing

cd lis/lis
dotnet ef database update \
  --project Lis.Persistence/Lis.Persistence.csproj \
  --startup-project Lis.Api/Lis.Api.csproj
```

GOWA handles its own schema creation on first connect — no manual migration needed.

---

## Step 5 — Pair WhatsApp (GOWA)

1. Browse to `https://<GOWA_DOMAIN>/`.
2. Basic auth prompt — log in with the credentials you set in `APP_BASIC_AUTH`.
3. From the GOWA UI choose **Login** / **Scan QR**.
4. On your phone: WhatsApp → Settings → Linked devices → Link a device → scan.
5. The session is persisted to `services/gowa-data/`. **Back that directory up** — losing it means re-pairing.

---

## Step 6 — Smoke test

Send yourself a WhatsApp message (from any chat you own, to your `LIS_OWNER_JID`). In another terminal:

```bash
docker logs lis -f
```

You should see the webhook arrive, the conversation service invoke Anthropic, and a reply get posted back through GOWA.

---

## Secret-matrix cheat sheet

Values shared between the two `.env` files that **must be identical**:

| `services/.env` | `lis/.env` | Purpose |
|---|---|---|
| `APP_BASIC_AUTH` | `GOWA_BASIC_AUTH` | Lis authenticates to GOWA's REST API |
| `WHATSAPP_WEBHOOK_SECRET` | `GOWA_WEBHOOK_SECRET` | HMAC on inbound webhook |
| `GOWA_DOMAIN` | host portion of `GOWA_BASE_URL` | Public URL Lis calls back on |

Everything else is independent.

---

## Backup / migration

- **Managed Postgres** holds both databases — PITR is managed server-side, nothing to back up locally.
- **`services/gowa-data/`** — WhatsApp session. Tar before migrating hosts to skip re-pairing.
- **`services/data/`** — Caddy ACME state. Copy across hosts to avoid Let's Encrypt rate limits (optional; Caddy will re-issue on a fresh host).
- **`.env` files** — the only copies of your secrets. Back them up out-of-band (password manager, encrypted vault).

## Gotchas

- `GOWA_BASE_URL` in `lis/.env` points at the **public HTTPS** URL (through Caddy), not `http://gowa:3000`. Deliberate — keeps the basic-auth + TLS flow consistent — but means Lis→GOWA traffic exits the host and comes back in through Caddy.
- The `proxy` network is non-external in **services** and external in **lis**. If you `docker compose down` the services stack, the Lis container refuses to start until it's back.
- Lis's container is memory-constrained (`mem_limit: 512m`); the GC env vars exist to respect that limit. Raising `mem_limit` without also relaxing `DOTNET_GCHeapHardLimit` is a footgun — raise both or neither.
- `LIS_EXEC_HOST=true` gives the agent shell access to the host. That's why the compose file has `pid: host` + `privileged: true`. If you don't want that, flip the env var to `false` and drop both flags.
- Only the `default` agent is seeded automatically. Create additional agents at runtime with `/agent new <name> [display]`, or insert rows directly in the `agent` table if you prefer.
