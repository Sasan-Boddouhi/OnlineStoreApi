# API Versioning Implementation Plan

> **For agentic workers:** Use the host's available task-by-task implementation workflow. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce explicit URL-segment API versioning as the canonical public contract of the ASP.NET Core API.

**Architecture:** Use `Asp.Versioning.Mvc` for controller/API version semantics and `Asp.Versioning.Mvc.ApiExplorer` for version-aware Swagger discovery. All six API controllers are versioned under `/api/v1/...`; `/health` and `/version` remain version-neutral operational endpoints. Swagger will expose the v1 document without changing business services or persistence.

**Tech Stack:** .NET 8, ASP.NET Core MVC, Asp.Versioning 8.1.1, Swashbuckle 6.4.0, xUnit, FluentAssertions, WebApplicationFactory.

## Global Constraints
- v1 is the only API version.
- URL-segment versioning is canonical.
- Unversioned API routes are not preserved.
- Products, Users, Auth, EmployeeTypes, Employees, and Orders controllers must be covered.
- `/health` and `/version` remain outside API versioning.
- Do not change business logic, DTO contracts, authentication/token behavior, database schema, caching, rate limiting, or observability behavior.
- Unsupported or unspecified API versions must fail predictably rather than silently selecting v1.
- Swagger/API Explorer must represent v1 correctly.
- Existing tests must remain green.

---

### Task 1: Add versioning infrastructure

**Files:**
- Modify: `Online Store Application/Online Store Application(API).csproj`
- Modify: `Online Store Application/Program.cs`

**Interfaces:**
- Consumes: ASP.NET Core MVC registration and existing Swagger registration.
- Produces: API versioning services with URL segment reader and API Explorer metadata.

- [ ] **Step 1: Add the focused failing test**
Add an integration test that requests `/api/v1/products` and expects a non-404 response, plus `/api/products` expecting 404.

- [ ] **Step 2: Verify the relevant failure**
Run the focused integration test. Expected: v1 request is 404 because no versioned route exists.

- [ ] **Step 3: Implement the minimum behavior**
Add Asp.Versioning.Mvc and Asp.Versioning.Mvc.ApiExplorer 8.1.1. Register AddApiVersioning with API version 1.0, URL segment reader, ReportApiVersions=true, and no implicit default-version selection. Register AddApiExplorer with group format `'v'VVV`. Do not version infrastructure endpoints.

- [ ] **Step 4: Verify the focused pass**
Run the same focused integration test after controller metadata is present in Task 2. Expected: versioned routing resolves.

- [ ] **Step 5: Run the affected integration check**
Run the full integration project and update only tests that intentionally depend on old unversioned routes.

- [ ] **Step 6: Commit the passing deliverable**
Commit the package/configuration changes with a focused conventional commit.

---

### Task 2: Version all API controllers

**Files:**
- Modify: `Online Store Application/Controllers/ProductsController.cs`
- Modify: `Online Store Application/Controllers/UsersController.cs`
- Modify: `Online Store Application/Controllers/AuthController.cs`
- Modify: `Online Store Application/Controllers/EmployeeTypesController.cs`
- Modify: `Online Store Application/Controllers/EmployeesController.cs`
- Modify: `Online Store Application/Controllers/OrdersController.cs`

**Interfaces:**
- Consumes: versioning services from Task 1.
- Produces: canonical v1 routes for products, users, auth, employeetypes, employees, and orders.

- [ ] **Step 1: Add focused failing tests**
Add route assertions covering one representative GET per controller and Auth subroutes, plus an assertion that old `/api/...` routes are unavailable.

- [ ] **Step 2: Verify the relevant failure**
Run only the versioning integration tests. Expected: old routes resolve or v1 routes are missing before controller metadata is applied.

- [ ] **Step 3: Implement the minimum behavior**
Use `[ApiVersion(1.0)]` on each public API controller and `[Route("api/v{version:apiVersion}/[controller]")]` where conventional controller routing applies. Use `api/v{version:apiVersion}/auth` for Auth and `api/v{version:apiVersion}/orders` for Orders. Preserve action attributes and service calls.

- [ ] **Step 4: Verify the focused pass**
Run the same tests and assert v1 paths resolve while unversioned paths return 404.

- [ ] **Step 5: Run the affected integration check**
Run all controller integration tests after updating request URLs from `/api/...` to `/api/v1/...`.

- [ ] **Step 6: Commit the passing deliverable**
Commit controller route/version metadata changes.

---

### Task 3: Make Swagger version-aware

**Files:**
- Modify: `Online Store Application/Program.cs`
- Create: `Online Store Application/Swagger/ConfigureSwaggerOptions.cs` only if no equivalent exists.

**Interfaces:**
- Consumes: `IApiVersionDescriptionProvider` from API Explorer.
- Produces: Swagger document `v1` with versioned API paths.

- [ ] **Step 1: Add focused failing tests**
Add an integration test against `/swagger/v1/swagger.json` asserting HTTP 200 and that documented paths contain `/api/v1/`.

- [ ] **Step 2: Verify the relevant failure**
Run the focused Swagger test. Expected: v1 Swagger document is unavailable or does not expose versioned paths.

- [ ] **Step 3: Implement the minimum behavior**
Register one Swagger document per discovered API version using the API Explorer group name. Retain the existing Bearer security definition/requirement.

- [ ] **Step 4: Verify the focused pass**
Run the same Swagger test. Expected: v1 document is returned and API paths use the v1 segment.

- [ ] **Step 5: Run the affected integration check**
Run the integration suite and validate Swagger serialization.

- [ ] **Step 6: Commit the passing deliverable**
Commit version-aware OpenAPI configuration.

---

### Task 4: Documentation and regression verification

**Files:**
- Modify: `README.md`
- Modify: relevant integration tests under `OnlineStore.Tests.Integration/Controllers`
- Create: `OnlineStore.Tests.Integration/Infrastructure/ApiVersioningIntegrationTests.cs` if no suitable existing file exists.

**Interfaces:**
- Consumes: final v1 routing and Swagger contract.
- Produces: documented v1 API contract and regression coverage.

- [ ] **Step 1: Add focused failing tests**
Add tests for unsupported version behavior and version-reporting headers. Assert unsupported versions are rejected and v1 responses include supported-version metadata.

- [ ] **Step 2: Verify the relevant failure**
Run the focused tests. Expected: unsupported-version and version-reporting assertions fail before final configuration.

- [ ] **Step 3: Implement the minimum behavior**
Finalize version reporting, update controller integration request paths to v1, document canonical routes and version policy in README, and mark API Versioning complete in the roadmap.

- [ ] **Step 4: Verify the focused pass**
Run the focused versioning tests. Expected: all assertions pass.

- [ ] **Step 5: Run the affected integration check**
Run `dotnet test` for the complete solution. Expected: all existing and new tests pass.

- [ ] **Step 6: Commit the passing deliverable**
Commit documentation and regression coverage as the final API Versioning deliverable.

## Unresolved product decisions
None. The versioning contract was explicitly approved before implementation.