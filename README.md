# Online Store API

A production-style e-commerce REST API built with **ASP.NET Core 8**, with a layered architecture, reusable application infrastructure, specification-based querying, JWT authentication, automated testing, structured logging, caching, and containerized deployment.

The repository is designed as a practical backend engineering project: application behavior, infrastructure concerns, security, testing, and deployment are separated into explicit components rather than being concentrated in controllers.

---

## Table of Contents

- [Features](#features)
- [Architecture](#architecture)
- [Query Pipeline](#query-pipeline)
- [Authentication and Security](#authentication-and-security)
- [Error Handling](#error-handling)
- [Observability and Performance](#observability-and-performance)
- [Testing](#testing)
- [CI/CD](#cicd)
- [Docker and Deployment](#docker-and-deployment)
- [Project Structure](#project-structure)
- [Technology Stack](#technology-stack)
- [Getting Started](#getting-started)
- [API Documentation](#api-documentation)
- [Health and Version Endpoints](#health-and-version-endpoints)
- [Documentation](#documentation)
- [Repository Workflow](#repository-workflow)
- [License](#license)

---

## Features

### Architecture

- Layered architecture with dependency inversion
- Repository Pattern
- Unit of Work Pattern
- Specification Pattern
- Separation of application contracts, business logic, persistence, and presentation concerns

### Querying

- Dynamic query pipeline
- Type-safe query contracts
- Filtering
- Sorting
- Pagination
- Reusable specifications
- Projection-first queries
- Query normalization and policy validation

### Authentication and Security

- JWT Bearer authentication
- Refresh-token rotation
- Refresh-token reuse detection
- Refresh-token family revocation
- BCrypt password hashing
- Per-device user sessions
- Active-session limit of five sessions per user
- Login lockout protection
- Login and refresh rate limiting
- SecurityStamp validation
- Role-based authorization
- Standardized error responses

### Performance

- `AsNoTracking` for read-only queries where appropriate
- Projection-first database queries
- Deferred `IQueryable` execution
- Query normalization
- Memory caching
- Redis distributed caching infrastructure
- Reusable specifications
- N+1 query prevention through query design

### Observability

- Structured logging with Serilog
- Query metrics middleware
- Health checks
- Version/commit endpoint

### Developer Experience

- Swagger / OpenAPI
- FluentValidation
- AutoMapper
- Async APIs and `CancellationToken` support
- Unit and integration testing infrastructure
- Docker and Docker Compose
- GitHub Actions CI/CD

---

## Architecture

The repository separates compile-time dependencies from runtime request flow.

### Compile-time dependency direction

```text
Online Store API
      |
      +--------------------+
      |                    |
      v                    v
BusinessLogic          DataLayer
      |                    |
      +---------+----------+
                |
                v
          Application
```

The API project references the application, business-logic, and data-layer projects. BusinessLogic and DataLayer depend on Application contracts and abstractions.

### Runtime request flow

```text
HTTP Request
     |
     v
Presentation / Controllers
     |
     v
BusinessLogic / Services
     |
     v
DataLayer / EF Core / Repository
     |
     v
SQL Server
```

The distinction matters: the dependency graph describes which projects reference which other projects, while the runtime flow describes how a request is processed.

For the detailed architecture, see [`docs/architecture.md`](docs/architecture.md).

---

## Query Pipeline

The project centralizes dynamic querying through a reusable pipeline:

```text
Query String
     |
     v
StringQueryParser
     |
     v
QueryContract<TEntity>
     |
     v
QueryPolicy.Normalize()
     |
     v
QueryPolicy.Validate()
     |
     v
QueryContractMapper.ToSpec()
     |
     v
Spec<TEntity>
     |
     v
SpecificationEvaluator
     |
     v
EF Core / Repository
```

This keeps parsing, normalization, validation, specification construction, and database execution separate from controller code.

### Example queries

Filtering:

```http
GET /api/products?filter=price gt 1000
```

Sorting:

```http
GET /api/products?sort=-price,name
```

Pagination:

```http
GET /api/products?page=1&size=10
```

Combined query:

```http
GET /api/products?filter=price gt 1000 and category.name eq 'electronics'&sort=-price&page=2&size=10
```

The exact supported query syntax is defined by the query parser, query contract, policy, and specification infrastructure in the source code.

---

## Authentication and Security

The authentication subsystem includes:

- Registration and login
- BCrypt password hashing
- JWT access tokens
- Session-bound JWT claims
- Refresh-token rotation
- Refresh-token reuse detection
- Refresh-token family revocation
- Concurrent refresh protection
- Per-user active-session limits
- Login lockout after repeated failed authentication attempts
- Login and refresh rate limiting
- SecurityStamp validation during JWT validation
- Role-based authorization
- Logout and logout-all behavior

The access token contains the session identity used by the authentication pipeline. During token validation, the application verifies the corresponding active user session and current security stamp.

For the complete authentication flow and failure scenarios, see [`docs/authentication.md`](docs/authentication.md).

---

## Error Handling

The API uses centralized exception handling and standardized `ProblemDetails` responses.

The application defines typed application exceptions for common conditions such as validation/business errors, conflicts, forbidden operations, unauthorized access, and missing resources.

A typical validation response follows the `application/problem+json` format:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Validation Error",
  "status": 422,
  "errors": {
    "Field": ["Validation message"]
  }
}
```

The exact error code, status code, and response fields depend on the exception and middleware path involved.

---

## Observability and Performance

### Logging

Serilog provides structured application logging so operational events can be recorded with named properties rather than relying only on unstructured text.

### Query metrics

Query metrics middleware provides visibility into query-related execution behavior.

### Caching

The application contains both in-memory caching and Redis-based distributed caching infrastructure. Cache usage is kept behind application services rather than being coupled directly to controllers.

### Database query behavior

Read-oriented query paths use techniques such as projection-first querying, `AsNoTracking`, deferred execution, reusable specifications, and query normalization to keep database access explicit and avoid unnecessary entity materialization.

---

## Testing

The solution contains dedicated unit, integration, and shared testing projects.

| Project | Purpose |
|---|---|
| `OnlineStore.Tests.Unit` | Fast isolated unit tests with mocked dependencies |
| `OnlineStore.Tests.Integration` | Integration tests against the ASP.NET Core application pipeline |
| `OnlineStore.Tests.Shared` | Shared fixtures, builders, constants, and test utilities |

The testing stack includes xUnit, FluentAssertions, Moq, WebApplicationFactory, SQLite In-Memory, and ReportGenerator.

The current suite contains **330 automated tests: 216 unit tests and 114 integration tests**.

Run all tests locally:

```bash
dotnet test
```

For the testing architecture, commands, fixtures, and coverage workflow, see [`TESTING.md`](TESTING.md).

---

## CI/CD

The GitHub Actions workflow is defined in `.github/workflows/ci-cd.yml`.

The pipeline is organized into four jobs:

```text
Build & Test
     |
     +----> Coverage Report
     |
     v
Docker Build & Push
     |
     v
Production Deployment
```

### Build and test

The workflow restores the solution, builds it in Release configuration, executes the test suite, collects Cobertura coverage data, and uploads the coverage artifact.

### Docker

After successful build and test execution, the Docker job builds the application image and publishes commit-tagged images to GitHub Container Registry for events that allow publishing.

### Production deployment

Production deployment runs only for pushes to `main` and manually dispatched workflows on `main`.

The deployment process:

1. Captures the currently deployed commit SHA.
2. Deploys the new commit-specific API image.
3. Waits for the API to start.
4. Verifies `/health`.
5. Rolls back the API container to the previous SHA if the new deployment fails health verification and a valid previous SHA is available.
6. Verifies the rollback health and `/version`.
7. Sends state-specific Discord notifications.
8. Leaves the job failed when the new deployment was not successfully verified, even if rollback restored the previous version.

For the complete workflow and rollback state machine, see [`docs/ci-cd.md`](docs/ci-cd.md).

---

## Docker and Deployment

The repository contains a multi-stage `Dockerfile` and a development/local `docker-compose.yml`.

The container image:

- Builds with the .NET 8 SDK image.
- Publishes the API in Release configuration.
- Runs on the .NET 8 ASP.NET runtime image.
- Exposes port 80 inside the container.
- Includes a container health check against `/health`.

The Compose configuration provides:

- SQL Server 2022
- Redis 7 Alpine
- Online Store API
- Persistent Docker volumes for SQL Server and Redis
- An internal Compose network

Production uses a separate `docker-compose.prod.yml` on the production VM and pulls the API image from GHCR using a commit SHA tag.

For production deployment details, persistent data considerations, health checks, version tracking, and rollback behavior, see [`docs/deployment.md`](docs/deployment.md).

---

## Project Structure

The main solution projects are organized around application responsibilities:

```text
Application/
├── Entities/
├── Contracts/
├── Common/
│   └── Queries/
└── Specifications/

BusinessLogic/
├── Services/
├── DTOs/
├── Validators/
└── Mappings/

DataLayer/
├── Context/
├── Repositories/
├── Persistence/
└── Security/

Online Store Application/
├── Controllers/
├── Middleware/
└── Configuration/

tests/
├── OnlineStore.Tests.Unit/
├── OnlineStore.Tests.Integration/
└── OnlineStore.Tests.Shared/
```

The exact directory contents can evolve with the implementation; the architecture documentation describes the responsibility of each layer in more detail.

---

## Technology Stack

### Platform

- .NET 8
- ASP.NET Core Web API
- Entity Framework Core 8
- SQL Server

### Application infrastructure

- Repository Pattern
- Unit of Work Pattern
- Specification Pattern
- Dynamic Query Pipeline
- AutoMapper
- FluentValidation

### Security

- JWT Bearer Authentication
- Refresh Token Rotation
- BCrypt Password Hashing
- Session Management
- Rate Limiting

### Observability and caching

- Serilog
- Query Metrics Middleware
- Memory Cache
- Redis
- Health Checks

### Testing

- xUnit
- Moq
- FluentAssertions
- WebApplicationFactory
- SQLite In-Memory
- ReportGenerator

### Deployment

- Docker
- Docker Compose
- GitHub Actions
- GitHub Container Registry
- Self-hosted production runner

### API documentation

- Swagger / OpenAPI

---

## Getting Started

### Prerequisites

For local development, install:

- .NET 8 SDK
- SQL Server, or Docker if using the containerized setup

### Run with Docker

From the repository root:

```bash
git clone https://github.com/Sasan-Boddouhi/OnlineStoreApi.git
cd OnlineStoreApi

docker compose up -d --build
```

The local Compose configuration publishes the API on:

```text
http://localhost:5000
```

The Compose file injects the SQL Server connection string and JWT configuration through environment variables. Do not commit real credentials or signing keys to the repository.

### Run locally with .NET

Restore the solution:

```bash
dotnet restore
```

Apply EF Core migrations using the project's configured startup/data-layer setup:

```bash
dotnet ef database update
```

Run the API:

```bash
dotnet run
```

The exact local URL depends on the launch profile and development configuration.

---

## API Documentation

Swagger/OpenAPI is configurable by environment.

In development, Swagger is enabled by the application configuration. In production, it can be enabled explicitly through the `Swagger__Enabled` configuration value.

When running the local Docker Compose setup, the API is published on port `5000`; if Swagger is enabled, its usual path is:

```text
http://localhost:5000/swagger
```

---

## Health and Version Endpoints

### Health

```http
GET /health
```

The health endpoint exposes application health information and checks the configured infrastructure dependencies.

### Version

```http
GET /version
```

The version endpoint exposes the deployed application version and the `GIT_COMMIT` value when available. The CI/CD deployment uses this endpoint to identify the currently deployed commit before a deployment and to verify the version after rollback.

---

## Documentation

The repository documentation is maintained in English.

| Document | Purpose |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Architecture, layers, dependencies, and application structure |
| [`docs/authentication.md`](docs/authentication.md) | Authentication, JWT, sessions, refresh tokens, and authorization |
| [`docs/deployment.md`](docs/deployment.md) | Docker and production deployment behavior |
| [`docs/ci-cd.md`](docs/ci-cd.md) | GitHub Actions pipeline, deployment gates, verification, and rollback |
| [`TESTING.md`](TESTING.md) | Unit/integration testing and coverage workflow |

The workflow implementation remains the authoritative source for CI/CD behavior, and the source code remains authoritative for runtime application behavior.

---

## Repository Workflow

A typical development flow is:

```text
Feature / Fix
     |
     v
Pull Request
     |
     v
Build & Test
     |
     v
Merge to main
     |
     v
Docker Build & Push
     |
     v
Production Deployment
     |
     v
Health Verification
     |
     +---- failure ----> Rollback
     |
     v
Deployment Result
```

Documentation-only changes are excluded from the push-triggered CI workflow by the workflow's `paths-ignore` configuration. Pull requests targeting `main` continue to use the pull-request workflow trigger.

---

## License

This project is licensed under the MIT License. See [`LICENSE`](LICENSE) for the license text.

---

Built with ASP.NET Core 8 and a focus on maintainable backend engineering.