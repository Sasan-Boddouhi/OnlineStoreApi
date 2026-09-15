# CI/CD Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the separate CI and Docker workflows with one gated CI/CD workflow that tests on GitHub-hosted runners, publishes the Docker image to GHCR, and deploys only trusted `main` pushes to the `onlinestore-prod` self-hosted runner.

**Architecture:** A single `.github/workflows/ci-cd.yml` contains three gates: `build-and-test` on `ubuntu-latest`, `docker` on `ubuntu-latest` after successful tests, and `deploy` on the production self-hosted runner after a successful image push. The deployment job never checks out repository code; it executes the already validated production Compose file under `~/onlinestore-prod`, recreates only the API service, and verifies `/health` and `/version`.

**Tech Stack:** GitHub Actions, .NET 8, Docker Buildx, GitHub Container Registry (GHCR), Docker Compose, Ubuntu self-hosted runner.

**Spec:** `docs/superpowers/specs/2026-09-15-ci-cd-design.md`

## Global Constraints

- For pushes to `main`, build/test must succeed before Docker publish, and Docker publish must succeed before production deployment.
- Pull requests targeting `main` run build/test/coverage only; production deployment must never run for pull requests.
- The deploy job runs on `[self-hosted, Linux, X64, onlinestore-prod]` and is restricted to `github.event_name == 'push' && github.ref == 'refs/heads/main'`.
- Do not run checkout or untrusted PR code on the production runner.
- Deploy only the API service using `~/onlinestore-prod/docker-compose.prod.yml`.
- Do not remove, recreate, prune, or migrate SQL Server and Redis volumes during deployment.
- Preserve GHCR `latest` and commit-SHA image tags.
- Deployment fails when either `/health` or `/version` verification fails.
- Do not change application architecture, Docker image format, production Compose data volumes, or database persistence strategy.
- Keep the existing production `.env` on the VM as the source of deployment secrets; do not add secrets to the workflow file.

---

### Task 1: Create the unified CI/CD workflow

**Files:**
- Create: `.github/workflows/ci-cd.yml`

**Interfaces:**
- Consumes: repository source, `.NET 8` solution, `Dockerfile`, and GitHub Actions `GITHUB_TOKEN`.
- Produces: jobs named `build-and-test`, `docker`, and `deploy`, with `docker` gated by `build-and-test` and `deploy` gated by `docker`.

- [ ] **Step 1: Add workflow triggers and permissions**

Use `push` for `main` and `develop`, `pull_request` for `main`, and `workflow_dispatch` for manual validation. Keep package publishing permission at workflow level:

```yaml
name: CI/CD

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]
  workflow_dispatch:

permissions:
  contents: read
  packages: write

env:
  FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true
```

`workflow_dispatch` is for manual testing; the deploy condition remains restricted to a push to `main`.

- [ ] **Step 2: Port the existing build/test/coverage job**

Create `build-and-test` on `ubuntu-latest`, preserving the existing restore, Release build, coverage collection, coverage artifact upload, and `coverage-report` job behavior from the current `.github/workflows/dotnet.yml`. The test command remains:

```bash
dotnet test "Online Store Application.sln" --configuration Release --no-build --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

Keep `coverage-report` dependent on `build-and-test` and upload its HTML report as `coverage-report`. Do not introduce a self-hosted runner into these jobs.

- [ ] **Step 3: Add the Docker publish gate**

Create `docker` on `ubuntu-latest` with `needs: build-and-test`. Check out the repository, log in to GHCR using `github.actor` and `${{ secrets.GITHUB_TOKEN }}`, then build and push the existing root `Dockerfile` with these exact tags:

```yaml
tags: |
  ghcr.io/sasan-boddouhi/onlinestoreapi:latest
  ghcr.io/sasan-boddouhi/onlinestoreapi:${{ github.sha }}
