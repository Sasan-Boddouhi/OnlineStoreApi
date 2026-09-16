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
http://localhost:80/health
```

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
```

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

## 9. Health Endpoint

The API exposes:

```http
GET /health
```

The response includes the overall status and individual health-check entries.

The application registers health checks for its configured infrastructure dependencies, including SQL Server and Redis.

This endpoint is also used by the Docker health check.

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

- Verify `/health`.
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
