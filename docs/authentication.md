# Authentication & Authorization

This document describes the current authentication, session, token, and authorization behavior implemented by `OnlineStoreApi`.

> **Source of truth:** The implementation is authoritative. If authentication behavior changes in code, this document must be reviewed and updated.

## 1. Overview

The API authentication model is based on:

- JWT access tokens
- Refresh tokens
- Server-side user sessions
- SecurityStamp validation
- BCrypt password hashing
- Refresh token rotation
- Refresh token reuse detection
- Maximum active-session enforcement
- Login lockout
- Endpoint rate limiting

High-level flow:

```text
Client
  |
  | Register / Login
  v
AuthController
  |
  v
IAuthService
  |
  v
AuthService
  |
  +--> User
  +--> UserSession
  +--> RefreshTokenEntity
  +--> IPasswordHasher
  +--> IJwtTokenService
  |
  v
Database
```

A valid JWT alone is not sufficient for an authenticated request. JWT validation is followed by server-side session validation and SecurityStamp validation.

## 2. Authentication Endpoints

Base route:

```text
/api/auth
```

| Method | Endpoint | Authentication | Purpose |
|---|---|---|---|
| POST | `/api/auth/register` | Public | Register a user and create an initial session |
| POST | `/api/auth/login` | Public | Authenticate a user and issue access/refresh tokens |
| GET | `/api/auth/me` | Required | Return the current user's profile from claims |
| POST | `/api/auth/refresh` | Public | Rotate the refresh token and issue new tokens |
| POST | `/api/auth/logout` | Required | Revoke the current session and its refresh tokens |

## 3. Registration

Registration is handled by `AuthService.RegisterAsync`.

The operation runs inside a transaction:

```text
RegisterDto
    |
    v
Begin Transaction
    |
    v
Check duplicate PhoneNumber
    |
    +---- exists ----> BusinessException
    |
    v
Create User
    |
    +--> PasswordHash
    +--> UserType = Customer
    +--> IsActive = true
    +--> SecurityStamp = new GUID
    |
    v
Save User
    |
    v
Create UserSession
    |
    v
Create Access Token + Refresh Token
    |
    v
Commit Transaction
    |
    v
AuthResultDto
```

Registration therefore creates the user, session, and initial authentication result as one transactional workflow.

## 4. Password Storage

The raw password is not stored in the database.

The application uses `IPasswordHasher` for password hashing and verification. The registered implementation is `BcryptPasswordHasher`.

Conceptually:

```text
Raw Password
    |
    v
IPasswordHasher
    |
    v
BCrypt Hash
    |
    v
User.PasswordHash
```

During login, the supplied password is verified against the stored hash.

## 5. Login

Login is handled by:

```http
POST /api/auth/login
```

The service loads an active user by `PhoneNumber` and includes the employee/employee-type relationship when available.

A successful login resets failed-login state, enforces the active-session limit, creates a new session, and issues an access token plus refresh token.

## 6. Login Lockout

The current login lockout policy is implemented in `AuthService`:

```text
Maximum failed attempts = 5
Lock duration = 15 minutes
```

On an invalid password:

```text
FailedLoginAttempts++
```

When the threshold is reached, `LockoutEnd` is set to 15 minutes in the future.

On a successful login:

```text
FailedLoginAttempts = 0
LockoutEnd = null
```

## 7. Login Rate Limiting

The login endpoint uses the `LoginLimiter` policy and the register endpoint its own `RegisterLimiter` policy. Both are partitioned **per client IP**: each IP has its own quota, so one client cannot lock everyone else out.

Defaults (configurable through `RateLimiting:Login` and `RateLimiting:Register`):

```text
Login:    PermitLimit = 5, WindowSeconds = 60, QueueLimit = 0
Register: PermitLimit = 5, WindowSeconds = 60, QueueLimit = 0
```

The client IP is the TCP peer address, or the `X-Forwarded-For` address when the request comes from a trusted proxy configured in `ForwardedHeaders` (see `docs/deployment.md` §7). A rejected request receives `429 Too Many Requests` with a `Retry-After` header (seconds) and a ProblemDetails body:

```json
{
  "type": "https://api.yourstore.com/errors/rate_limit_exceeded",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "تعداد درخواست‌های شما بیش از حد مجاز است. لطفاً 60 ثانیهٔ دیگر دوباره تلاش کنید.",
  "instance": "/api/v1/auth/login",
  "code": "RATE_LIMIT_EXCEEDED",
  "traceId": "...",
  "retryAfterSeconds": 60
}
```

The integration test host raises the limits to 10 000 through configuration; `RateLimitingTestFactory` runs the real, small limits.

## 8. User Sessions

A successful registration or login creates a `UserSession`.

A session records information including:

```text
Session Id
User Id
Device Id
Device Name
IP Address
User Agent
CreatedAtUtc
LastActivityUtc
AbsoluteExpiryUtc
Status
```

`Device Id` and `Device Name` come from the request body. `IP Address` and `User Agent` come from the HTTP request itself: the resolved client IP (see section 7) and the `User-Agent` header, truncated to the column sizes. `ipAddress` and `userAgent` fields in the login or register body are ignored.

New sessions are created with:

```text
Status = Active
```

The session is the server-side state that connects authentication tokens to a revocable login instance.

## 9. Session Lifetime

Session limits come from `UserSessionOptions` (section `Session`) and `JwtOptions`, validated at startup (`ValidateOnStart`):

| Setting | Config key | Default |
|---|---|---|
| Access-token lifetime | `Jwt:ExpireMinutes` | 15 minutes |
| Idle timeout | `Session:IdleTimeoutDays` | 7 days |
| Absolute session lifetime | `Session:AbsoluteLifetimeDays` | 30 days |
| Rotated-token reuse grace window | `Session:RefreshReuseGraceSeconds` | 10 seconds |

- **Idle timeout** is measured from the last successful login or refresh (`UserSession.LastActivityUtc`). Ordinary authenticated requests do not update it; there is no database write per request. Refresh is the activity signal.
- **Absolute lifetime** starts at login. After it, the user must log in again, however active they were.
- Each refresh token lives for the idle timeout, capped at the session's absolute expiry.
- Startup fails unless the idle timeout is longer than the access-token lifetime, the absolute lifetime is at least the idle timeout, and the grace window is shorter than the access-token lifetime. (An idle timeout shorter than the access token was the cause of F-13: the first refresh after the access token expired always found the session idle and logged the user out.)

Refresh operations check both idle and absolute expiration. When a session is expired through this flow, its status is changed to `Expired` and refresh is rejected.

All session times use the injected `TimeProvider`. JWT signature and lifetime validation in `JwtBearer` still use the system clock.

## 10. Maximum Active Sessions

The current default maximum is:

```text
5 active sessions per user
```

When a user logs in, active sessions are ordered by `CreatedAtUtc` and the oldest sessions are revoked when necessary to make room for the new session.

Revoking an old session also revokes its related refresh tokens.

## 11. Access Tokens

Access tokens are JWTs generated by `JwtTokenService`.

The signing algorithm is:

```text
HMAC-SHA256
```

JWT configuration is read from:

```text
Jwt:Key
Jwt:Issuer
Jwt:Audience
Jwt:ExpireMinutes
```

The generated token contains claims including:

```text
NameIdentifier
SessionId
Role
FullName
PhoneNumber
SecurityStamp
```

## 12. SessionId Claim

Every access token contains a `SessionId` claim.

This claim identifies the server-side `UserSession` associated with the token:

```text
JWT
 |
 +--> UserId
 |
 +--> SessionId
 |
 +--> SecurityStamp
 |
 v
Database
```

This allows a session to be revoked server-side while the JWT itself remains unchanged.

## 13. Role Claim

The role is taken from the user's employee type when available:

```text
User.Employee.EmployeeType.TypeName
```

If no role can be obtained from the employee relationship, the service falls back to the user's `UserType`:

```text
Employee -> Employee
Customer -> Customer
```

The resulting role is stored in `ClaimTypes.Role`.

## 14. SecurityStamp

Users have a `SecurityStamp` value.

A new value is generated when the user is created and the value is also placed into the access token.

During JWT validation, the token's SecurityStamp is compared with the current database value:

```text
JWT.SecurityStamp == Database.SecurityStamp
```

If they differ, authentication is rejected.

This provides a server-side invalidation mechanism for access tokens when the user's security state changes.

## 15. JWT Validation

JWT Bearer authentication is configured with the following validations enabled:

```text
ValidateIssuer = true
ValidateAudience = true
ValidateLifetime = true
ValidateIssuerSigningKey = true
```

The application also configures:

```text
RoleClaimType = ClaimTypes.Role
NameClaimType = ClaimTypes.NameIdentifier
```

## 16. Database-backed Authentication Validation

After the JWT has passed the normal JWT validation stage, `OnTokenValidated` performs additional database checks.

### 16.1 UserId and SessionId

The application reads:

```text
ClaimTypes.NameIdentifier
SessionId
```

Both values must be valid identifiers.

### 16.2 Active Session

The database must contain an active session matching both the user and session identifiers:

```text
UserId == token.UserId
AND
SessionId == token.SessionId
AND
Status == Active
```

If the session is not active, authentication fails.

### 16.3 SecurityStamp

The current user is loaded from the database and its SecurityStamp is compared with the claim in the token.

A mismatch causes authentication to fail.

## 17. Refresh Tokens

Refresh tokens are generated using `RandomNumberGenerator` with 32 random bytes, then Base64-encoded.

The plaintext refresh token is not stored directly in the database.

The corresponding entity stores values including:

```text
TokenHash
TokenIdentifier
UserId
SessionId
CreatedAt
ExpiryDate
AbsoluteExpiry
FamilyCreatedAt
IsRevoked
ReplacedByTokenId
```

`TokenIdentifier` is derived from the token using SHA-256 and is used to locate the token record. The token value is separately verified against the stored hash.

## 18. Refresh Token Lifetime

A newly created or rotated refresh token has:

```text
ExpiryDate     = min(UtcNow + Session:IdleTimeoutDays, session AbsoluteExpiryUtc)
AbsoluteExpiry = session AbsoluteExpiryUtc
```

Presenting an expired refresh token revokes it and marks its session `Expired` (each session has exactly one live refresh token, so an expired token means the session can no longer be extended).

## 19. Refresh Token Rate Limiting

The refresh endpoint:

```http
POST /api/auth/refresh
```

uses the `RefreshLimiter` policy, partitioned per client IP.

Defaults (configurable through `RateLimiting:Refresh`):

```text
PermitLimit = 20
WindowSeconds = 60
QueueLimit = 0
```

Rejections use the same `429` response as login (section 7).

## 20. Refresh Token Rotation

Refresh token rotation is performed inside a transaction.

The normal flow is:

```text
Old Refresh Token
       |
       v
Validate
       |
       v
Generate New Refresh Token
       |
       v
Create New RefreshTokenEntity
       |
       v
Revoke Old Token
       |
       v
Old.ReplacedByTokenId = New.Id
       |
       v
Return New Access + Refresh Token
```

The old token is revoked and points to its replacement through `ReplacedByTokenId`.

## 21. Refresh Token Reuse Detection

A revoked refresh token is always rejected (`401`). What else happens depends on when and why it was revoked. The check runs before the expiry check, so a stolen token is detected even after it has expired.

| Case | Result |
|---|---|
| The session is no longer `Active` (logout, expiry, earlier revocation) | Rejected; nothing else to protect |
| Rotated (`ReplacedByTokenId != null`) and presented within `Session:RefreshReuseGraceSeconds` (default 10 s) of rotation | Rejected only. This covers two tabs refreshing at the same moment; the session and the newer token keep working |
| Rotated and presented after the grace window, or revoked without a replacement | **Reuse detected**: the whole token family is revoked (the session plus every refresh token in it, including the newer one). The access token of that session fails on its next request because the session is no longer `Active`. Metric `RefreshTokenReuse` is incremented |

