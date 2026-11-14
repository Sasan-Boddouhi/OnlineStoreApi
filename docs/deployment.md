# Deployment Guide

This document describes the current containerized deployment model for `OnlineStoreApi`, including the Docker image, application configuration, SQL Server, Redis, production deployment flow, health verification, and rollback behavior.

> **Source of truth:** The repository Dockerfile, Docker Compose configuration, application configuration, and CI/CD workflow define the actual deployment behavior. This document should be updated whenever those mechanisms change.

## 1. Deployment Architecture

The production environment consists of three application containers:

```text
                         Production VM
                              |
              +---------------+---------------+
              |               |               |
              v               v               v
        +-----------+   +-----------+   +-----------+
        |    API    |   | SQL Server|   |   Redis   |
        | ASP.NET 8 |   |   2022    |   |    7      |
        +-----------+   +-----------+   +-----------+
              |               |               |
              +---------------+---------------+
                      Docker Network
```

The API connects to SQL Server and Redis through the Docker network using their service/container names.

## 2. Docker Image

The application is packaged as a multi-stage Docker image.

### Build stage

The Dockerfile uses:

```text
mcr.microsoft.com/dotnet/sdk:8.0
```

The project files are copied first so that `dotnet restore` can benefit from Docker layer caching. The complete source is then copied and the API project is published in Release configuration.

### Runtime stage

The runtime image uses:

```text
mcr.microsoft.com/dotnet/aspnet:8.0
```

Only the published application is copied into the runtime image.

The container runs with:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:80
```

The application listens on port `80` inside the container.

The final container runs as the non-root `app` user.

## 3. Docker Healthcheck

The Docker image defines a health check against:

```text
http://localhost:80/health/live
```

`/health/live` runs no dependency checks, so a short SQL Server or Redis outage does not mark the container unhealthy and does not restart the API. Use `/health/ready` for dependency readiness.

Current Docker health-check configuration:

```text
interval:     30s
timeout:       5s
start-period: 30s
retries:      3
```

The health endpoint itself reports application health and its registered dependency checks.

## 4. Local Docker Compose

The repository contains `docker-compose.yml` with three services:

```text
sqlserver
redis
api
```

The API is built from the repository Dockerfile:

```yaml
build:
  context: .
  dockerfile: Dockerfile
