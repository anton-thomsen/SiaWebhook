# Pre-Deployment Security Hardening Plan

**Status:** Implemented in code on 2026-06-14. Created 2026-06-14.
**Goal:** Close the blockers found in the pre-production security + deployment-readiness scan before SiaWebhook handles **real, live panic-alarm data** on the VPS (Coolify/Docker).
**Scope of this doc:** the **6 original blockers** plus the follow-up parking-lot items now implemented below.

> This is a life-safety-adjacent system: a dropped or misrouted panic alarm is a safety failure, not just a bug. Treat the core path (`/zepcam-panic` → SIA receiver) as the highest-stakes code.

---

## Cross-cutting decision (read this first — it shapes blockers 4 and 5)

**Sensitive and operational config is env-managed on the VPS, not editable through the web UI.**
The receiver **IP/port** and the **encryption key** are set as environment variables on the VPS (Coolify secrets / `.env`), persist across restarts, and are never writable (and the key never readable) through the admin panel. This single decision fixes blocker #4, blocker #5, and the old "settings don't survive restart" problem at once. The web UI becomes a read-only dashboard for the dangerous fields (it can still edit harmless ones like event code if desired).

**Terminology note (for future reference):** config held only in the running process's RAM is *in-memory* / *in-process* state, and it is **lost on container restart**. That's why secrets live in env vars (persistent, re-injected by Coolify on each start), not in memory-only state.

---

## Build order (test after each step; keep the test suite green)

`6 → 3 → 2 → 1 → 5 → 4`

Rationale: 6 and 3 are small and self-contained. 2 before 1 because 1's failure handling depends on 2's success/failure result type. 5 and 4 are the env-config pair (backend + frontend) — do them together last.

---

## Blocker 1 — Stop swallowing failed alarms (retry + dead-letter + alert + correct status)

**The problem (confirmed):** `Program.cs:145-146` discards the result of `SendAlarmAsync` and always returns HTTP 200 "Processed". `SiaTransmitter.cs:40-49` catches every socket error/timeout and *returns* an `"ERROR: …"` string instead of throwing. So if the SIA receiver is down, Zepcam gets a 200 for a panic that was never delivered, and the only trace is one log line.

**Definitions:**
- **Retry** — re-attempt delivery of the *same* alarm before giving up (handles brief receiver blips).
- **Dead-letter** — when retries are exhausted, persist the alarm to a durable store (file on a mounted volume that survives restarts) so it's not lost.
- **Out-of-band alert** — at dead-letter time, notify a human through a channel that does NOT depend on the failed SIA path.

