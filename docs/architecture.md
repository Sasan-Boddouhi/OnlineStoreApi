# Architecture

## 1. Overview

`OnlineStoreApi` is an ASP.NET Core 8 REST API organized into four projects:

- `Online Store Application` — HTTP/API host and composition root.
- `BusinessLogic` — application services and business workflows.
- `Application` — entities, contracts, abstractions, specifications, query infrastructure, exceptions, and options.
- `DataLayer` — EF Core persistence, repository/unit-of-work implementations, and database configuration.

The compile-time project references are:

```text
Application <- BusinessLogic
Application <- DataLayer
API -> Application
API -> BusinessLogic
API -> DataLayer
```

`Application` does not reference `BusinessLogic` or `DataLayer`. The API project composes the concrete implementations through Dependency Injection.

## 2. Layer Responsibilities

### Application

`Application` is the shared core of the solution. It contains entities, repository and Unit of Work contracts, specification/query infrastructure, exceptions, models, interfaces, and strongly typed options such as `JwtOptions`, `DatabaseOptions`, and `RedisOptions`.

### BusinessLogic

`BusinessLogic` contains application services and business workflows and references `Application`. Services coordinate repositories, transactions, authentication/security components, mapping, caching, and current-user information according to the use case.

Examples include user management and authentication/session workflows.

### DataLayer

`DataLayer` references `Application` and provides persistence implementations, including `AppDbContext`, generic repository, Unit of Work, database configuration, security infrastructure, and EF Core interceptors.

### Online Store Application

The API project is the ASP.NET Core host and composition root. It references all three other projects and configures controllers, Dependency Injection, authentication/authorization, exception handling, rate limiting, CORS, output caching, health checks, Swagger, query metrics, Serilog, and startup database migrations.

## 3. Runtime Request Flow

A typical request follows this architectural path:

```text
HTTP Request
    |
    v
ASP.NET Core pipeline
    |
    +--> cross-cutting middleware
    |
    v
Controller
    |
    v
Business service
    |
    v
Application abstraction
    |
    v
DataLayer implementation
    |
    v
AppDbContext / EF Core
    |
    v
SQL Server
```

The exact middleware execution order remains defined by `Program.cs`; this diagram describes responsibilities rather than replacing the actual pipeline order.

## 4. Specification Pattern

The specification infrastructure is centered on `Spec<TEntity>`. A specification can contain:

- `Criteria` for filtering.
- `Includes` for related data.
- Ordering expressions.
- `Skip` and `Take` paging values.
- Read-only/tracking state.
- Query tags.

`Where()` combines additional criteria with `AndAlso`. Ordering, includes, paging, tracking, and tags are accumulated on the specification. Specifications are read-only by default; `AsTracking()` switches to tracked behavior.

```text
Spec<TEntity>
  ├── Criteria
  ├── Includes
  ├── OrderExpressions
  ├── Skip / Take
  ├── IsReadOnly
  └── Tags
```

## 5. Dynamic Query Pipeline

Dynamic filtering/sorting is separated from EF Core execution:

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
QueryContract -> Spec<TEntity>
    |
    v
SpecificationEvaluator
    |
    v
Repository / EF Core
```

`StringQueryParser` parses optional filter and sort strings and carries the supported paging representations into `QueryContract<TEntity>`:

- `page` + `size`
- `skip` + `take`

Parsing errors are returned as a failed parse result instead of silently producing an executable query.

`QueryContract<TEntity>` represents filter, sort, and paging intent. The query policy normalizes and validates that intent before it is converted to a specification and evaluated against persistence.

## 6. Specification Evaluation

The specification evaluator translates specification information into EF Core query operations, including criteria, includes, ordering, paging, and tracking behavior.

Read-only queries use no-tracking behavior in the repository path. This keeps read-oriented queries separate from workflows that need EF Core tracking.

## 7. Repository Pattern

The generic repository in `DataLayer` supports:

- Entity retrieval by ID.
- Add/add-range.
- Update/update-range.
- Delete/delete-range.
- Predicate-based `AnyAsync` and `CountAsync`.
- Specification-based list, first-item, count, and existence queries.
- Projection-based queries into result/DTO types.
- `IQueryable` access where the repository abstraction requires it.

The repository is a persistence implementation; business services consume the repository abstractions exposed by `Application`.

### Projection-first reads

The repository supports projection queries so DTO/result shapes can be selected directly instead of requiring a complete entity graph to be materialized first.

## 8. Unit of Work

`UnitOfWork` owns `AppDbContext`, provides repository access, and manages transactions.

Repository instances are cached by entity type in a thread-safe dictionary during the lifetime of the Unit of Work.

The transaction lifecycle is:

```text
BeginTransactionAsync
        |
        v
   application work
        |
        v
SaveChangesAsync
        |
        v