```

The API image is named:

```text
onlinestore-api:latest
```

The API is exposed as:

```text
5000:80
```

Therefore, from the host machine the API is reachable at port `5000` while the application listens on port `80` inside the container.

## 5. SQL Server

The Compose configuration uses:

```text
mcr.microsoft.com/mssql/server:2022-latest
```

SQL Server data is stored in the `sqlserver_data` Docker volume.

The API connects to the SQL Server service using the Docker service name:

```text
Server=sqlserver
```

The database name is:

```text
ShopDB
```

The SQL Server password is supplied through the `MSSQL_SA_PASSWORD` environment variable rather than being hardcoded in Compose.

**Batch 52:** in production the API no longer logs in as `sa`; it uses a least-privilege application login and migrations use a separate migration login. SQL Server runs as Express (`MSSQL_PID`) with 2 GB of memory and a health check. See Appendix B.

## 6. Redis

The Compose configuration uses:

```text
redis:7-alpine
```

Redis data is stored in the `redisdata` Docker volume.

A Redis health check runs:

```text
redis-cli ping
```

with:

```text
interval: 10s
timeout: 5s
retries: 5
```

The API connects to Redis using:

```text
redis:6379
```

## 7. Production Configuration

Production configuration is supplied through environment variables.

The deployment uses values including:

```text
ASPNETCORE_ENVIRONMENT
ASPNETCORE_URLS
Swagger__Enabled
ConnectionStrings__DefaultConnection
Jwt__Key
Jwt__Issuer
Jwt__Audience
Redis__Configuration
Redis__InstanceName
PaymentGatewaySettings__Provider
PaymentGatewaySettings__EnableRealGateway
PaymentGatewaySettings__MerchantId
PaymentGatewaySettings__WebhookSecret
PaymentGatewaySettings__CallbackUrl
CorsOrigins__0
AllowedHosts
ForwardedHeaders__KnownNetworks__0
ForwardedHeaders__KnownProxies__0
ForwardedHeaders__ForwardLimit
```

**Client IP behind a reverse proxy (F-20).** The API trusts `X-Forwarded-For` and `X-Forwarded-Proto` **only** from the addresses listed in `ForwardedHeaders__KnownProxies__N` (single IPs) or `ForwardedHeaders__KnownNetworks__N` (CIDR). From any other peer the headers are ignored, so a client cannot spoof its IP. Per-IP rate limits, `UserSession.IpAddress` and the rate-limit log lines all use this resolved IP.

- `docker-compose.prod.yml` maps them from `TRUSTED_PROXY_NETWORK` and `TRUSTED_PROXY_IP`. Set **one** of them on the deploy host.
- If nginx runs on the host and proxies to `localhost:5000`, Docker forwards the connection from the gateway of the compose network. Use that network's subnet, or the gateway IP alone (tighter):
  ```bash
  docker network inspect <project>_onlinestore-network --format '{{(index .IPAM.Config 0).Subnet}}'   # -> TRUSTED_PROXY_NETWORK
  docker network inspect <project>_onlinestore-network --format '{{(index .IPAM.Config 0).Gateway}}'  # -> TRUSTED_PROXY_IP
  ```
  If nginx runs as a container on the same network, use that subnet or the nginx container IP.
- The network must be written with its host bits zero (`172.18.0.0/16`, not `172.18.0.1/16`). A prefix of `/0` (`0.0.0.0/0`) means "trust everyone" and makes start-up fail.
- `ForwardLimit` (default `1`) is the number of proxies in front of the API. With one nginx it stays `1`, so only the address nginx appended is used.
- nginx must set the header, for example `proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;` and `proxy_set_header X-Forwarded-Proto $scheme;`.
- When nginx is on the host, publish the API port on loopback only (`127.0.0.1:5000:80`) so nobody can reach the API without passing through nginx.
- If neither variable is set in Production the API still starts but logs a warning at start-up: every client then appears with the proxy's IP and shares one rate-limit bucket per endpoint.

**Rate limits (F-03).** Each limit is a fixed window **per client**: per client IP for `login`, `register` and `refresh`; per user id (falling back to IP) for `carts/checkout` and `payments/initiate`. Defaults (`appsettings.json`), all overridable with `RateLimiting__<Policy>__PermitLimit` / `RateLimiting__<Policy>__WindowSeconds`:

| Policy | Endpoint | Partition | Default |
|---|---|---|---|
| `Login` | `POST /api/v1/auth/login` | client IP | 5 / 60 s |
| `Register` | `POST /api/v1/auth/register` | client IP | 5 / 60 s |
| `Refresh` | `POST /api/v1/auth/refresh` | client IP | 60 / 60 s (raised from 20 in Batch 11b: 15-minute access tokens, many mobile users share one carrier IP) |
| `Checkout` | `POST /api/v1/carts/checkout` | user id, else IP | 5 / 60 s |
| `PaymentInitiate` | `POST /api/v1/payments/initiate` | user id, else IP | 10 / 60 s |

A rejected request gets `429 Too Many Requests` with a `Retry-After` header (seconds) and a ProblemDetails body with `code = RATE_LIMIT_EXCEEDED` and `retryAfterSeconds`. Counters are kept in memory per API instance. Users behind one carrier NAT share the per-IP login/register quota; raise those limits if that becomes a problem.

**Start-up validation (Production).** The API refuses to start unless `PaymentGatewaySettings__Provider=Zarinpal`, `PaymentGatewaySettings__EnableRealGateway=true`, `PaymentGatewaySettings__MerchantId` and `PaymentGatewaySettings__WebhookSecret` are set, `PaymentGatewaySettings__CallbackUrl` is an absolute `https` address that is not `localhost`/loopback (Batch 40), and `CorsOrigins__0` (and further indexes) lists the real front-end origin (no `localhost`). In any environment other than Development and Testing the `Manual`/fake payment gateway is never created; `Provider` must be `Zarinpal`. `Jwt__Issuer` and `Jwt__Audience` are required in every environment (empty values stop start-up, Batch 40). `Jwt__ExpireMinutes` is optional and defaults to 15.

The compose file reads these from the deployment environment: `ZARINPAL_MERCHANT_ID`, `PAYMENT_WEBHOOK_SECRET`, `PAYMENT_CALLBACK_URL`, `CORS_ORIGIN_0`, `ALLOWED_HOSTS`, and `TRUSTED_PROXY_NETWORK` or `TRUSTED_PROXY_IP` (see below).

**Allowed hosts (Batch 40).** `AllowedHosts` (compose: `ALLOWED_HOSTS`, `;`-separated) limits the `Host` header the API answers. Include `localhost`, because the container `HEALTHCHECK` calls `http://localhost:80/health/live`; for example `ALLOWED_HOSTS=api.example.com;localhost`. When it is empty or `*` in Production the API still starts but logs a warning.

**HTTP surface (Batch 40).**
- Authorization is on by default: every endpoint without an authorization attribute requires a signed-in user. Public endpoints carry an explicit `[AllowAnonymous]` (`/health/live`, `/health/ready` and `/version` are anonymous; since Batch 52 the detailed `/health` is anonymous only in Development and needs an admin token elsewhere, Appendix B.6). Unknown routes still return `404`.
- JSON only: a request body with `Content-Type: application/xml` gets `415`; `Accept: application/xml` is ignored and JSON is returned.
- Every response (API and `/uploads/*`) carries `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'`.
- CORS does not send `Access-Control-Allow-Credentials`; the API uses bearer tokens, not cookies.

**Images are built from a clean checkout only (Batch 40).** `.dockerignore` keeps `.env*`, every `appsettings.Production.json`, the local audit/plan documents and the test projects out of the build context, but never push an image built on a developer machine; CI builds from a clean checkout.

**Uploads.** Product images are stored in `/app/wwwroot/uploads`. The image creates this folder owned by the `app` user and `docker-compose.prod.yml` mounts the named volume `onlinestoreapi_uploads_data` there so files survive redeploys.

Secrets such as the SQL Server password and JWT signing key must be supplied through protected environment/secret storage and must not be committed to Git.

## 8. Database Migrations

The application performs Entity Framework Core database migration during startup when it is not running in the `Testing` environment and the configured database is relational.

The startup migration logic retries transient SQL Server/timeout failures up to the configured maximum retry count.

Current behavior:

```text
Maximum startup migration attempts = 10
Retry delay = 5 seconds
```

This allows the API container to start while SQL Server is still becoming ready.

**Batch 52:** this start-up path only runs when `Database__AutoMigrate=true` (staging). Production sets `Database__AutoMigrate=false` and applies migrations with the one-shot `migrator` service before the API starts (Appendix B.4).

**Platform warehouse (Batch 49, start-up guarantee since Batch 49b).** Migrations do not create a platform warehouse, and the API never invents a "Platform" province, city and address when stock is added without a warehouse. Instead, at every start the API (`PlatformWarehouseBootstrapper`, before the first request is served) checks whether a platform warehouse (`Warehouse.SellerId IS NULL`) exists:

