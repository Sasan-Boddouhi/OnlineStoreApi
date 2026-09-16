# CI/CD Pipeline

## Overview

The repository uses GitHub Actions to build, test, package, and deploy the Online Store API.

The workflow is defined in `.github/workflows/ci-cd.yml` and is named `CI/CD Pipeline`.

The pipeline has four jobs:

1. **Build & Test** — restores, builds, and tests the solution on a GitHub-hosted runner.
2. **Coverage Report** — converts the collected Cobertura coverage data into an HTML report.
3. **Docker Build & Push** — builds the Docker image and publishes it to GitHub Container Registry (GHCR) when the event allows publishing.
4. **Deploy to Production** — deploys a specific image to the production VM, verifies health, and can roll back the API container when the new deployment is unhealthy.

The deployment job runs only for a push to `main` or a manually dispatched workflow on `main`. It does not run for pull requests or pushes to `develop`. fileciteturn100file0L2-L2

---

## Workflow Triggers

The workflow responds to three event types:

- `push` to `main` or `develop`.
- `pull_request` targeting `main`.
- `workflow_dispatch`, with an optional `force_verify_fail` boolean input used for controlled rollback testing.

Documentation-only changes and several repository metadata paths are ignored for `push` events. The ignored paths include Markdown files, `docs/**`, `LICENSE`, `.gitignore`, `.editorconfig`, `.vscode/**`, and `.vs/**`. fileciteturn100file0L2-L2

The workflow grants `contents: read` and `packages: write` permissions. It also enables `FORCE_JAVASCRIPT_ACTIONS_TO_NODE24`. fileciteturn100file0L2-L2

---

## Job 1: Build & Test

**Runner:** `ubuntu-latest`

This is the primary quality gate for the pipeline.

### Steps

1. Check out the repository with `actions/checkout@v4`.
2. Install .NET `8.0.x` with `actions/setup-dotnet@v4`.
3. Cache NuGet packages with `actions/cache@v4`.
4. Restore the solution:

```text
dotnet restore "Online Store Application.sln"
```

5. Build the solution in Release configuration without restoring again.
6. Run the complete test suite in Release configuration and collect XPlat Code Coverage using `coverlet.runsettings`.
7. Upload the generated Cobertura XML as the `coverage-xml` artifact with a one-day retention period.

The Docker job depends on this job, so the container build/publish stage is gated by successful build and test execution. fileciteturn100file0L2-L2

---

## Job 2: Coverage Report

**Runner:** `ubuntu-latest`

This job depends on `build-and-test` and is not on the deployment path.

It:

1. Checks out the repository.
2. Downloads the `coverage-xml` artifact.
3. Installs `dotnet-reportgenerator-globaltool`.
4. Generates an HTML report from `**/*.cobertura.xml` into `coveragereport`.
5. Uploads the HTML report as the `coverage-report` artifact.

Because the Docker job depends directly on `build-and-test`, this reporting job does not gate Docker packaging or production deployment. fileciteturn100file0L2-L2

---

## Job 3: Docker Build & Push

**Runner:** `ubuntu-latest`

**Dependency:** `build-and-test`

The job uses Docker Buildx and authenticates to GitHub Container Registry with the GitHub-provided token.

The Docker image is tagged with both:

```text
ghcr.io/sasan-boddouhi/onlinestoreapi:latest
ghcr.io/sasan-boddouhi/onlinestoreapi:${{ github.sha }}
```

The event controls whether the image is pushed:

- **Pull request:** build only; no push.
- **Push to `main` or `develop`:** build and push.
- **Manual workflow dispatch:** build and push.

GitHub Actions cache storage is used through Docker Buildx with `cache-from: type=gha` and `cache-to: type=gha,mode=max`. fileciteturn100file0L2-L2

The SHA tag is important for deployment because production can deploy an immutable commit-specific image instead of relying on the moving `latest` tag.

---

## Job 4: Production Deployment

**Runner:** self-hosted production runner

```text
[self-hosted, Linux, X64, onlinestore-prod]
```

The job requires the Docker job to succeed.

It is enabled only when:

```text
push -> main
```

or:

```text
workflow_dispatch -> main
```

This prevents pull requests and `develop` pushes from executing the production deployment job. fileciteturn100file0L2-L2

---

## Deployment State Machine

The production deployment is intentionally state-aware rather than treating every failure as the same event.

```text
Capture Previous SHA
        |
        v
Deploy New API Image
        |
        v
Wait for API
        |
        v
Verify New Health
     /       \
  success    failure
    |           |
    |           v
    |       Rollback to
    |       Previous SHA
    |          /     \
    |     success    failure
    |        |          |
    v        v          v
Success   Rolled Back  Critical
             but Job     Failure
             Fails
```