**Retry design (two layers):**
1. **Synchronous fast retries** inside the webhook request: attempt 1 immediately, then up to 2 more with short backoff (~2s, then ~4s). Worst case ~10–12s total.
   - Any attempt succeeds → return **200** to Zepcam.
   - All three fail → **dead-letter + fire alert + return 502** (so Zepcam's own retry, if any, also engages).
2. **Background slow retries**: a hosted background service re-attempts dead-lettered alarms on a slower cadence (e.g. every 30s for a few minutes) in case the outage is transient — while the alerted humans are already responding. Never a substitute for the alert; a safety net on top.

**Out-of-band alert channel: n8n webhook fan-out (DECIDED).**
SiaWebhook fires ONE `POST` to an n8n webhook URL; n8n routes to the loud channel(s) + email backup and handles escalation.
- New env var `ALERT_WEBHOOK_URL` (secret — env-managed on the VPS).
- Notifier is behind an interface (`IAlertNotifier`) so it's mockable and swappable.
- Notifier is **fire-and-forget with its own short timeout + try/catch**: if n8n is unreachable, log loudly but NEVER let a failed alert throw and disrupt the webhook handler.
- **What to set up on the n8n side (user's part, not app code):** a Webhook trigger node with a hard-to-guess path → fan-out to SMS/push + email backup → optional "no human ack in 5 min → escalate" branch.
- **Security:** the alert body carries identity + live GPS (PII). Keep the n8n flow + channels private (no public Slack). Treat `ALERT_WEBHOOK_URL` as a secret.

**Alert / dead-letter payload contents** (we have the full Zepcam DTO in memory):
- Who/where: `user_name`, `user_externalid`, `issuer`, latitude, longitude, Zepcam timestamp.
- What we tried: the built SIA message, encrypted (yes/no), sequence number, account number.
- Why it failed: receiver IP:port, and the per-attempt error list with timestamps.

**Files:** `Services/SiaTransmitter.cs`, `Services/SiaAlarmService.cs`, `Program.cs` (`/zepcam-panic` handler), new dead-letter store, new `IAlertNotifier` (n8n), new background hosted retry service.

**Tests:** transmitter-failure path returns a failure result; handler returns 502 on failure / 200 on success; dead-letter file written on failure; notifier invoked on failure (mocked); background worker retries and clears on success.

---

## Blocker 2 — Verify the receiver actually ACKed

**The problem (confirmed):** `SiaTransmitter.cs:33-38` / `SiaAlarmService.cs:49-52` treat ANY TCP response not starting with `"ERROR:"` as success — including a DC-09 `NAK`/`DUH` (bad CRC, unknown account, auth fail). A rejected alarm is recorded as delivered.

**Plan:** parse the receiver response for the DC-09 `ACK` token. Treat `NAK` / `DUH` / unrecognized / empty as **failure**, feeding the SAME retry → dead-letter → alert path as Blocker 1. (This is why Blocker 1 changes the transmitter return type to a real success/failure result rather than a string: "transmitted but NAK'd" and "couldn't connect" both become failures.)

**Files:** `Services/SiaTransmitter.cs` / `Services/SiaAlarmService.cs`.

**Tests:** `ACK` → success; `NAK`/`DUH`/garbage/empty → failure (mocked transmitter in alarm-service tests).

---

## Blocker 3 — Validate & sanitize webhook fields before building the SIA frame

**The problem (confirmed):** `UserName`, `UserExternalId`, `Issuer`, `Latitude`, `Longitude` go from webhook JSON straight into the framed SIA message with no sanitization (`SiaAlarmService.cs:29-31`, `SiaMessageBuilder.cs:26-29`). Coordinates are only checked non-empty, not numeric. An attacker (or buggy upstream) can embed `\r \n [ ] | _` to break framing or inject blocks — verbatim on the wire in the unencrypted path. Compounding: the length field is only 3 hex digits (max 4095 bytes, `SiaMath.cs:24`) and there's no field-length cap, so a long field overflows it and corrupts framing.

**Plan:**
1. Latitude/Longitude must match a numeric/decimal pattern — reject non-numeric.
2. `UserName`, `UserExternalId`, `Issuer`: **reject** (don't silently strip — fail loud for an alarm system) if they contain any SIA delimiter/control char (`\r \n [ ] | _`); cap each to a sane max length (**proposed 64 chars each**, tune as needed).
3. Guard the total payload against the **4095-byte ceiling** in `SiaMath` so it can't overflow.
4. Return **400** with a clear message on any violation.

**Files:** `Program.cs` (webhook validation) and/or `Services/SiaMessageBuilder.cs`, `Services/SiaMath.cs`.

**Tests:** injection chars in fields → 400; non-numeric coordinates → 400; over-length fields → 400; over-ceiling total → handled; valid payload still builds the correct frame.

---

## Blocker 4 — Receiver config can't be changed to divert alarms

**The problem (confirmed):** `PUT /api/settings` (`Program.cs:115-120`) accepts an arbitrary `IpAddress`/`Port` with no server-side validation (the only check is client-side in `settings-form.tsx`, trivially bypassed). The real alarm path sends to whatever is stored, so a compromised admin session can silently divert every panic alarm to an attacker's machine (and use `test-connection` as an internal port scanner).

**Plan (defense in depth — all three layers):**
1. **Make receiver IP/port (and encryption key) env-managed** on the VPS; remove them from the UI write path (show read-only; hide the key per #5).
2. **Server-side validation** as a backstop: valid IP, port in range, reject loopback/link-local/internal/metadata addresses.
3. **Deployment:** keep the admin panel OFF the public internet — reachable only via SSH tunnel / VPN (Tailscale/WireGuard) / Coolify access control / IP allowlist. Only `/zepcam-panic` is publicly routed. Document in README.

**Files:** `Services/SiaSettingsService.cs`, `Program.cs` (`PUT /api/settings`), `webapp` settings form, deployment config/docs.

**Tests:** `PUT` with malformed/loopback IP or out-of-range port → rejected. (Network isolation is a deployment step verified manually on the VPS, not unit-tested.)

---

## Blocker 5 — Stop returning the encryption key; expose `encryptionEnabled`

**The problem (confirmed):** `Get()` copies `EncryptionKey` (`SiaSettingsService.cs:37`) and even overrides it from the env var (`:45-47`); `GET /api/settings` returns `Get()` directly (`Program.cs:111-112`), so the AES key travels to the BFF and on to the browser, one "Show" click from plaintext — and it leaks the env-managed prod secret too.

**Plan:**
1. `Get()` (or a dedicated response DTO) **no longer includes `EncryptionKey`**; add an `encryptionEnabled` boolean (true when a key is configured via env).
2. UI shows "Encryption: configured / not configured" instead of a key field; remove the show/hide-key control.
3. Key stays **env-only** (`SIA_ENCRYPTION_KEY`), persistent across restarts, never echoed back. Write-once on the server; to change it, set a fresh key on both ends (VPS env + receiver) and redeploy — you never need to read the old one.

**Files:** `Services/SiaSettingsService.cs` (`Get()`), `Program.cs` (`GET /api/settings`), `webapp/src/lib/types.ts`, `webapp/src/app/settings-form.tsx`.

**Tests:** `GET /api/settings` response contains no key and includes `encryptionEnabled`; boolean correct with/without the env var set.

---

## Blocker 6 — Request body size limit on the webhook

**The problem (confirmed):** no `MaxRequestBodySize` anywhere; Kestrel default is 30 MB. The 10/min rate limit doesn't stop a single oversized body being buffered before validation.

**Plan:** apply an **8 KB (8192 bytes)** max request body size to the `/zepcam-panic` endpoint specifically. Legitimate payload is well under 1 KB; 8 KB gives headroom while making body-size DoS impossible. Pairs with the per-field caps from #3.

**Files:** `Program.cs` (`/zepcam-panic` endpoint).

**Tests:** >8 KB body → 413; normal body → processed.

---

## Parking lot — implemented on 2026-06-14

- `dotnet test` at repo root now runs the real test project because `SiaWebhookApi.Tests/SiaWebhookApi.Tests.csproj` is included in `SiaWebhookApi.slnx`.
- CI added in `.github/workflows/ci.yml`: .NET restore/build/test plus `npm ci`, `npm run lint`, and `npm run build`.
- SIA sequence counter persists to `SIA_SEQUENCE_FILE` (default `/app/data/sia-sequence.txt` in Docker) and wraps `9999 → 0001`.
- UI-editable harmless settings persist to `SIA_SETTINGS_FILE` on the mounted `/app/data` volume. Receiver IP/port and encryption key remain env-only.
- Forwarded headers are trusted only from `TRUSTED_PROXY_IPS` / `TRUSTED_PROXY_NETWORKS` unless `TRUST_ALL_FORWARDED_HEADERS=true` is explicitly set. Compose binds the API debug port to `127.0.0.1:8080` instead of publishing it on every interface.
- `Timestamp` is validated: required, not older than 24 hours, and not more than 5 minutes in the future.
- JWT validation pins `ValidAlgorithms` to HS256; Production rejects weak admin passwords; Next.js sends defensive security headers and a report-only CSP; README documents the SameSite/CSRF assumptions.
- API Docker runtime now runs as a non-root user, base images are patch-pinned, compose has CPU/memory/PID limits, and webhook rate limiting defaults to 60/min/IP plus a queue of 20 to avoid throttling a legitimate multi-guard incident from one Zepcam NAT IP.

---

## Verified solid in the scan — don't re-investigate

Blazor fully removed (old bypass gap closed); `.env`/`.env.local` gitignored and untracked; JWT validation complete (issuer/audience/key/lifetime, HS256, 30s skew); constant-time comparison for both password and API key; prod fail-fast on missing secrets + 32-char JWT minimum; BFF cookie httpOnly+Secure+SameSite, token never in browser JS; AES-256-CBC zero-IV is **spec-mandated** by DC-09 and acceptable (always-varying plaintext); TCP timeouts present; .NET dependencies report zero vulnerabilities; npm has only 2 build-only moderate advisories (do NOT `audit fix --force` — it downgrades Next to 9.x).