- **It exists:** nothing happens; the `PlatformWarehouse__*` settings are not read for anything else (if any of them is set, the set must still be complete and valid, otherwise start-up fails in every environment).
- **It does not exist and `PlatformWarehouse__*` is complete:** the API creates it in one transaction: the province and city are reused when a row with exactly that name exists (city within that province), otherwise created; the address carries the recipient name, postal code and `ExtraDescription = "تلفن مسئول انبار: 09…"`. The address must belong to a user (`Address.UserId`): the owner is the user whose phone equals `PlatformWarehouse__PhoneNumber` (normalised to `09xxxxxxxxx`, Persian digits and `+98` accepted); if no user has that phone, an **inactive** system account with that phone, the recipient name and a random unknown password is created (it cannot log in; activate it or reset its password only if you want a person to use that phone). Use the warehouse's own mobile number, or the admin's number if the admin has already registered.
- **It does not exist and the settings are missing or invalid:** in `Production` start-up stops with an `OptionsValidationException` that names every missing or invalid `PlatformWarehouse:*` key. In other environments only a warning is logged.
- If the database cannot be queried at that moment (for example migrations not applied), the check is skipped and logged (error level in Production); the readiness health check reports the database.

| Setting (environment variable in the container) | `.env` variable in `docker-compose.prod.yml` | Rule |
|---|---|---|
| `PlatformWarehouse__Name` | `PLATFORM_WAREHOUSE_NAME` (default `انبار مرکزی پلتفرم`) | required, ≤ 150 |
| `PlatformWarehouse__ProvinceName` | `PLATFORM_WAREHOUSE_PROVINCE` | required, ≤ 100 |
| `PlatformWarehouse__CityName` | `PLATFORM_WAREHOUSE_CITY` | required, ≤ 100 |
| `PlatformWarehouse__StreetAddress` | `PLATFORM_WAREHOUSE_STREET_ADDRESS` | required, ≤ 300 |
| `PlatformWarehouse__Plaque` | `PLATFORM_WAREHOUSE_PLAQUE` | required, ≤ 10 |
| `PlatformWarehouse__Unit` | `PLATFORM_WAREHOUSE_UNIT` | optional (`-` when empty), ≤ 20 |
| `PlatformWarehouse__PostalCode` | `PLATFORM_WAREHOUSE_POSTAL_CODE` | exactly 10 ASCII digits |
| `PlatformWarehouse__RecipientFirstName` / `__RecipientLastName` | `PLATFORM_WAREHOUSE_RECIPIENT_FIRST_NAME` / `_LAST_NAME` | required, ≤ 50 each |
| `PlatformWarehouse__PhoneNumber` | `PLATFORM_WAREHOUSE_PHONE` | Iranian mobile number |

The code defaults are safe: only the name has a default, every address field is empty, so nothing is created from defaults. Set the variables before the first start of a release that contains Batch 49b on a database without a platform warehouse; afterwards they may stay or be removed. To change the warehouse later, edit it as an admin (`PUT /api/v1/warehouses/{id}`); the start-up check never edits an existing warehouse. Run a single API instance during the first start (two instances starting at the same moment on an empty database could both create one; stock without a warehouse then uses the one with the lowest id).

`409 PLATFORM_WAREHOUSE_NOT_CONFIGURED` (add-stock without a `warehouseId`) remains only as a safety net and should never be seen in normal operation; if it appears, someone deleted the platform warehouse or the start-up check was skipped because of a database error, and the next start recreates it from the settings. Old "Platform" province/city rows created by versions before Batch 49 are left in place and can be renamed or reused by an admin.

## 9. Health Endpoint

The API exposes:

```http
GET /health
```

The response includes the overall status and individual health-check entries.

The application registers health checks for its configured infrastructure dependencies, including SQL Server and Redis.

This endpoint is also used by the Docker health check.

**Batch 52:** the Docker health check uses `/health/live` (§3). Outside Development the detailed `/health` requires an admin token; automation uses `/health/ready` and the container health status (Appendix B.6).

## 10. Version Endpoint

The API exposes:

```http
GET /version
```

The endpoint returns deployment metadata including:

```text
version
commit
deployedAt
```

The `commit` value is supplied through the `GIT_COMMIT` environment variable.

This endpoint is also used by the production deployment workflow to identify the currently deployed commit before replacing the application container.

## 11. CI/CD Deployment Pipeline

The repository CI/CD workflow is defined in:

```text
.github/workflows/ci-cd.yml
```

The overall flow is:

```text
Git Push / Manual Dispatch
          |
          v
   Build & Test
          |
          v
    Docker Build
          |
          v
      Push to GHCR
          |
          v
 Production Deploy
          |
          v
 Health Verification
          |
      +---+---+
      |       |
    Pass     Fail
      |       |
      v       v
  Success   Rollback
```

## 12. Build and Test Gate

The workflow first restores, builds, and tests the solution using .NET 8.

The test command also collects XPlat Code Coverage data.

The Docker job depends on successful completion of the build-and-test job.

Therefore, a failing test prevents the Docker image from becoming part of the deployment path.

## 13. Container Registry

Successful non-PR Docker builds are pushed to GitHub Container Registry (GHCR).

The workflow publishes two tags:

```text
ghcr.io/sasan-boddouhi/onlinestoreapi:latest
ghcr.io/sasan-boddouhi/onlinestoreapi:<commit-sha>
```

