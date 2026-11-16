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
- `BusinessException` (400 — invalid input)
- `UnprocessableEntityException` (422 — well-formed input rejected by a business precondition)
- `ConflictException` (409 — the resource is in a state that does not allow the action)
- `ForbiddenException`
- `NotFoundException`
- `UnauthorizedException`
- `BusinessRuleViolationException` — thrown by domain entities (`Order`, `OrderSellerGroup`, `Payment`, `SellerPayout`, `Inventory`) when a state-machine rule is broken. It derives from `InvalidOperationException` (so existing catch blocks keep working) and carries a `Code` (for example `ORDER_INVALID_STATE`, `GROUP_INVALID_STATE`, `PAYMENT_INVALID_TRANSITION`, `PAYOUT_INVALID_STATE`, `INSUFFICIENT_STOCK`). It is not an `AppException`, so the domain layer stays free of HTTP concerns.

`ExceptionHandlingMiddleware` translates these exceptions into HTTP Problem Details responses. Status convention: invalid input 400/422, state conflict 409, never 500 for a domain rule. The middleware also maps `DbUpdateConcurrencyException` to 409 `CONCURRENCY_CONFLICT` and a database unique-index/key violation to 409 `DUPLICATE_RECORD` (SQL Server error 2601/2627; database details are not returned). Any other `DbUpdateException` stays a 500.

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

Output caching is opt-in. There is no base policy: an endpoint is cached only when it carries a named policy from `OutputCachePolicies` (`Online Store Application/Extensions/OutputCacheExtensions.cs`). Both policies ignore the query string, so random query parameters cannot add entries.

| Policy | Duration | Endpoints |
|---|---|---|
| `ReferenceData` | 60 s | `GET categories`, `categories/tree`, `categories/{id}`, `brands`, `brands/{id}`, `shipping-methods`, `shipping-methods/{id}`, `provinces/{id}`, `cities/{id}`, `cities/by-province/{provinceId}` |
| `ProductDetail` | 30 s | `GET products/{id}` |

Carts, inventory, auth and health endpoints are explicitly marked no-store. Never put a policy on anything that depends on the current user, the guest cart session header (`X-Cart-Session`), payments, orders, or live stock. The product list is not output-cached; it uses the data cache below. `UseOutputCache` runs after CORS, authentication and authorization. Output caching is off in the Testing environment unless `OutputCaching:Enabled=true` (see `OutputCacheIsolationTests`).

The data cache (`ICacheService` / `RedisCacheService`) is an optimisation only: a Redis error is logged and treated as a miss (reads) or ignored (writes and removals), so the catalog keeps working from the database when Redis is down. Query cache keys are `{prefix}:q:{sha256}` of the normalised query (paging, filter, sort field and direction), so key length is fixed whatever the filter text.

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

## 12. M6 — Dual-Mode Commerce (Store Mode + Marketplace Mode)

M6 added the marketplace layer on top of the single-vendor core. This section
records only what a reader must know to change the system safely; the
per-phase reports (`M6-PHASE{0..9}-REPORT.md`) hold the full history.

### 12.1 The catalog chain

```text
Product → ProductVariant → SellerProduct (listing) → Inventory
```

- `Product.Price` is **display-only**. The authoritative price for a purchase is
  `SellerProduct.Price` (locked decision D2).
- `Inventory` is keyed by `ListingId`, not `ProductId` (D1), so two sellers can
  stock the same product independently.
- `OrderItem` references `ListingId` (D5), which is what makes seller payouts
  possible.

### 12.2 Ownership

`Seller.UserId` is a required 1:1 link to `User` (D3). `SellerDashboardService`
and `PaymentService` both resolve "who is calling" through `ICurrentUserService`
and fall back to `ForbiddenException` when the caller is neither the owner nor an
admin.

### 12.3 Payments

`Payment.State` is the single source of truth (`Initiated → Pending →
Succeeded → Refunded`, with `Failed` / `Cancelled` as terminal side branches).
The only writer is `Payment.TransitionTo`, which throws on an illegal edge. The
legacy `Status` column was dropped in Phase 9 (R39).

`IPaymentGateway` is resolved at startup from `PaymentGatewaySettings.Provider`.
`FakePaymentGateway` is used for `Manual`; `ZarinpalPaymentGateway` fails closed
unless `EnableRealGateway` is true and `MerchantId` is set.

