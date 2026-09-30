# CI/CD Pipeline Design

## Goal
Turn the existing independent CI and Docker workflows into a gated CI/CD pipeline for the Online Store API.

## Target Flow
For pushes to `main`:
1. Build and test on `ubuntu-latest`.
2. Only after successful tests, build and push the Docker image to GHCR.
3. Only after a successful image push, deploy on the `onlinestore-prod` self-hosted runner.
4. Capture the currently deployed commit SHA.
5. Pull the new API image, recreate only the API container, verify health and deployed version, and roll back the API container when the new health verification fails.

For pull requests targeting `main`, run build/test/coverage only. Never execute production deployment on the self-hosted runner.

For a manual `workflow_dispatch` on `main`, the same deployment path may be used with the controlled `force_verify_fail` input to exercise rollback behavior.

## Workflow Structure
Use a single `.github/workflows/ci-cd.yml` with three gated jobs:
- `build-and-test`: `ubuntu-latest`
- `docker`: `ubuntu-latest`, `needs: build-and-test`
- `deploy`: self-hosted labels `self-hosted`, `Linux`, `X64`, `onlinestore-prod`, `needs: docker`

The deploy job is restricted to a push to `refs/heads/main` or a manual `workflow_dispatch` on `refs/heads/main`.

## Deployment Safety
- Never execute untrusted pull-request code on the production runner.
- The deployment job may check out trusted main-branch deployment configuration.
- Deploy only the API service.
- Do not remove, recreate, prune, or migrate SQL Server and Redis volumes as part of deployment.
- Preserve GHCR `latest` and commit-SHA tags.
- Fail the deployment if health or version verification fails.

## Verification
After deployment:
- Wait briefly for the container to become ready.
- `curl --fail http://localhost:5000/health`
- `curl --fail http://localhost:5000/version`

## Migration
Create and validate the unified workflow first. Once it is proven, remove the old independent workflow files to avoid duplicate builds/deployments.

## Non-Goals
- No changes to application architecture.
- No changes to Docker image format or production Compose data volumes.
- No production deployment for pull requests.
- No changes to database persistence strategy.