The commit SHA tag provides an immutable deployment reference that can be used during rollback.

## 14. Production Runner

The production deployment job runs on a self-hosted GitHub Actions runner with labels corresponding to the production environment.

The deployment job is intentionally restricted so that it runs only for:

```text
push to main
```

or a manual `workflow_dispatch` on `main`.

Pull requests and `develop` pushes do not execute the production deployment job.

## 15. Previous Version Capture

Before changing the production API container, the deployment workflow attempts to determine the currently deployed commit.

Primary source:

```text
GET http://localhost:5000/version
```

If that is unavailable or does not return a valid 40-character commit SHA, the workflow falls back to:

```text
docker inspect onlinestore-api
```

If neither source produces a valid SHA, the previous version is treated as unavailable.

No production mutation is performed during this capture step.

## 16. Production Image Deployment

The production environment uses the GHCR image identified by the workflow's `IMAGE_TAG` value.

The production API container is configured with the production SQL Server and Redis connection settings and exposes port `5000` on the host mapped to port `80` in the API container.

The deployment replaces the running API container while preserving the persistent database and Redis volumes.

## 17. Deployment Verification

After deploying the new image, the workflow waits for the API to become available and then verifies the health endpoint.

The verification target is:

```text
http://localhost:5000/health
```

If the new deployment passes the health gate, the workflow also verifies `/version` so the deployed commit can be confirmed.

**Batch 52:** use `http://localhost:5000/health/ready` for automated verification; the detailed `/health` answers `401` without an admin token outside Development (Appendix B.6).

## 18. Rollback Strategy

Rollback is designed as an API-image rollback.

The deployment workflow first captures the previous commit SHA and then deploys the new API image.

If the new deployment fails its health verification and a valid previous SHA was captured:

```text
New API deployment
       |
       v
Health check fails
       |
       v
Deploy previous API image
       |
       v
Wait for API
       |
       v
Verify health
       |
       v
Verify /version
```

The database and Redis persistent volumes are not removed as part of this API rollback path.

## 19. Rollback Failure Handling

If rollback itself fails, or if no valid previous SHA was available, the workflow reports a critical deployment state and fails the job.

The workflow does not silently report success after an unsuccessful deployment/rollback sequence.

## 20. Deployment Notifications

The CI/CD workflow sends deployment notifications through the configured Discord webhook.

Notification states include:

```text
Success
Failure
Rollback
Critical
```

Notification failures are deliberately non-critical to the deployment result so that a notification transport problem does not change the actual deployment state.

## 21. Controlled Rollback Test

The workflow supports a manual `workflow_dispatch` input:

```text
force_verify_fail
```

This input is intended for controlled verification of the rollback path.

The forced failure is applied only to the verification gate and does not alter the application code or production data.

Normal pushes to `main` do not activate the forced verification failure.

## 22. Persistent Data

The production database uses a persistent Docker volume.

The Redis service also uses a persistent Docker volume.

These volumes must be treated as production data and must not be removed during normal application deployments or rollback operations.

Avoid destructive commands such as:

```bash
docker volume prune
docker system prune --volumes
```

unless the consequences have been explicitly reviewed and the data is intentionally disposable.

## 23. Deployment Safety Principles

The current deployment design follows these principles:

1. Tests gate Docker image creation for the deployment path.
2. Production deployment is restricted to `main`.
3. Production deployment runs on a dedicated self-hosted runner.
4. The currently deployed commit is captured before replacement.
5. Health is verified after deployment.
6. A known previous API image can be restored when available.
7. Persistent database and Redis volumes are preserved during API rollback.
8. Deployment state is reported explicitly.
9. Secrets are provided through environment/secret configuration rather than committed source files.

## 24. Operational Checklist

Before a production deployment:

- Confirm the intended commit is on `main`.
- Confirm CI build and tests pass.
- Confirm the Docker image is available in GHCR.
- Confirm production environment variables/secrets are present.
- Confirm the production runner is online.
- Do not remove persistent Docker volumes.

After deployment:

- Verify `/health/ready` (the detailed `/health` needs an admin token outside Development, Appendix B.6).
- Verify `/version`.
- Confirm the returned commit matches the intended deployment.
- Review deployment logs.
- If verification fails, follow the workflow's rollback result rather than manually deleting production data.

## 25. Related Documentation

- `docs/architecture.md` — application architecture and dependency flow
- `docs/authentication.md` — authentication, authorization, sessions, JWTs, and refresh tokens
- `docs/ci-cd.md` — CI/CD workflow details

## 26. Source Files

The main deployment-related files are:

```text
Dockerfile
docker-compose.yml
.github/workflows/ci-cd.yml
```

The application endpoints used for deployment verification are defined in:

```text
Online Store Application/Program.cs
```

---

## Appendix A — M6 production readiness (Phase 9)

### A.1 Migration order

See `docs/runbook.md` §1. Three migrations rewrite or delete data
(`Phase8_PaymentAndDeliveredAt`, `Phase9_DropPaymentStatus`,
`Phase9_RatingCache`). Back up first.

### A.2 R38 / R39 notes

**R38 — unique index on `Payment.TransactionId`.**
The index created by `InitialCreate` was already filtered
(`WHERE [TransactionId] IS NOT NULL`); the claim that it was unfiltered was a
Phase 8 reporting error, corrected here. What Phase 9 actually fixed was the
**model-schema divergence**: `AppDbContext` did not declare the filter, so the
C# model and the real schema disagreed. The filter is now declared explicitly in
`AppDbContext` and a regression test
(`Payment_TwoRowsWithNullTransactionId_BothPersist`) proves two NULL authorities
can coexist.