```

Use `docker/login-action@v3` and `docker/build-push-action@v6`. Do not change the Dockerfile or add build arguments.

- [ ] **Step 4: Add the production deployment gate**

Create `deploy` with:

```yaml
needs: docker
if: github.event_name == 'push' && github.ref == 'refs/heads/main'
runs-on: [self-hosted, Linux, X64, onlinestore-prod]
```

Do not use `actions/checkout` in this job. Run only the existing production Compose commands from the VM:

```bash
cd ~/onlinestore-prod
docker compose -f docker-compose.prod.yml pull api
docker compose -f docker-compose.prod.yml up -d --force-recreate api
```

This intentionally targets only `api`; it must not use `down`, `rm` for database/cache services, volume commands, or prune commands.

- [ ] **Step 5: Add deployment verification**

After recreation, wait briefly and fail the job on HTTP errors:

```bash
sleep 10
curl --fail http://localhost:5000/health
curl --fail http://localhost:5000/version
```

Keep health and version verification as separate named steps so a failed check is immediately identifiable in the Actions log.

- [ ] **Step 6: Review the workflow for security and gate correctness**

Confirm all of the following before committing:

1. `build-and-test` has no self-hosted labels.
2. `docker` has `needs: build-and-test`.
3. `deploy` has `needs: docker`.
4. The deploy `if` expression allows only `push` to `refs/heads/main`.
5. The deploy job has no checkout step.
6. No production secret appears literally in YAML.
7. No Docker volume deletion/pruning command exists.
8. GHCR `latest` and `${{ github.sha }}` tags are both present.
9. `/health` and `/version` use `curl --fail`.

- [ ] **Step 7: Commit the unified workflow**

```bash
git add .github/workflows/ci-cd.yml
git commit -m "ci: add unified CI/CD pipeline"
```

### Task 2: Remove the superseded independent workflows

**Files:**
- Delete: `.github/workflows/dotnet.yml`
- Delete: `.github/workflows/docker-publish.yml`

**Interfaces:**
- Consumes: validated `.github/workflows/ci-cd.yml` from Task 1.
- Produces: a single source of truth for CI/CD, preventing duplicate builds and deployments after the unified workflow reaches `main`.

- [ ] **Step 1: Confirm the unified workflow is the intended replacement**

Compare its jobs against the old files before deletion. The current CI workflow contains build/test and coverage reporting, while the Docker workflow publishes `latest` and `${{ github.sha }}`; the new workflow must contain both behaviors plus the deployment gate.

- [ ] **Step 2: Delete the old CI workflow**

```bash
git rm .github/workflows/dotnet.yml
```

- [ ] **Step 3: Delete the old Docker publish workflow**

```bash
git rm .github/workflows/docker-publish.yml
```

- [ ] **Step 4: Review workflow directory state**

```bash
git status --short
git diff -- .github/workflows
```

Expected result: the new `ci-cd.yml` is present and the two superseded workflow files are staged for deletion.

- [ ] **Step 5: Commit the migration cleanup**

```bash
git add .github/workflows
git commit -m "ci: remove superseded workflows"
```

### Task 3: Validate the pipeline without exposing production to PR code

**Files:**
- Test/inspect: `.github/workflows/ci-cd.yml`

**Interfaces:**
- Consumes: the complete unified workflow from Tasks 1-2.
- Produces: evidence that PR validation executes only on GitHub-hosted runners and that the production deployment path is available only on trusted `main` pushes.

- [ ] **Step 1: Push the feature branch and open a pull request targeting `main`**

Push the implementation branch and open a PR to `main`. The PR event must exercise `build-and-test` and `coverage-report`; it must not execute `deploy` because the deploy condition requires a `push` event to `refs/heads/main`.

- [ ] **Step 2: Verify PR Actions results**

Confirm:

```text
build-and-test = success
coverage-report = success
Docker publish = not executed on the PR
Production deploy = not executed on the PR
```

If the workflow parser rejects the YAML, correct only the workflow syntax and rerun the PR checks; do not bypass the deploy condition.

- [ ] **Step 3: Merge the validated PR to `main`**

After PR checks succeed, merge to `main`. The resulting `push` event is the only event that should execute the complete production path.

- [ ] **Step 4: Verify the `main` pipeline gates**

Confirm the Actions run proceeds in this order:

```text
build-and-test -> docker -> deploy
```

The Docker job must not start when build/test fails, and deploy must not start when Docker publishing fails.

- [ ] **Step 5: Verify the production deployment result**

On the `onlinestore-prod` runner, confirm the deployment job succeeds and its final checks return HTTP success:

```bash
curl --fail http://localhost:5000/health
curl --fail http://localhost:5000/version
```

Also confirm the production services remain backed by the existing named Docker volumes; no volume deletion or recreation should have occurred.

- [ ] **Step 6: Commit only after verification evidence is available**

Use the repository's normal merge flow after the PR and `main` workflow have passed. Do not claim the pipeline is production-ready before both PR validation and the first successful `main` deployment are observed.

## Verification Matrix

| Scenario | Expected behavior |
| --- | --- |
| Push to `develop` | `build-and-test` and coverage run; no production deploy |
| PR to `main` | `build-and-test` and coverage run; no production deploy |
| Manual `workflow_dispatch` | Build/test and Docker publish may run; deploy condition remains false |
| Push to `main` | Build/test → Docker publish → production deploy |
| Build/test failure | Docker and deploy do not run |
| Docker publish failure | Deploy does not run |
| `/health` failure after deploy | Deploy job fails |
| `/version` failure after deploy | Deploy job fails |
| Production runner | No repository checkout/untrusted PR code |
| SQL Server/Redis persistence | Existing named volumes remain untouched |

## Spec Coverage / Self-Review

- Target flow is covered by Task 1 Steps 2–5 and Task 3 Steps 3–5.
- PR isolation is covered by Task 1 Step 4 and Task 3 Steps 1–2.
- Self-hosted runner restrictions are covered by Task 1 Step 4 and Task 3 Step 2.
- API-only deployment and volume safety are covered by Task 1 Step 4 and Task 3 Step 5.
- GHCR `latest` and commit-SHA tags are covered by Task 1 Step 3.
- Health/version failure behavior is covered by Task 1 Step 5 and Task 3 Step 5.
- Migration from the two old workflows is covered by Task 2.
- No placeholders such as TBD/TODO are used.
- No application-code, Dockerfile, Compose-volume, or database-persistence changes are introduced.