CommitTransactionAsync
```

On failure, `RollbackTransactionAsync` rolls back and disposes the active transaction.

## 9. Persistence Model

`AppDbContext` defines the EF Core model for users, customers, employees, addresses, catalog entities, inventory, orders, invoices, payments, logs, refresh tokens, and user sessions.

It also configures relational constraints, indexes, and cascade/restrict behaviors. Examples include unique indexes for user phone number, product barcode, category/subcategory names, warehouse name, invoice number, and payment transaction ID.

Important relationship groups include:

- User → Customer.
- User → Employee.
- Category → Subcategory → Product.
- Product/Warehouse → Inventory.
- Customer → Order → OrderItem.
- Order → optional Invoice → Payment.
- User → UserSession → RefreshToken.

`AppDbContext` accepts EF Core save-change interceptors, allowing cross-cutting persistence behavior such as auditing to be applied centrally.

## 10. Exception Handling

Application-specific failures are represented under `Application/Exceptions`, including:

- `AppException`
- `BusinessException`
- `ConflictException`
- `ForbiddenException`
- `NotFoundException`
- `UnauthorizedException`

`ExceptionHandlingMiddleware` translates these exceptions into HTTP Problem Details responses.

FluentValidation exceptions are returned as validation Problem Details with HTTP 422 and property-level validation errors. Unexpected exceptions become HTTP 500 responses with a generic client-facing message and are logged server-side.

Responses include a `traceId` so client-visible failures can be correlated with server logs.

```text
Exception
   |
   +--> AppException ----------------> mapped HTTP status
   |
   +--> FluentValidation exception -> 422 validation response
   |
   +--> unexpected exception -------> 500 response + server log
```

## 11. Options Pattern

Configuration is represented through strongly typed options classes, including:

- `JwtOptions`
- `DatabaseOptions`
- `RedisOptions`

The API binds these options during startup. Database options are validated as part of startup configuration. This keeps configuration structure separate from service implementation and avoids spreading raw configuration-key access through business code.

## 12. Authentication and Authorization Boundary

Authentication is configured in the API host and implemented through business-layer services.

JWT validation is configured in `Program.cs`. Token validation also checks the persisted session identified by the token and validates the user's SecurityStamp, so a signed token is not treated as the only source of session validity.

The API registers authorization policies. The catalog-management policy currently permits the `Admin` or `Manager` roles.

Detailed authentication/session behavior belongs in `docs/authentication.md`.

## 13. Caching and Cross-cutting Infrastructure

The API composition root registers memory caching and Redis-based caching. Redis is also used for distributed output caching outside the Testing environment.

Serilog provides structured logging. Query metrics middleware provides request/query diagnostics. Health checks cover SQL Server and Redis and are exposed through the API health endpoint.

## 14. Dependency Injection

The API composition root registers concrete implementations for the Application and BusinessLogic abstractions. Infrastructure services include current-user access, password hashing, JWT token generation, query metrics, Redis caching, FluentValidation, EF Core/SQL Server, and Redis.

This keeps service construction out of controllers and business workflows.

## 15. Controller-to-Data Example

A product/catalog query follows the same separation:

```text
ProductsController
      |
      v
application/business service
      |
      v
StringQueryParser
      |
      v
QueryContract
      |
      v
QueryPolicy
      |
      v
Spec<TEntity>
      |
      v
Repository
      |
      v
SpecificationEvaluator
      |
      v
EF Core
      |
      v
SQL Server
```

The controller is responsible for HTTP concerns. Query construction, business behavior, and persistence remain in their respective layers.

## 16. Architectural Rules

The current codebase follows these rules:

1. `Application` defines shared abstractions/core models and does not reference infrastructure projects.
2. `BusinessLogic` references `Application`, not the concrete DataLayer project.
3. `DataLayer` references `Application` and implements persistence concerns.
4. The API project is the composition root and can reference all solution layers.
5. Controllers delegate business work rather than implementing persistence logic.
6. Query parsing/policy validation is separated from EF Core execution.
7. Specifications represent query intent; the evaluator translates that intent to EF Core operations.
8. Read-only specification queries use no-tracking behavior.
9. DTO reads can use projection rather than loading complete entity graphs.
10. Exception-to-HTTP translation is centralized in middleware.
11. Strongly typed options are used where the project provides options classes.
12. Multi-operation business workflows can coordinate database transactions through the Unit of Work.

## 17. Source of Truth

This document describes the architecture implemented by the repository. The code is the authoritative source when behavior changes.

Related documentation:

- `docs/authentication.md` — authentication, sessions, JWT, refresh tokens, and security flows.
- `docs/deployment.md` — containerized deployment and production runtime configuration.
- `docs/ci-cd.md` — build, test, image publishing, deployment, verification, and rollback workflow.