**R39 — `Payment.Status` removed.**
`State` is the only source of truth. The column was dropped because Phase 8
already backfilled `State` from `Status`; `State` is a strict superset
(it also has `Initiated` and `Cancelled`), so no information was lost. The
`Down` migration re-derives `Status` from `State` rather than defaulting every
row to `Pending`.

### A.3 R42 / R43 warnings

> **R42 — `ZarinpalPaymentGateway` has never run against the real API.**
> It is covered by unit tests only, and deliberately so: a test that hits the
> real gateway is a test that spends money and needs credentials. First
> production enablement is the first real execution. Follow the checklist in
> `docs/runbook.md` §6.

> **R43 — refunds are manual.**
> The platform records `Payment.State = Refunded`; it does not move money.
> `IPaymentGateway.RefundPaymentAsync` is not implemented for Zarinpal. The
> operations team must refund in the gateway panel. See `docs/runbook.md` §3.

### A.4 Environment variables

| Variable | Required | Purpose |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | yes | SQL Server connection string. |
| `Redis__Configuration` | yes | Cache/session backend. |
| `Jwt__Key` | yes | Signing key. Must not be the development value. |
| `Jwt__Issuer` / `Jwt__Audience` | yes | Token issuer and audience. Empty values stop start-up (Batch 40). |
| `AllowedHosts` | recommended in production | Allowed `Host` values, `;`-separated, include `localhost` for the health check. `*` logs a start-up warning in Production. |
| `PaymentGatewaySettings__Provider` | yes | `Manual` \| `Zarinpal` \| `IDPay`. |
| `PaymentGatewaySettings__WebhookSecret` | **yes in production** | HMAC-SHA256 key for `X-Payment-Signature`. If unset, **every** webhook is rejected (fail-closed). |
| `PaymentGatewaySettings__MerchantId` | when real gateway | Zarinpal merchant id. Empty ⇒ gateway refuses to start a payment. |
| `PaymentGatewaySettings__EnableRealGateway` | when real gateway | `false` refuses all live calls. Set to `true` only after the runbook checklist. |
| `PaymentGatewaySettings__CallbackUrl` | **yes in production** | Absolute URL the gateway redirects to. In Production it must be `https` and not `localhost`/loopback, or start-up fails (Batch 40). |
| `PaymentGatewaySettings__UseSandbox` | staging | `true` forces `https://sandbox.zarinpal.com` and **overrides** `BaseUrl`, so a sandbox merchant id can never reach the live gateway. |
| `PaymentGatewaySettings__BaseUrl` | no (default `https://payment.zarinpal.com`) | Zarinpal v4 host. Both the API calls (`/pg/v4/payment/*.json`) and the payment page (`/pg/StartPay/{authority}`) are built from it (Batch 5, F-28). Must be absolute `https`. |
| `PaymentGatewaySettings__RequestTimeoutSeconds` | no (default 20, 1-120) | Timeout of each gateway call. A timeout on verify is an unknown outcome, not a failure. |
| `PaymentGatewaySettings__VerifyRetryAfterSeconds` | no (default 5, 1-300) | `Retry-After` value on `503 PAYMENT_VERIFY_RETRY`. |
| `PaymentGatewaySettings__OpenPaymentReuseMinutes` | no (default 15, 0-60) | A repeated initiate for the same order returns the open payment (same authority, `isReplay=true`) if it is younger than this; an older open payment is cancelled and a new one is created. `0` never reuses. |
| `Database__AutoMigrate` | staging only | `true` applies migrations at startup (with retry). Never enable in production. |
| `ProductImages__AllowedExternalHosts__0` (`__1`, ...) | no (default empty = adding a product image by external URL is off) | Host names (no scheme, path, port, space or wildcard, e.g. `cdn.example.com`) allowed in `POST /products/{id}/images/url`. Exact, case-insensitive match; https on the default port only, no user-info. An invalid entry stops start-up in every environment. Uploaded files (`/uploads/...`) are not affected (Batch 48c). |
| `AbandonedPaymentSettings__ScanIntervalMinutes` | no (default 15) | Cleanup job cadence. |
| `AbandonedPaymentSettings__TimeoutMinutes` | no (default 30) | Age after which a `Pending` payment is cancelled. |
| `AbandonedPaymentSettings__Enabled` | no (default true) | Turn the cleanup job off. |
| `ForwardedHeaders__KnownNetworks__0` / `ForwardedHeaders__KnownProxies__0` | **yes behind a proxy** | Trusted reverse proxy (CIDR or single IP). Without it every client has the proxy's IP; a start-up warning is logged in Production. See §7. |
| `ForwardedHeaders__ForwardLimit` | no (default 1) | Number of proxies in front of the API. |
| `RateLimiting__<Policy>__PermitLimit` / `__WindowSeconds` | no | Per-client limits for `Login`, `Register`, `Refresh`, `Checkout`, `PaymentInitiate`. See §7. |

`appsettings.json` currently ships
`WebhookSecret = "dev-only-webhook-secret-change-in-production"`. **Override it
via the environment in every non-development deployment.** A webhook signed with
a known secret is an unsigned webhook.

### A.5 Test coverage before deploying

```bash
dotnet test "Online Store Application.sln" --filter "Category!=Migration"
dotnet test OnlineStore.Tests.Migrations     # requires SQL Server LocalDB
```

The second command is the only one that exercises real migrations; the rest use
`EnsureCreated` and never run a migration script. Skipping it means shipping
unverified SQL. CI excludes it because `ubuntu-latest` has no LocalDB — run it on
Windows before a production deploy.