Set `Session:RefreshReuseGraceSeconds` to `0` for strict detection with no grace window.

## 22. Refresh Token Signature Mismatch

After locating a refresh-token entity through its identifier, the supplied token value is verified against the stored token hash.

If verification fails, the service treats the event as suspicious and revokes the associated session and refresh tokens.

## 23. Refresh Token Concurrency

Refresh rotation is transactional.

If a `DbUpdateConcurrencyException` occurs during rotation, the transaction is rolled back and the refresh operation returns failure rather than leaving the token family in a partially updated state.

## 24. Logout

The endpoint:

```http
POST /api/auth/logout
```

requires authentication.

The `SessionId` claim identifies the current session.

The session is then revoked together with its related refresh tokens.

Conceptually:

```text
Authenticated Request
       |
       v
SessionId Claim
       |
       v
UserSession
       |
       +--> Status = Revoked
       |
       +--> Revoke related Refresh Tokens
```

## 25. Logout All

`AuthService` also provides `LogoutAllAsync(userId)`.

It finds all active sessions for the user and revokes each session and its related refresh tokens inside a transaction.

### 25.1 Client refresh contract

Access tokens live 15 minutes by default, so clients must refresh:

1. **Proactively**: refresh shortly before the access token's `exp` (for example 60 seconds before), and/or
2. **On 401**: when an API call returns `401`, call `POST /api/auth/refresh` once, then retry the original request with the new access token.

Rules:

- Every successful refresh returns a **new** refresh token. Replace the stored one immediately; the old one is now rotated.
- Run only one refresh at a time per session (a single in-flight promise, shared across tabs if the token is shared). A second refresh with the old token inside the grace window gets `401` without logging the user out; read the newly stored token and retry. After the grace window the same replay revokes the whole session.
- If refresh returns `401`, the session is over (idle more than 7 days, past 30 days, logged out, deactivated, or reuse detected). Clear the tokens and send the user to login. Do not retry refresh in a loop.
- A user who opens the site at least once every 7 days stays logged in for up to 30 days from login.

### 25.2 User deactivation and delete

`PATCH /api/users/{id}` with `isActive = false` and `DELETE /api/users/{id}` both deactivate the account. Delete is a soft delete: the row, orders and history remain. Deactivation:

- rotates the user's `SecurityStamp`, so every existing access token fails on its next request;
- revokes all of the user's sessions and refresh tokens in the same save.

Admin guards (`UserService`):

| Case | Response |
|---|---|
| An admin deactivates or deletes their own account | `409 CANNOT_DEACTIVATE_SELF` |
| Deactivating, deleting or removing the SuperAdmin role from the last active SuperAdmin | `409 LAST_SUPERADMIN` |
| A non-SuperAdmin deactivates, reactivates, deletes or changes the roles of a SuperAdmin | `403 ADMIN_CANNOT_MODIFY_SUPERADMIN` |

Registration, admin user creation and admin user update return `409 USER_EMAIL_EXISTS` for an email that another user already has. An invalid Persian date of birth on register returns `422`.

## 26. Authorization

Authentication and authorization are separate stages.

```text
Authentication
    |
    +--> JWT valid?
    +--> Session active?
    +--> SecurityStamp valid?
    |
    v
Authenticated User
    |
    v
Authorization
    |
    +--> Required role/policy satisfied?
```

The current `CanManageCatalog` policy requires one of:

```text
Admin
Manager
```

## 27. Current User Endpoint

The endpoint:

```http
GET /api/auth/me
```

requires `[Authorize]`.

The response is constructed from claims including:

```text
NameIdentifier
FullName
Role
PhoneNumber
```

and returned as `UserProfileDto`.

## 28. Complete Authentication Flow