The deployment is considered successful only when the new deployment passes its health verification. A successful rollback restores service but the GitHub Actions job still finishes as failed, making the failed deployment visible to CI/CD rather than hiding it behind the rollback. fileciteturn101file0L2-L2 fileciteturn102file0L2-L2

---

## Step 1: Capture the Previous Version

Before changing the production API container, the workflow attempts to identify the currently deployed commit SHA.

### Primary source

The workflow calls:

```text
http://localhost:5000/version
```

It accepts the value only when it matches a 40-character lowercase hexadecimal Git SHA.

### Fallback source

If `/version` is unavailable or does not contain a valid SHA, the workflow runs:

```text
docker inspect onlinestore-api --format '{{.Config.Image}}'
```

and extracts a valid 40-character lowercase hexadecimal SHA from the image reference.

If neither source produces a valid SHA, `previous_sha` is left empty and a later rollback cannot proceed. fileciteturn101file0L2-L2

No production mutation occurs during this capture step. fileciteturn101file0L2-L2

---

## Step 2: Deploy the New API Image

The deployment step changes into the production deployment directory:

```text
~/onlinestore-prod
```

It sets:

```text
IMAGE_TAG=${GITHUB_SHA}
GIT_COMMIT=${GITHUB_SHA}
```

Then it pulls the commit-specific API image and recreates only the API service:

```text
docker compose -f docker-compose.prod.yml pull api
docker compose -f docker-compose.prod.yml up -d --force-recreate api
```

The production Compose configuration supplies the commit SHA to the application through `GIT_COMMIT`, which is exposed by the API's `/version` endpoint. fileciteturn101file0L2-L2

---

## Step 3: Wait and Verify Health

After deployment, the workflow waits 20 seconds before verification.

The health check then calls:

```text
http://localhost:5000/health
```

The check is attempted up to five times with a five-second delay between failed attempts.

The health verification step uses `continue-on-error: true`. This is intentional: the workflow needs the step to retain a `failure` outcome so the rollback condition can inspect it without immediately terminating the job. fileciteturn101file0L2-L2

### Controlled failure mode

A manual `workflow_dispatch` can set `force_verify_fail=true`. In that mode, the verification step intentionally exits with failure after the new container has been deployed.

This is a test mechanism for exercising the rollback path. It is not enabled by normal pushes to `main`. fileciteturn100file0L2-L2 fileciteturn101file0L2-L2

---

## Step 4: Rollback

Rollback runs only when the new health verification has a `failure` outcome.

The rollback step:

1. Reads `previous_sha` captured before deployment.
2. Requires the value to be a valid 40-character lowercase hexadecimal SHA.
3. Pulls the corresponding previous API image.
4. Recreates the API container using that image.
5. Waits 20 seconds.
6. Verifies `/health`, retrying up to five times with five-second intervals.
7. Calls `/version`.
8. Requires the returned response to contain the previous SHA.

Only the API service is recreated. The rollback workflow does not recreate SQL Server or Redis and does not modify the persistent volumes. fileciteturn101file0L2-L2

### Rollback failure

Rollback fails critically when, for example:

- no previous SHA was captured;
- the previous SHA is invalid;
- the previous image cannot be restored successfully;
- the rollback health check fails; or
- `/version` does not match the expected previous SHA.

The rollback step itself uses `continue-on-error: true` so that the notification and final status logic can inspect its outcome. fileciteturn101file0L2-L2

---

## Deployment Summary

A deployment summary always runs, regardless of deployment outcome.

It records:

- commit;
- branch;
- runner name;
- previous SHA;
- deploy outcome;
- new health outcome;
- rollback outcome;
- version-check outcome.

This provides an explicit state record in the GitHub Actions log even when an earlier deployment step failed. fileciteturn102file0L2-L2

---

## Notifications

The deployment job sends state-specific Discord webhook notifications.

### Successful deployment

A success notification is sent when the new health verification succeeds.

### Deployment failed and rollback succeeded

A rollback notification is sent when:

```text
new health = failure
rollback = success
```

The notification includes the failed commit and the commit to which production was rolled back.

The deployment job still fails afterward. fileciteturn102file0L2-L2

### Critical failure

A critical notification is sent when:

- new health verification fails and rollback does not succeed; or
- deployment itself fails before a successful health verification outcome is available.

The notification logic runs before the explicit final failure step. Notification steps use `continue-on-error: true` so that notification delivery does not replace the actual deployment state. fileciteturn102file0L2-L2