Idempotency is enforced at three levels: a filtered unique index on
`IdempotencyKey`, a service-level replay check, and a race handler that re-reads
the winner after `DbUpdateException`.

#### Money model (Batch 45)

`IPricingService` (`PricingService`, pure, no database) is the only place that
prices an order; checkout calls it and writes the result to `Order` and each
`OrderSellerGroup`. All amounts are whole Rial (`RialMoney`: half away from
zero; the remainder of a split goes to the largest group by items sub-total,
ties to the highest seller id — Batch 45b). A line total (quantity × unit
price, quantity may be fractional) is rounded the same way
(`RialMoney.LineTotal`) in the cart and in the order. Listing prices and
shipping-method costs with a fraction are refused with 422 `VALIDATION_ERROR`.

- Shipping is per parcel: each seller group pays the method's `BaseCost`
  (`OrderSellerGroup.ShippingAmount`) and that amount is **credited** to the
  seller who ships it.
- A platform-funded coupon (`Coupon.FundedBy = Platform`, the default) is the
  platform's cost (`PlatformDiscountShare`); it is never deducted from the
  seller, and commission is taken on the item price before the coupon.
- The coupon is capped so the payable total stays at or above
  `PaymentGatewaySettings:MinimumAmountRial`.
- Seller payable = sub-total − seller-funded discount − commission + shipping.
- Invariant: `Σ seller payable + Σ commission − platform coupon cost = amount paid`.

`PaymentService` refuses (409 `AMOUNT_NOT_WHOLE_RIAL`) an order total with a
fraction of a Rial instead of flooring it. Seller debts keep an explicit
`RemainingAmount`; payout netting reads it. Groups priced before Batch 45 keep
their stored `SellerPayable`. Parcel- or item-level reversal and partial refunds
are not part of this model yet (Batch 64).

### 12.4 Denormalized rating cache

`Product` and `Seller` carry `AverageRating` and `ReviewCount`. They are **cache,
not truth**: truth is the approved rows in `ProductReview` / `SellerReview`.
Every mutation path that changes the approved set must call
`IReviewRatingCacheService`; the only current caller is
`ReviewService.Approve*ReviewAsync`.

Treat a stale value as a bug: nothing recomputes it in the background.

### 12.5 Auditing

`IAuditService.RecordAsync` is the only writer of `AuditLog`. It deliberately
does **not** call `SaveChangesAsync` — the audit row must commit or roll back in
the same transaction as the business change. Callers save.

Currently audited: payment state changes, product price changes, product
deletion, order cancellation.

### 12.6 Background work

`AbandonedPaymentCleanupService` (`BackgroundService`, every 15 minutes) delegates
to `IAbandonedPaymentSweeper`, which reconciles `Pending` payments older than 30
minutes with the gateway before anything is cancelled (Batch 43). The money
decision for each payment is in `IPaymentService.ReconcileStalePaymentAsync`:

| Gateway inquiry answer | Result |
|---|---|
| paid, not verified, order still payable | captured through the normal verify path (`source=reconciliation`) |
| already verified (money captured) | recorded `Succeeded`; if the order is no longer payable it goes to the review queue with `refundNeeded=true` |
| paid, not verified, order no longer payable | **not** verified, cancelled, flagged for review (the gateway returns unverified money) |
| not paid | cancelled |
| unknown | left `Pending`, flagged for review once |
| inquiry turned off, verify never started | cancelled, not flagged (Batch 43b: Zarinpal captures only on our Verify, so nothing was captured) |
| inquiry turned off, verify started, outcome unknown | left `Pending`, flagged for review once |

`AbandonedOrderCleanupService` asks the same question for the open payments of an
order before it cancels the order. `PaymentGatewaySettings:EnableInquiry` is
`false` by default (the Zarinpal inquiry method is documented but not yet
confirmed for the merchant); while it is off the jobs never verify a payment,
the payment job cancels stale payments whose verify never started (the order job
flags them instead of cancelling), and an admin settles each flagged payment with
`POST /api/v1/payments/verify-by-authority`. `AbandonedPaymentSettings:InquireGatewayBeforeCancel=false`
restores the old behaviour (cancel after the timeout without asking).
The clock is injected as `TimeProvider` so the behaviour is testable without waiting.

A failed payment does **not** release its inventory reservation. The order stays
`Pending` so the customer can retry; release happens only on explicit
cancellation. This is a deliberate Phase 8 decision (see the Phase 8 report).
