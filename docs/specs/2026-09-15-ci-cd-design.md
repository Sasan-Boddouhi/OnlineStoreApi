# CI/CD Pipeline Design

## Goal
Turn the existing independent CI and Docker workflows into a gated CI/CD pipeline for the Online Store API.

## Target Flow
For pushes to `main`:
1. Build and test on `ubuntu-latest`.
2. Only after successful tests, build and push the Docker image to GHCR.
3. Only after a successful image push, deploy on the `onlinestore-prod` self-hosted runner.
4. Pull the new API image and recreate only the API container using `~/onlinestore-prod/docker-compose.prod.yml`.
5. Verify `/health` and `/version` from the VM.

For pull requests targeting `main`, run build/test/coverage only. Never execute production deployment on the self-hosted runner.

## Workflow Structure
Use a single `.github/workflows/ci-cd.yml` with three gated jobs:
- `build-and-test`: `ubuntu-latest`
- `docker`: `ubuntu-latest`, `needs: build-and-test`
- `deploy`: self-hosted labels `self-hosted`, `Linux`, `X64`, `onlinestore-prod`, `needs: docker`

The deploy job is restricted to a push event on `refs/heads/main`.

## Deployment Safety
- Do not run checkout or untrusted PR code on the production runner.
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
