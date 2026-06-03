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
admin domain. Route the public webhook URL (`…/zepcam-panic`) to `api`. The `web → api`
hop stays on the internal Docker network via `API_BASE_URL=http://api:8080`; Coolify's
Traefik handles TLS and `X-Forwarded-For` (the API already trusts forwarded headers).

> **Settings persistence:** the API persists UI edits by rewriting `appsettings.json`,
> which is **ephemeral in a container** — changes are lost on restart. For durable config,
> set the `SiaSettings__*` environment variables (see `.env.example`); these override
> `appsettings.json` at startup. Making in-container UI edits durable (a mounted data
> volume) is a known follow-up.

## Tests

```bash
dotnet test        # 35 unit tests (CRC, crypto, message builder, alarm service)
```