### A.6 Staging environment

```bash
export MSSQL_SA_PASSWORD='...'
export JWT_KEY='...'
export PAYMENT_WEBHOOK_SECRET='...'
export ZARINPAL_MERCHANT_ID='...'          # sandbox merchant id

docker compose -f docker-compose.staging.yml up -d
```

Differences from production, all deliberate:

| | Production | Staging |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | `Staging` |
| API port | `5000` | `5080` |
| SQL Server port | not published | `127.0.0.1:14330` |
| Redis port | not published | `127.0.0.1:6380` |
| `Database:AutoMigrate` | `false` | **`true`** |
| `EnableRealGateway` | `true` (eventually) | `false` |
| `UseSandbox` | `false` | **`true`** |

**Why auto-migrate only in staging:** staging exists to prove that the risky
migrations (`Phase8_*`, `Phase9_*`) work against real data. Running them by hand
there would hide exactly the failure this environment is meant to catch. In
production a deploy must never silently change the schema.

If migration fails at startup, `DatabaseMigrationRunner` retries 10 times with a
5 s delay, then throws. The container never becomes healthy, which is the
correct outcome: serving traffic on an old schema is worse than being down.

### A.7 Smoke-testing the payment path

```powershell
./scripts/test-zarinpal-sandbox.ps1 -WebhookSecret '<same secret as the API>'
```

Runs three phases: webhook signature enforcement, staging configuration
resolution, and (with `-OrderId`) the full initiate → verify → idempotency path.
See the script header for what each phase can and cannot prove.
### A.8 Payment lifecycle (Batch 5)

Amounts are whole **rials** (owner decision D2). The Zarinpal adapter sends `amount` as an integer and `currency = "IRR"` on request; verify sends the same integer amount (the verify call has no currency field). A payment's stored `Amount` is exactly what was sent to the gateway; an order total with a fraction of a rial (percentage coupon rounding) is paid as the whole-rial part.

All Zarinpal details stay in `ZarinpalPaymentGateway`. `IPaymentGateway.VerifyAsync` returns a provider-neutral outcome and `PaymentService` branches only on it:

| Outcome | Zarinpal | Payment | API |
|---|---|---|---|
| `Verified` | code `100` | `Succeeded` (Batch 4b capture path) | `200` |
| `AlreadyVerified` | code `101` | `Succeeded` (idempotent) | `200` |
| `Rejected` | `-50` amount mismatch, `-51` not paid, `-53` other merchant, `-54` invalid authority | `Failed` | `400` with the gateway code |
| `TransientError` | timeout, network error, HTTP 5xx, unreadable body, any other code (e.g. `-9`, `-10`, `-12`, `-52`), gateway disabled | stays `Pending` | `503 PAYMENT_VERIFY_RETRY` + `Retry-After` |

Guards:

- `POST payments/initiate`: `409 ORDER_ALREADY_PAID` if the order has a `Succeeded` payment; `409 ORDER_NOT_PAYABLE` if the order is cancelled, shipped, delivered or returned; `409 PAYMENT_VERIFICATION_PENDING` if a verify of an open payment has no definitive answer yet. A repeated initiate returns the open payment (see `OpenPaymentReuseMinutes`).
- `POST payments/verify` and the webhook: if the order is no longer payable or already paid, the gateway verify is **not** called; the payment is cancelled with a reason (`Payment.NotVerified` in the audit log) and the API returns `409 ORDER_NOT_PAYABLE` / `409 ORDER_ALREADY_PAID`. Zarinpal returns money it was never asked to verify to the payer.
- Before calling verify the payment is claimed in one transaction (other open payments of the order are cancelled, the payment row is updated under its `RowVersion`, `Payment.VerificationStarted` is written). Two verifies for two payments of the same order cannot both reach the gateway.
- While a verify has no definitive answer (`Payment.VerificationStarted` on a `Pending` payment), nothing cancels that payment: order cancel returns `409 PAYMENT_VERIFICATION_PENDING`, and the abandoned-order cleanup and the abandoned-payment sweep skip it and write one `Payment.ReviewRequired` entry. Verifying the payment again (customer or admin) resolves it.
- If money is captured for an order that is no longer payable (only possible when verify had already started), the payment is still recorded `Succeeded` and `Payment.ReviewRequired` gets `refundNeeded=true` for the refund workflow (Batch 8).

Not confirmed in Zarinpal's documentation (verified 2026-11-11): the time window after which an unverified payment is returned to the payer, how long after the callback verify may still be called, the default `currency` when omitted, the exact sandbox StartPay URL (inferred from "replace the host with sandbox.zarinpal.com"), and the lifetime of an authority.

## Appendix B — Production operations (Batch 52)

Findings OPS-01, OPS-02, OPS-03, OPS-05, OPS-07 and OPS-08 of the backend review; owner decisions N15 (SQL Server Express for launch, 2 GB+ memory, never Developer) and N19 (uploads on a local volume of a single node, included in backups).

### B.1 New environment variables

All are read by `docker-compose.prod.yml` from the deployment `.env` file (never committed). Variables marked *required* stop `docker compose` with an error when missing.

