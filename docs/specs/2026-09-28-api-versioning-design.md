# API Versioning Design

**Date:** 2026-09-28  
**Status:** Approved for implementation

## Goal

Introduce explicit, maintainable API versioning without changing existing business behavior.

## Decisions

### 1. Versioning mechanism

Use the official ASP.NET API Versioning stack (`Asp.Versioning`) with URL-segment versioning.

Canonical v1 routes:

- `/api/v1/products`
- `/api/v1/users`
- `/api/v1/auth/...`

API version 1.0 is represented as `v1`.

### 2. Initial-development routing policy

The project is still in early development and has no external clients that require backward compatibility.

Therefore, **do not preserve unversioned legacy API routes**.

The versioned routes become the canonical public API immediately. Existing routes such as `/api/products`, `/api/users`, and `/api/auth` will be replaced by their `/api/v1/...` equivalents.

This avoids dual routing, compatibility code, and unnecessary technical debt at this stage.

### 3. Version policy

Introduce only v1 now. Add another version only for a genuine incompatible contract change.

### 4. Version discovery

Enable API-version reporting.

Use URL segments only initially. Do not assume a default version for unspecified requests.

### 5. Infrastructure endpoints

Keep `/health` and `/version` outside API versioning because they are operational infrastructure endpoints.

### 6. Swagger / OpenAPI

Make API Explorer/Swagger version-aware so documentation matches the actual versioned routes.

Swagger should expose v1 as the current API contract.

### 7. Controllers

Version ProductsController, UsersController, and AuthController.

Leave business logic, DTOs, repositories, authentication, caching, rate limiting, and persistence unchanged.

### 8. Testing

Add integration coverage for:

- v1 routes
- version metadata
- unsupported-version behavior
- rejection/non-availability of unversioned API routes
- existing endpoint behavior
- Swagger/API Explorer version grouping

### 9. Documentation

Update README/API documentation with the canonical v1 route convention.

Document that new breaking API contracts will receive a new version rather than silently changing v1.

## Implementation order

1. Add API Versioning packages.
2. Configure versioning services.
3. Configure API Explorer/OpenAPI integration.
4. Add v1 metadata and URL routes to controllers.
5. Remove reliance on unversioned API routes.
6. Add/update integration tests.
7. Update API documentation.
8. Run full test/build verification.
9. Review the diff and CI results.

## Non-goals

- No API business-logic changes.
- No database schema changes.
- No authentication/token contract changes.
- No packet/protocol changes.
- No versioning of `/health` or `/version`.
- No v2 implementation.
- No legacy API compatibility layer.

## Acceptance criteria

The implementation is complete when v1 routes are documented and tested, unversioned API routes are no longer part of the public contract, unsupported versions fail predictably, Swagger exposes the versioned API correctly, and the existing test suite remains green.