The webhook URL is supplied through the GitHub Actions secret `DISCORD_WEBHOOK_URL`; the secret value itself is never stored in this documentation.

---

## Final Job Status

The final deployment status is authoritative.

The workflow checks:

```text
steps.verify-new.outcome != 'success'
```

If the new deployment was not successfully verified, the job exits with code `1` after the notification and summary steps have run.

Therefore:

| New deployment | Rollback | Final job state |
|---|---|---|
| Healthy | Not needed | Success |
| Unhealthy | Successful | Failure, production restored to previous version |
| Unhealthy | Failed or unavailable | Failure, critical state |
| Deployment failed before health verification | Not applicable | Failure, critical state |

The table describes the state machine implemented by the workflow; it is not a claim that every failure mode has been simulated in production. fileciteturn102file0L2-L2

---

## Image and Version Traceability

Production deployment uses the GitHub commit SHA as the Docker image tag:

```text
ghcr.io/sasan-boddouhi/onlinestoreapi:<commit-sha>
```

The same SHA is passed to the application through `GIT_COMMIT` and can be observed through `/version`.

This creates a traceability chain:

```text
Git commit
   ↓
GitHub Actions
   ↓
GHCR image tag
   ↓
Production container
   ↓
GIT_COMMIT
   ↓
/version
```

The deployment workflow also checks the deployed version after a successful health verification. That check is informational and does not determine rollback; rollback is driven by the health verification outcome. fileciteturn102file0L2-L2

---

## Production Runner

The deployment job executes on the self-hosted runner matching:

```text
self-hosted
Linux
X64
onlinestore-prod
```

The runner therefore needs access to the production Docker environment and the production Compose deployment directory used by the workflow. fileciteturn100file0L2-L2

The build, test, coverage, and Docker packaging jobs remain on GitHub-hosted Ubuntu runners; only the production deployment job uses the self-hosted production runner. fileciteturn100file0L2-L2

---

## Security and Operational Notes

### Secrets

The workflow references secrets through GitHub Actions secret variables rather than embedding secret values in the workflow documentation.

The relevant notification secret is:

```text
DISCORD_WEBHOOK_URL
```

Other production secrets are supplied through the production environment rather than committed into this repository.

### Production data

The rollback operation is deliberately scoped to the API container. SQL Server, Redis, and their persistent volumes are not recreated by the rollback commands in the workflow. fileciteturn101file0L2-L2

### Self-hosted runner

The production runner is a security-sensitive component. Because it can execute deployment commands against the production environment, access to the runner and its registration/configuration should be tightly controlled.

---

## Controlled Rollback Test

The workflow contains a controlled rollback test through `workflow_dispatch`.

To exercise it, the workflow is manually dispatched on `main` with:

```text
force_verify_fail = true
```

The workflow then:

1. builds and publishes the selected commit image;
2. captures the current production SHA;
3. deploys the new image;
4. intentionally fails the new health verification;
5. enters the rollback path;
6. verifies the restored API health and version;
7. sends the rollback notification; and
8. finishes the deployment job as failed because the new deployment did not pass verification.

This test exists to validate rollback behavior without intentionally breaking the application itself. fileciteturn100file0L2-L2 fileciteturn101file0L2-L2 fileciteturn102file0L2-L2

---

## End-to-End Flow

```text
Developer pushes code
        |
        v
GitHub Actions
        |
        v
Build & Test
        |
        +----> Coverage Report
        |
        v
Docker Build & Push
        |
        v
GHCR commit-tagged image
        |
        v
Production self-hosted runner
        |
        v
Capture previous SHA
        |
        v
Deploy API image
        |
        v
Verify /health
     /       \
  success    failure
    |           |
    v           v
Verify       Rollback
Version        |
    |       /      \
    |   success    failure
    |      |          |
    v      v          v
Notify   Notify     Notify
Success  Rollback   Critical
    |      |          |
    |      +----+-----+
    |           |
    v           v
  Job Success   Job Failure
```

The key property is that **rollback is a recovery action, not a success override**. If a new deployment fails health verification, the workflow records that failure even when the previous version is successfully restored. fileciteturn101file0L2-L2 fileciteturn102file0L2-L2

---

## Related Documentation

- [`architecture.md`](architecture.md) — application architecture and dependency structure.
- [`authentication.md`](authentication.md) — authentication, JWT, refresh tokens, sessions, and authorization.
- [`deployment.md`](deployment.md) — Docker and production deployment architecture.
- [`../README.md`](../README.md) — project overview and entry points.

## Primary Source

The authoritative CI/CD implementation is:

```text
.github/workflows/ci-cd.yml
```

Documentation should be updated when the workflow behavior changes.