| Variable | Required | Default | Purpose |
|---|---|---|---|
| `APP_DB_USER` / `APP_DB_PASSWORD` | yes | – | SQL login the API uses at run time (read, write, execute; no DDL). |
| `MIGRATION_DB_USER` / `MIGRATION_DB_PASSWORD` | yes | – | SQL login used only by the one-shot `migrator` (`db_owner` of `ShopDB`, no server role). |
| `IMAGE_TAG` | yes | – | Image tag of the API; the migrator uses `<IMAGE_TAG>-migrator` from the same commit. |
| `MSSQL_PID` | no | `Express` | SQL Server edition. `Standard` (or a product key) once a licence is bought. Never `Developer` or `Evaluation` in production. |
| `MSSQL_MEM_LIMIT` | no | `2g` | Container memory limit of SQL Server (minimum 2 GB). |
| `MSSQL_MEMORY_LIMIT_MB` | no | `1638` | SQL Server's own memory cap; keep it below the container limit. |
| `REDIS_MAXMEMORY` | no | `96mb` | Redis `maxmemory` (policy `allkeys-lru`; Redis is only a cache). |
| `API_BIND_ADDRESS` | no | `127.0.0.1` | Host address the API port is published on. Use a private-network IP if the reverse proxy runs on another machine; never `0.0.0.0`. |
| `SQL_SCRIPTS_PATH` | no | `./scripts/sql` | Folder mounted into `db-init`. |
| `PLATFORM_WAREHOUSE_NAME`, `PLATFORM_WAREHOUSE_PROVINCE`, `PLATFORM_WAREHOUSE_CITY`, `PLATFORM_WAREHOUSE_STREET_ADDRESS`, `PLATFORM_WAREHOUSE_PLAQUE`, `PLATFORM_WAREHOUSE_UNIT`, `PLATFORM_WAREHOUSE_POSTAL_CODE`, `PLATFORM_WAREHOUSE_RECIPIENT_FIRST_NAME`, `PLATFORM_WAREHOUSE_RECIPIENT_LAST_NAME`, `PLATFORM_WAREHOUSE_PHONE` (Batch 49b) | yes while no platform warehouse exists | name: `انبار مرکزی پلتفرم`; others empty | Mapped to `PlatformWarehouse__*`. The API creates the platform warehouse from them at start-up when none exists; without them a Production start on such a database fails (section 8). |

Password rules: at least 16 characters, SQL Server complexity rules (upper case, lower case, digit, symbol), and **no single quote and no semicolon** (the value is placed into T-SQL text and into a connection string). The application and migration passwords must differ. `MSSQL_SA_PASSWORD` stays; it is used only by the SQL health check, `db-init` and the backup scripts.

### B.2 First deployment (empty server)

```bash
# 0. The host .env holds every variable of B.1 and section 7 (JWT_KEY, ZARINPAL_MERCHANT_ID, PAYMENT_CALLBACK_URL, ...).
docker volume create onlinestoreapi_sqlserver_data
docker volume create onlinestoreapi_redisdata

# 1. SQL Server and Redis
docker compose -f docker-compose.prod.yml up -d sqlserver redis

# 2. Database, application login, migration login (idempotent; re-run after every password rotation)
docker compose -f docker-compose.prod.yml --profile setup run --rm db-init

# 3. Migrations, then the API (the API waits until migrator exits with 0)
docker compose -f docker-compose.prod.yml up -d

# 4. Checks
docker inspect onlinestore-sqlserver --format '{{.State.Health.Status}}'   # healthy
docker compose -f docker-compose.prod.yml logs migrator | tail -n 20       # ends with "Done."
curl --fail http://127.0.0.1:5000/health/ready
curl --fail http://127.0.0.1:5000/version
```

Then finish the data steps of B.7 (platform warehouse, shipping method, SuperAdmin bootstrap).

### B.3 Upgrading a deployment that still uses `sa` and auto-migrate

Order matters: the backup comes first, and the new API must not start before the logins exist.

1. Add the B.1 variables to `.env` and pull the new `docker-compose.prod.yml`, `scripts/` and both images (`<tag>` and `<tag>-migrator`).
2. Take a backup with the **old** setup. The old SQL container has no `SQLCMDPASSWORD`, so pass it once:
   ```bash
   docker exec -e SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" onlinestore-sqlserver \
     /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b \
     -Q "BACKUP DATABASE [ShopDB] TO DISK = N'/var/opt/mssql/backup/pre-batch52.bak' WITH CHECKSUM, INIT"
   docker cp onlinestore-sqlserver:/var/opt/mssql/backup/pre-batch52.bak ./pre-batch52.bak
   ```
3. `docker compose -f docker-compose.prod.yml up -d sqlserver redis` recreates SQL Server with `MSSQL_PID`, the memory limit and the health check on the existing data volume. Check the edition: `SELECT SERVERPROPERTY('Edition')` must say `Express Edition` (or `Standard Edition`). **Do this on staging first**: an edition change on an existing data volume has not been tested in this repository.
4. `docker compose -f docker-compose.prod.yml --profile setup run --rm db-init`
5. `docker compose -f docker-compose.prod.yml up -d` — the migrator applies pending migrations with the migration login, then the API starts with the application login and `Database__AutoMigrate=false`.
6. Run `scripts/backup.sh` and `scripts/restore-test.sh` once (B.5).

Rollback: restore the `.env` and compose file of the previous release and start it; the new logins can stay (they are harmless). A migration is never rolled back automatically; restore the pre-upgrade backup if a schema rollback is needed.

### B.4 Migrations: the one-shot `migrator`

