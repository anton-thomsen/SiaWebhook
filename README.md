# SIA Webhook

Bridges **Zepcam body-camera panic webhooks** to a **SIA DC-09 alarm receiver** over TCP.

## Architecture

Two independently deployable services:

```
                         INTERNET
   ┌──────────┐  HTTPS POST          ┌──────────────────────────┐
   │  Zepcam  │ ───────────────────► │  api  (.NET 10)          │   TCP    ┌──────────────┐
   └──────────┘  /zepcam-panic       │  - webhook → SIA pipeline │ ───────► │ SIA receiver │
                  (Bearer API key)    │  - JWT settings API       │  55001   └──────────────┘
   ┌──────────┐  HTTPS (admin)       │                          │
   │  Browser │ ───────────────────► │  web  (Next.js)          │
   └──────────┘  cookie session      │  - admin UI + BFF        │ ──► api (internal network)
                                      └──────────────────────────┘
```

- **`web/` (Next.js 16, App Router)** — the admin UI. Uses a **BFF pattern**: the browser
  holds only an httpOnly session cookie; Next.js route handlers attach the JWT to internal
  calls to the API. The token never reaches browser JavaScript.
- **`SiaWebhookApi/` (.NET 10)** — the SIA pipeline + JSON API. No UI (Blazor removed in
  favour of Next.js).

## Auth model

| Surface | Mechanism |
|---|---|
| `POST /zepcam-panic` | Static `Authorization: Bearer <ZEPCAM_API_KEY>` |
| `POST /api/auth/login` | Admin password → HS256 JWT (8h), rate-limited 5/min/IP |
| `GET/PUT /api/settings`, `/api/settings/test-connection` | JWT bearer |
| Admin UI session | httpOnly + Secure + SameSite cookie holding the JWT |

Secrets come from environment variables. In **Production** the API refuses to start if any
are missing or weak. In **Development** it falls back to insecure defaults with a loud warning.

| Variable | Purpose |
|---|---|
| `SIA_JWT_SECRET` | JWT signing key (≥32 chars) |
| `SIA_ADMIN_PASSWORD` | Admin login password (Production requires 12+ chars with upper/lower/digit/symbol) |
| `ZEPCAM_API_KEY` | Webhook shared secret |
| `SIA_ENCRYPTION_KEY` | Optional AES-256 key (32 ASCII chars) |
| `ALERT_WEBHOOK_URL` | Optional n8n webhook for dead-letter alarm alerts |
| `SIA_RECEIVER_IP`, `SIA_RECEIVER_PORT` | Env-only receiver routing config |
| `SIA_SETTINGS_FILE` | Durable UI-editable settings file path |
| `SIA_SEQUENCE_FILE` | Durable SIA sequence counter path |
| `SIA_DEAD_LETTER_DIR` | Durable dead-letter alarm directory |
| `TRUSTED_PROXY_IPS`, `TRUSTED_PROXY_NETWORKS` | Comma-separated reverse proxy sources trusted for `X-Forwarded-*` |

## Local development

```bash
# API (terminal 1)
cd SiaWebhookApi
SIA_JWT_SECRET=dev-secret-0123456789ABCDEF-0123456789 \
SIA_ADMIN_PASSWORD='Admin12345!' ZEPCAM_API_KEY=zepcam-local-key \
dotnet run                       # http://localhost:5257

# Web (terminal 2)
cd webapp
cp .env.example .env.local       # API_BASE_URL=http://localhost:5257
npm install && npm run dev       # http://localhost:3000
```

## Docker / Coolify

```bash
cp .env.example .env             # fill in the secrets
docker compose up --build        # web :3000, api :8080
```

For **Coolify**: deploy the two services from this compose file. Give `web` your public
admin domain, ideally behind VPN/IP allow-list/Coolify access control. Route only the
public webhook URL (`…/zepcam-panic`) to `api`. The `web → api` hop stays on the internal
Docker network via `API_BASE_URL=http://api:8080`.

The compose file binds the API's local debug port to `127.0.0.1:8080` only. On Coolify,
do not publish the API container directly to the internet. Configure `TRUSTED_PROXY_IPS`
or `TRUSTED_PROXY_NETWORKS` for the Coolify/Traefik source(s) before relying on
`X-Forwarded-For` for rate limiting/logging.

> **API container:** in this repo, `api` is the Docker container running the .NET backend.
> It receives Zepcam webhooks, talks TCP to the SIA receiver, and serves the internal
> settings API. It is separate from the `web` container, which runs the Next.js admin UI.

> **Durable runtime state:** the API writes harmless UI settings, the SIA sequence counter,
> and dead-letter alarms under `/app/data`, backed by the `api-data` Docker volume. Receiver
> IP/port and the encryption key stay env-only and are never editable through the UI.

> **SameSite / CSRF:** the admin session cookie is `httpOnly`, `Secure` in Production, and
> `SameSite=Lax`. That blocks normal cross-site POSTs from carrying the cookie, and the app
> does not enable browser CORS for credentialed cross-origin API calls. If the admin UI ever
> needs third-party embedding or cross-site integrations, add explicit CSRF tokens before
> loosening those cookie/header assumptions.

## Tests

```bash
dotnet test        # API + test project are both in SiaWebhookApi.slnx
cd webapp && npm run lint && npm run build
```