```text
                         +---------------------+
                         |       Client        |
                         +----------+----------+
                                    |
                       Register / Login
                                    |
                                    v
                         +---------------------+
                         |   AuthController    |
                         +----------+----------+
                                    |
                                    v
                         +---------------------+
                         |    AuthService      |
                         +----------+----------+
                                    |
                 +------------------+------------------+
                 |                  |                  |
                 v                  v                  v
              User             UserSession       RefreshToken
                 |                  |                  |
                 +------------------+------------------+
                                    |
                                    v
                              JWT Generation
                                    |
                                    v
                         Access + Refresh Token
                                    |
                                    v
                                  Client
```

## 29. Protected Request Flow

For a protected API request:

```text
HTTP Request
    |
    v
Authorization: Bearer <JWT>
    |
    v
JWT Signature / Issuer / Audience / Lifetime
    |
    v
OnTokenValidated
    |
    +--> Parse UserId
    +--> Parse SessionId
    +--> Check Active UserSession
    +--> Check SecurityStamp
    |
    v
Authenticated
    |
    v
Authorization Policy
    |
    v
Controller
```

## 30. Failure Scenarios

| Scenario | Current behavior |
|---|---|
| User not found during login | Login fails |
| Wrong password | Failed-attempt counter increases |
| Five failed attempts | 15-minute lockout is applied |
| Expired refresh token | Token is revoked, session is marked `Expired`, refresh is rejected |
| Rotated token replayed inside the grace window | Refresh is rejected; session stays active |
| Rotated token replayed after the grace window | Session and all its refresh tokens are revoked |
| Suspicious revoked-token reuse | Session and related refresh tokens are revoked |
| User deactivated or deleted | All sessions and refresh tokens revoked; SecurityStamp rotated |
| Refresh-token value mismatch | Associated session is revoked |
| Inactive session | Authentication/refresh is rejected |
| Idle-expired session | Session is marked `Expired` |
| Absolute-expired session | Session is marked `Expired` |
| Invalid JWT claims | Authentication is rejected |
| SecurityStamp mismatch | Authentication is rejected |
| Active-session limit exceeded | Oldest active sessions are revoked |

## 31. Security Model Summary

The current authentication model can be summarized as three complementary validation layers:

```text
                     Access Token
                          |
          +---------------+---------------+
          |               |               |
       Signature        SessionId     SecurityStamp
          |               |               |
          v               v               v
       JWT Valid       Session DB       User DB
                          |
                          v
                       Active?
                          |
                          v
                    Authorization
```

The result is a combination of cryptographic token validation and server-side authentication state.

## 32. Source Files

The main implementation files are:

```text
Online Store Application/
├── Controllers/
│   └── AuthController.cs
├── Services/
│   └── JwtTokenService.cs
├── Extensions/
│   └── UserSessionOptionsExtensions.cs
└── Program.cs

BusinessLogic/
├── Services/
│   ├── Interfaces/
│   │   └── IAuthService.cs
│   └── Implementations/
│       └── AuthService.cs
└── DTOs/
    └── Auth/

Application/
├── Entities/
│   ├── User.cs
│   ├── UserSession.cs
│   └── RefreshTokenEntity.cs
├── Interfaces/
│   └── Security/
│       ├── IJwtTokenService.cs
│       └── IPasswordHasher.cs
└── Options/
    ├── JwtOptions.cs
    └── UserSessionOptions.cs

DataLayer/
└── Security/
    └── BcryptPasswordHasher.cs
```

## 33. Source of Truth

The authentication behavior documented here is based on the current implementation in:

- `AuthController.cs`
- `AuthService.cs`
- `JwtTokenService.cs`
- `Program.cs`
- `UserSessionOptions.cs` / `UserSessionOptionsExtensions.cs`
- `UserService.cs` (deactivation and admin guards)
- `User.cs`
- `UserSession.cs`
- `RefreshTokenEntity.cs`
- `IPasswordHasher` / `BcryptPasswordHasher`

Any future authentication change should update this document as part of the same change.