- The `Dockerfile` target `migrator` builds an EF Core migrations bundle (`efbundle`, self-contained, `dotnet-ef` 8.0.34 = the project's EF Core version) and copies `appsettings.json` next to it, because the design-time factory reads that file from the working directory. The connection string comes only from `ConnectionStrings__DefaultConnection`.
- CI pushes it as `ghcr.io/<owner>/onlinestoreapi:<sha>-migrator` and `latest-migrator` next to the API image.
- `docker compose up` runs it before the API (`depends_on: condition: service_completed_successfully`). A failed migration leaves the running API untouched; read `docker compose logs migrator`.
- Apply by hand: `docker compose -f docker-compose.prod.yml run --rm migrator` (the bundle only applies missing migrations).
- Staging and the local Compose file may keep `Database__AutoMigrate=true`; production must not (the application login has no DDL rights, so an auto-migrate attempt would fail anyway).

### B.5 Backups and restore test

- `scripts/backup.sh` (on the Docker host, as a user allowed to run `docker`): full `ShopDB` backup `WITH CHECKSUM` plus `RESTORE VERIFYONLY`, a `tar.gz` of the uploads volume, `manifest.txt` (edition, last migration, data size, number of upload files) and `SHA256SUMS`, under `$BACKUP_ROOT/<UTC stamp>/` (default `/var/backups/onlinestore`, kept 14 days). It warns when the Express data file passes 8 GB. Copy every backup off the machine.
- `scripts/restore-test.sh [dir]`: checks the checksums, restores the database into a throw-away SQL Server container with no network, runs `DBCC CHECKDB`, compares the last migration with the manifest, unpacks the uploads and compares the file count. Production is not touched.
- Schedule and restore log: `docs/runbook.md` §7.

### B.6 Health endpoints

| Endpoint | Who | Content | Used by |
|---|---|---|---|
| `/health/live` | anonymous | `Healthy` / `Unhealthy` only | Docker `HEALTHCHECK` |
| `/health/ready` | anonymous | `Healthy` / `Unhealthy` only (database and Redis readiness) | deploy scripts, load balancer |
| `/health` | anonymous in Development; `AdminOnly` elsewhere | every check with name, status and duration | an admin, by hand |

`scripts/migrate-production-network.sh` now checks `/health/ready` and the container health status instead of `/health`.

### B.7 Pre-traffic checklist (review §10)

1. `appsettings.Production.json` is git-ignored and docker-ignored (Batch 40); real values come only from environment variables.
2. `JWT_KEY` has at least 32 random characters; `Jwt__Issuer` and `Jwt__Audience` are set (compose); `Session__*` reviewed.
3. `PaymentGatewaySettings__Provider=Zarinpal`, `EnableRealGateway=true`, real `ZARINPAL_MERCHANT_ID`, no sandbox flag, `PAYMENT_CALLBACK_URL` = the real https result page (start-up refuses anything else, Batch 40); `PAYMENT_WEBHOOK_SECRET` set while the webhook still exists.
4. `CORS_ORIGIN_0…` = real origins; `TRUSTED_PROXY_NETWORK` or `TRUSTED_PROXY_IP` = the reverse proxy; the start-up warning about missing proxies is gone; two clients get different rate-limit buckets; `ALLOWED_HOSTS` set (include `localhost`).
5. At least one active shipping method, categories and geography seeded, commission rate set, one platform warehouse created (Batch 49; since Batch 49b created by the API at start-up from `PLATFORM_WAREHOUSE_*` → `PlatformWarehouse__*`, section 8 — check the start-up log line "Platform warehouse … created" or an existing `Warehouse` row with `SellerId IS NULL`), `ProductImages__AllowedExternalHosts__N` set if images are added by URL (Batch 48c).
6. `BOOTSTRAP_SUPERADMIN_PHONE` set to a registered phone for the first start, then removed.
7. Database: the API logs in with `APP_DB_USER` (not `sa`); edition is Express or Standard; memory 2 GB or more; `Database__AutoMigrate=false`; migrations applied by `migrator`; `scripts/backup.sh` scheduled; one `scripts/restore-test.sh` passed and logged.
8. One API container only (rate limiting, the job queue and uploads are per node until OPS-02 is solved); the uploads volume is mounted and in the backup.
9. `Swagger__Enabled=false`; `ASPNETCORE_ENVIRONMENT=Production`; `OpenTelemetry__TraceSamplingRatio` at most 0.1; API port bound to `127.0.0.1` behind the TLS proxy.
10. `scripts/test-zarinpal-sandbox.ps1` passes against staging; one real small payment is paid, verified and refunded by hand once.

### B.8 Staging dry run (not executed in this repository: the development machine has no Docker)

Run on a staging host with a copy of the production `.env` (with different passwords) and record the output:

```bash
docker compose -f docker-compose.prod.yml config >/dev/null                   # variables and YAML are valid
docker build --target migrator -t ghcr.io/<owner>/onlinestoreapi:staging-migrator .
docker build -t ghcr.io/<owner>/onlinestoreapi:staging .
export IMAGE_TAG=staging
docker compose -f docker-compose.prod.yml up -d sqlserver redis
docker exec onlinestore-sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -Q "SELECT SERVERPROPERTY('Edition')"
docker compose -f docker-compose.prod.yml --profile setup run --rm db-init     # run it twice: the second run must also succeed
docker compose -f docker-compose.prod.yml up -d
docker compose -f docker-compose.prod.yml ps -a                               # migrator exited 0, api healthy
# The application login must not be able to change the schema (expect an error):
docker exec -e SQLCMDPASSWORD="$APP_DB_PASSWORD" onlinestore-sqlserver \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U "$APP_DB_USER" -C -b -d ShopDB -Q "CREATE TABLE dry_run_check (i int)"
curl -i http://127.0.0.1:5000/health                                          # 401
curl --fail http://127.0.0.1:5000/health/ready
docker exec onlinestore-redis redis-cli CONFIG GET maxmemory                  # 100663296 (96 MB)
scripts/backup.sh && scripts/restore-test.sh
```
