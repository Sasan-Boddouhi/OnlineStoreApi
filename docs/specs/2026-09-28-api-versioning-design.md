# API Versioning Design

**Date:** 2026-09-28  
**Status:** Approved for implementation

## Goal
Introduce explicit, maintainable API versioning without changing existing business behavior.

## Decisions

### 1. Versioning mechanism
Use the official ASP.NET API Versioning stack (`Asp.Versioning`) with URL-segment versioning.
Canonical v1 routes: `/api/v1/products`, `/api/v1/users`, `/api/v1/auth/...`.
API version 1.0 is represented as `v1`.

### 2. Compatibility
Keep existing unversioned routes during migration so current clients are not broken immediately. New development targets versioned routes.

### 3. Version policy
Introduce only v1 now. Add another version only for a genuine incompatible contract change.

### 4. Version discovery
Enable API-version reporting. Use URL segments only initially. Do not assume a default version for unspecified requests.

### 5. Infrastructure endpoints
Keep `/health` and `/version` outside API versioning because they are operational infrastructure endpoints.

### 6. Swagger / OpenAPI
Make API Explorer/Swagger version-aware so documentation matches actual versioned routes.

### 7. Controllers
Version ProductsController, UsersController, and AuthController. Leave business logic, DTOs, repositories, authentication, caching, rate limiting, and persistence unchanged.

### 8. Testing
Add integration coverage for v1 routes, version metadata, unsupported versions, legacy compatibility, existing behavior, and Swagger/API Explorer grouping.

### 9. Documentation
Update README/API documentation with canonical v1 routes and compatibility policy.

## Implementation order
1. Add API Versioning packages.
2. Configure versioning services.
3. Configure API Explorer/OpenAPI integration.
4. Add v1 metadata and URL routes to controllers.
5. Preserve legacy routes.
6. Add/update integration tests.
7. Update API documentation.
8. Run full test/build verification.
9. Review diff and CI results.

## Non-goals
- No API business-logic changes.
- No database schema changes.
- No authentication/token contract changes.
- No packet/protocol changes.
- No versioning of `/health` or `/version`.
- No v2 implementation.

## Acceptance criteria
The implementation is complete when v1 routes are documented and tested, legacy clients remain functional, unsupported versions fail predictably, Swagger exposes the versioned API correctly, and the existing test suite remains green.