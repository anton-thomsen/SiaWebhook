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
are missing. In **Development** it falls back to insecure defaults with a loud warning.

| Variable | Purpose |
|---|---|
| `SIA_JWT_SECRET` | JWT signing key (≥32 chars) |
| `SIA_ADMIN_PASSWORD` | Admin login password |
| `ZEPCAM_API_KEY` | Webhook shared secret |
| `SIA_ENCRYPTION_KEY` | Optional AES-256 key (32 ASCII chars) |
| `SIA_RECEIVER_IP`, `SIA_RECEIVER_PORT` | Env-only receiver routing — always win over stored settings, never editable via the UI |
| `SIA_SETTINGS_FILE` | Durable UI-editable settings file path |
| `SIA_SEQUENCE_FILE` | Durable SIA sequence counter path |
| `WEBHOOK_RATE_LIMIT_PER_MINUTE`, `WEBHOOK_RATE_LIMIT_QUEUE` | Webhook per-IP rate limit (default 60/min + queue of 20) |

## Local development

```bash
# API (terminal 1)
cd SiaWebhookApi
SIA_JWT_SECRET=dev-secret-0123456789ABCDEF-0123456789 \
SIA_ADMIN_PASSWORD=admin123 ZEPCAM_API_KEY=zepcam-local-key \
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
admin domain — ideally kept **off the public internet** (SSH tunnel, VPN such as
Tailscale/WireGuard, Coolify access control, or an IP allow-list). Route **only** the
public webhook URL (`…/zepcam-panic`) to `api`. The `web → api` hop stays on the internal
Docker network via `API_BASE_URL=http://api:8080`; Coolify's Traefik handles TLS and
`X-Forwarded-For` (the API already trusts forwarded headers).

> **Receiver routing is env-only:** `SIA_RECEIVER_IP` / `SIA_RECEIVER_PORT` (and
> `SIA_ENCRYPTION_KEY`) are read from the environment and always override stored
> settings. The admin UI shows them read-only — a hijacked admin session cannot
> divert panic alarms to another endpoint. To change them, update the VPS
> environment and redeploy.

> **Durable runtime state:** the API writes harmless UI settings, the SIA sequence
> counter, and dead-letter alarms under `/app/data`, backed by the `api-data` Docker
> volume — they survive container restarts. Receiver IP/port and the encryption key
> stay env-only and are never editable through the UI.

> **SameSite / CSRF:** the admin session cookie is `httpOnly`, `Secure` in Production,
> and `SameSite=Lax`. That blocks normal cross-site POSTs from carrying the cookie, and
> the app does not enable browser CORS for credentialed cross-origin calls. If the admin
> UI ever needs third-party embedding or cross-site integrations, add explicit CSRF
> tokens before loosening those cookie/header assumptions.

## Tests

```bash
dotnet test        # API + test project are both in SiaWebhookApi.slnx
cd webapp && npm run lint && npm run build
```
