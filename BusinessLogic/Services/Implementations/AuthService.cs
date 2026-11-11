using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Helper;
using Application.Interfaces;
using Application.Interfaces.Security;
using AutoMapper;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Application.Diagnostics;
using Application.Options;
using Microsoft.Extensions.Options;

public sealed class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly UserSessionOptions _sessionOptions;

    private static readonly ActivitySource ActivitySource = new("OnlineStore.Auth");

    private const int DefaultMaxSessions = 5;

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public AuthService(
        IUnitOfWork unitOfWork,
        IJwtTokenService jwtTokenService,
        IPasswordHasher passwordHasher,
        IMapper mapper,
        ILogger<AuthService> logger,
        TimeProvider timeProvider,
        IOptions<UserSessionOptions> sessionOptions)
    {
        _unitOfWork = unitOfWork;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
        _mapper = mapper;
        _logger = logger;
        _timeProvider = timeProvider;
        _sessionOptions = sessionOptions.Value;
    }

    // ================= REGISTER =================
    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, ClientRequestInfo client, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("RegisterUser", ActivityKind.Internal);

        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            var exists = await _unitOfWork.Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber, ct);

            if (exists)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Phone already exists");
                // Batch 11b: موبایل تکراری مانند ایمیل تکراری تعارض است (409)، نه ورودی نامعتبر.
                throw new ConflictException("شماره تماس تکراری است.", "USER_PHONE_EXISTS");
            }

            // Batch 11 (F-41): ایمیل در دیتابیس یکتاست؛ تکرار آن پیش‌تر به خطای ذخیره‌سازی می‌رسید.
            var email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            if (email is not null
                && await _unitOfWork.Repository<User>().AnyAsync(x => x.Email == email, ct))
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Email already exists");
                throw new ConflictException("این ایمیل قبلاً ثبت شده است.", "USER_EMAIL_EXISTS");
            }

            var user = new User
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                PhoneNumber = dto.PhoneNumber,
                Email = email,
                DateOfBirth = !string.IsNullOrWhiteSpace(dto.DateOfBirth)
                    ? PersianDateHelper.ToGregorian(dto.DateOfBirth)
                    : null,
                PasswordHash = _passwordHasher.Hash(dto.Password),
                UserType = UserType.Customer,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            await _unitOfWork.Repository<User>().AddAsync(user, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            // M7.11: Auto-provision Customer profile for newly registered users
            var customerRepo = _unitOfWork.Repository<Customer>();
            if (customerRepo != null)
            {
                var customer = new Customer { UserId = user.UserId };
                await customerRepo.AddAsync(customer, ct);
                await _unitOfWork.SaveChangesAsync(ct);
            }

            // NOTE: do NOT call LimitActiveSessionsAsync here (no active sessions yet)

            var session = CreateSession(user.UserId, new SessionMetadataDto(dto.DeviceId, dto.DeviceName, client.IpAddress, client.UserAgent));

            _logger.LogInformation("User {UserId} registered and creating initial session", user.UserId);

            await _unitOfWork.Repository<UserSession>().AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            // Create tokens inside same transaction so registration is atomic
            var authResult = await CreateAuthResultAsync(user, session, ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            OnlineStoreMetrics.UserRegistrations.Add(1);

            activity?.SetTag("user.id", user.UserId);
            activity?.SetTag("session.id", session.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return authResult;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ================= LOGIN =================
    public async Task<AuthResultDto?> LoginAsync(LoginDto dto, ClientRequestInfo client, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("Login", ActivityKind.Internal);

        const int maxFailed = 5;
        const int lockMinutes = 15;

        OnlineStoreMetrics.LoginAttempts.Add(1);

        var user = await _unitOfWork.Repository<User>()
            .FirstOrDefaultAsync(
                new Spec<User>()
                    .Where(u => u.PhoneNumber == dto.PhoneNumber)
                    .Where(u => u.IsActive)
                    .Include(u => u.Employee!)
                    .Include(u => u.Employee!.EmployeeType)
                    .AsTracking(),
                ct);

        if (user is null)
        {
            OnlineStoreMetrics.LoginFailure.Add(
                1,
                new KeyValuePair<string, object?>("reason", "user_not_found"));

            _logger.LogWarning("Login failed. User not found");
            activity?.SetStatus(ActivityStatusCode.Error, "User not found");
            return null;
        }

        activity?.SetTag("user.id", user.UserId);

        if (user.LockoutEnd.HasValue)
        {
            if (user.LockoutEnd.Value > UtcNow)
            {
                OnlineStoreMetrics.LoginFailure.Add(
                    1,
                    new KeyValuePair<string, object?>("reason", "account_locked"));

                activity?.SetStatus(ActivityStatusCode.Error, "Account locked");
                throw new LockedOutException(user.LockoutEnd.Value);
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await _unitOfWork.SaveChangesAsync(ct);
        }

        if (!_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= maxFailed)
                user.LockoutEnd = UtcNow.AddMinutes(lockMinutes);

            await _unitOfWork.SaveChangesAsync(ct);

            OnlineStoreMetrics.LoginFailure.Add(
                1,
                new KeyValuePair<string, object?>("reason", "invalid_password"));

            _logger.LogWarning("Invalid password for {UserId}", user.UserId);
            activity?.SetTag("auth.failed_attempts", user.FailedLoginAttempts);
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid password");

            if (user.LockoutEnd.HasValue)
                throw new LockedOutException(user.LockoutEnd.Value);

            return null;
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;

        await _unitOfWork.SaveChangesAsync(ct);

        // Begin transaction to make session + token creation atomic
        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            // enforce max active sessions per user (revokes oldest sessions and their tokens)
            await LimitActiveSessionsAsync(user.UserId, DefaultMaxSessions, ct);

            var session = CreateSession(user.UserId, new SessionMetadataDto(dto.DeviceId, dto.DeviceName, client.IpAddress, client.UserAgent));

            _logger.LogInformation("User {UserId} logged in", user.UserId);

            await _unitOfWork.Repository<UserSession>().AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            var result = await CreateAuthResultAsync(user, session, ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            activity?.SetTag("session.id", session.Id);
            activity?.SetStatus(ActivityStatusCode.Ok);

            OnlineStoreMetrics.LoginSuccess.Add(1);

            return result;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ================= REFRESH =================
    public async Task<AuthResultDto?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("RefreshToken", ActivityKind.Internal);

        var identifier = ComputeSha256Hash(refreshToken);

        var token = await _unitOfWork.Repository<RefreshTokenEntity>()
            .FirstOrDefaultAsync(
                new Spec<RefreshTokenEntity>()
                    .Where(t => t.TokenIdentifier == identifier)
                    .Include(t => t.Session)
                    .Include(t => t.User)
                    .Include(t => t.User.Employee!)
                    .Include(t => t.User.Employee!.EmployeeType)
                    .AsTracking(),
                ct);

        if (token is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Token not found");
            return null;
        }

        // M7.11: Validate user active status. Deactivated/suspended users must not refresh tokens.
        if (token.User is null || !token.User.IsActive)
        {
            if (token.Session != null)
            {
                await RevokeSessionAndTokensAsync(token.Session, "User account is inactive", ct, useTransaction: false);
            }
            activity?.SetStatus(ActivityStatusCode.Error, "User inactive");
            throw new UnauthorizedException("حساب کاربری غیرفعال است.", "USER_INACTIVE");
        }

        activity?.SetTag("user.id", token.UserId);
        activity?.SetTag("session.id", token.SessionId);

        // Batch 11 (F-19): تشخیص استفادهٔ دوباره پیش از بررسی انقضا می‌آید تا توکن دزدیده‌شده
        // حتی اگر منقضی هم شده باشد نشست را باطل کند.
        if (token.IsRevoked)
        {
            if (token.Session is null || token.Session.Status != UserSession.SessionStatus.Active)
            {
                // نشست قبلاً بسته شده (خروج، انقضا، باطل‌سازی)؛ چیزی برای محافظت نمانده است.
                activity?.SetStatus(ActivityStatusCode.Error, "Session inactive");
                return null;
            }

            var rotatedAt = token.RevokedAtUtc ?? DateTime.MinValue;
            var insideGraceWindow = token.ReplacedByTokenId != null
                && UtcNow - rotatedAt <= _sessionOptions.RefreshReuseGracePeriod;

            if (insideGraceWindow)
            {
                // دو درخواست هم‌زمان (مثلاً دو تب) با یک توکن: درخواست دوم فقط رد می‌شود و نشست می‌ماند.
                _logger.LogInformation("Rotated refresh token presented inside the grace window (UserId={UserId}, SessionId={SessionId})", token.UserId, token.SessionId);
                activity?.SetStatus(ActivityStatusCode.Error, "Stale refresh token (grace window)");
                return null;
            }

            // توکن چرخیده پس از بازهٔ تحمل، یا توکن باطل‌شده: نشانهٔ سرقت. کل خانواده (نشست و همهٔ توکن‌هایش) باطل می‌شود.
            OnlineStoreMetrics.RefreshTokenReuse.Add(1);
            _logger.LogWarning("Refresh token reuse detected for UserId={UserId}, SessionId={SessionId}", token.UserId, token.SessionId);
            // call without starting a nested transaction (caller may already be in a transaction)
            await RevokeSessionAndTokensAsync(token.Session, "Refresh token reuse detected", ct, useTransaction: false);

            activity?.SetStatus(ActivityStatusCode.Error, "Refresh token reuse detected");
            return null;
        }

        // If expired by expiry date -> revoke and return null
        if (token.ExpiryDate < UtcNow)
        {
            token.IsRevoked = true;
            token.RevokedAtUtc = UtcNow;

            // عمر refresh token همان بازهٔ بی‌کاری (با سقف مطلق) است و هر نشست فقط یک توکن زنده دارد؛
            // پس انقضای توکن یعنی نشست هم دیگر قابل تمدید نیست.
            if (token.Session is { Status: UserSession.SessionStatus.Active })
                token.Session.Status = UserSession.SessionStatus.Expired;

            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Expired refresh token used (UserId={UserId}, SessionId={SessionId})", token.UserId, token.SessionId);
            activity?.SetStatus(ActivityStatusCode.Error, "Refresh token expired");
            return null;
        }

        // Verify token value (hash)
        if (!_passwordHasher.Verify(refreshToken, token.TokenHash))
        {
            // signature mismatch -> treat as suspicious and revoke session
            _logger.LogWarning("Refresh token signature mismatch for UserId={UserId}, SessionId={SessionId}", token.UserId, token.SessionId);
            activity?.SetStatus(ActivityStatusCode.Error, "Refresh token signature mismatch");
            if (token.Session != null)
            {
                await RevokeSessionAndTokensAsync(token.Session, "Refresh token signature mismatch", ct, useTransaction: false);
            }
            return null;
        }

        if (token.Session.Status != UserSession.SessionStatus.Active)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Session inactive");
            return null;
        }

        // Batch 11 (F-13): بی‌کاری از آخرین ورود یا refresh موفق سنجیده می‌شود، نه از آخرین درخواست.
        if (token.Session.IsIdleExpired(_sessionOptions.IdleTimeout, UtcNow) || token.Session.IsAbsoluteExpired(UtcNow))
        {
            token.Session.Status = UserSession.SessionStatus.Expired;
            await _unitOfWork.SaveChangesAsync(ct);
            activity?.SetStatus(ActivityStatusCode.Error, "Session expired");
            return null;
        }

        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            // Create new refresh token entity (rotation) and link old -> new atomically
            var newRefreshPlain = _jwtTokenService.GenerateRefreshToken();

            var newEntity = new RefreshTokenEntity
            {
                UserId = token.UserId,
                SessionId = token.SessionId,
                TokenHash = _passwordHasher.Hash(newRefreshPlain),
                TokenIdentifier = ComputeSha256Hash(newRefreshPlain),
                CreatedAt = UtcNow,
                ExpiryDate = RefreshTokenExpiry(token.Session),
                AbsoluteExpiry = token.AbsoluteExpiry,
                FamilyCreatedAt = token.FamilyCreatedAt, // keep family timestamp
                IsRevoked = false
            };

            await _unitOfWork.Repository<RefreshTokenEntity>().AddAsync(newEntity, ct);
            await _unitOfWork.SaveChangesAsync(ct); // obtain newEntity.Id

            // Mark old token as rotated (revoked) and point to the replacement
            token.IsRevoked = true;
            token.RevokedAtUtc = UtcNow;
            token.ReplacedByTokenId = newEntity.Id;

            token.Session.LastActivityUtc = UtcNow;

            await _unitOfWork.SaveChangesAsync(ct);

            var accessToken = await GenerateAccessTokenAsync(token.User, token.Session.Id, ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            activity?.SetStatus(ActivityStatusCode.Ok);

            return new AuthResultDto
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshPlain,
                User = _mapper.Map<UserDto>(token.User)
            };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(ct);

            _logger.LogWarning(
                ex,
                "Refresh token concurrency conflict for UserId {UserId}, TokenId {TokenId}",
                token?.UserId,
                token?.Id);

            activity?.SetStatus(ActivityStatusCode.Error, "Refresh token concurrency conflict");
            return null;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    // ================= LOGOUT =================
    public async Task LogoutSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        using var activity = ActivitySource.StartActivity("LogoutSession", ActivityKind.Internal);
        activity?.SetTag("session.id", sessionId);

        var session = await _unitOfWork.Repository<UserSession>()
            .GetByIdAsync(sessionId, ct);

        if (session is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Session not found");
            return;
        }

        if (session.Status != UserSession.SessionStatus.Active)
        {
            activity?.SetStatus(ActivityStatusCode.Ok, "Already revoked");
            return;
        }

        activity?.SetTag("user.id", session.UserId);

        await RevokeSessionAndTokensAsync(session, "User logout", ct);
        activity?.SetStatus(ActivityStatusCode.Ok);
    }

    public async Task LogoutAllAsync(int userId, CancellationToken ct = default)
    {
        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            var sessions = await _unitOfWork.Repository<UserSession>()
                .ListAsync(
                    new Spec<UserSession>()
                        .Where(x => x.UserId == userId && x.Status == UserSession.SessionStatus.Active)
                        .AsTracking(),
                    ct);

            foreach (var session in sessions)
            {
                await RevokeSessionAndTokensAsync(
                    session,
                    "Logout all",
                    ct,
                    useTransaction: false);
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    // ================= CORE =================
    private async Task<AuthResultDto> CreateAuthResultAsync(User user, UserSession session, CancellationToken ct)
    {
        var accessToken = await GenerateAccessTokenAsync(user, session.Id, ct);

        var refreshToken = await CreateRefreshTokenAsync(session, ct);

        return new AuthResultDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = _mapper.Map<UserDto>(user)
        };
    }

    private UserSession CreateSession(int userId, SessionMetadataDto dto)
    {
        return new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DeviceId = string.IsNullOrWhiteSpace(dto.DeviceId) ? Guid.NewGuid().ToString() : dto.DeviceId,
            DeviceName = dto.DeviceName,
            IpAddress = dto.IpAddress,
            UserAgent = dto.UserAgent,
            CreatedAtUtc = UtcNow,
            LastActivityUtc = UtcNow,
            AbsoluteExpiryUtc = UtcNow.Add(_sessionOptions.AbsoluteLifetime),
            Status = UserSession.SessionStatus.Active
        };
    }

    private const string DefaultCustomerRole = "Customer";

    /// <summary>
    /// Phase 1: نقش‌ها دیگر از EmployeeType.TypeName خوانده نمی‌شوند،
    /// بلکه از جدول UserRole -> Role می‌آیند. یک claim به‌ازای هر نقش صادر می‌شود.
    /// موتور JWT اجازه می‌دهد چند claim با نوع Role تکرار شوند.
    /// </summary>
    private async Task<string> GenerateAccessTokenAsync(User user, Guid sessionId, CancellationToken ct)
    {
        var roles = await GetRoleCodesAsync(user.UserId, ct);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("SessionId", sessionId.ToString()),
            new("FullName", user.FullName ?? ""),
            new("PhoneNumber", user.PhoneNumber),
            new("SecurityStamp", user.SecurityStamp)
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        return _jwtTokenService.GenerateToken(claims);
    }

    private async Task<IReadOnlyList<string>> GetRoleCodesAsync(int userId, CancellationToken ct)
    {
        var userRoles = await _unitOfWork.Repository<UserRole>()
            .ListAsync(
                new Spec<UserRole>()
                    .Where(ur => ur.UserId == userId)
                    .Include(ur => ur.Role),
                ct);

        var codes = userRoles
            .Where(ur => ur.Role != null && ur.Role.IsActive)
            .Select(ur => ur.Role!.Code)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
            return new[] { DefaultCustomerRole };

        // ترتیب عمدی: Admin/Manager اول می‌آیند تا EnsureAdmin (چک رشته‌ای قدیمی)
        // برای کاربران چند-نقشی بی‌صدا نشکند (سازگاری D7).
        return codes
            .OrderBy(RoleSortRank)
            .ThenBy(code => code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int RoleSortRank(string code) => code switch
    {
        "Admin" => 0,
        "Manager" => 1,
        "SuperAdmin" => 2,
        _ => 3
    };

    private async Task<string> CreateRefreshTokenAsync(UserSession session, CancellationToken ct)
    {
        var token = _jwtTokenService.GenerateRefreshToken();

        var entity = new RefreshTokenEntity
        {
            UserId = session.UserId,
            SessionId = session.Id,
            TokenHash = _passwordHasher.Hash(token),
            TokenIdentifier = ComputeSha256Hash(token),
            FamilyCreatedAt = session.CreatedAtUtc,
            AbsoluteExpiry = session.AbsoluteExpiryUtc,
            ExpiryDate = RefreshTokenExpiry(session),
            CreatedAt = UtcNow,
            IsRevoked = false
        };

        await _unitOfWork.Repository<RefreshTokenEntity>().AddAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return token;
    }

    /// <summary>
    /// Batch 11 (F-13): عمر refresh token برابر بازهٔ بی‌کاری است و از سقف مطلق نشست جلوتر نمی‌رود؛
    /// پس پس از بی‌کاری یا پایان عمر نشست، توکن هم دیگر پذیرفته نمی‌شود.
    /// </summary>
    private DateTime RefreshTokenExpiry(UserSession session)
    {
        var idleExpiry = UtcNow.Add(_sessionOptions.IdleTimeout);
        return idleExpiry < session.AbsoluteExpiryUtc ? idleExpiry : session.AbsoluteExpiryUtc;
    }

    private static string ComputeSha256Hash(string raw)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
    }

    private async Task RevokeSessionAndTokensAsync(UserSession session, string reason, CancellationToken ct = default, bool useTransaction = true)
    {
        if (session is null) return;

        if (session.Status != UserSession.SessionStatus.Active)
            return;

        if (useTransaction)
            await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            // revoke session
            session.Status = UserSession.SessionStatus.Revoked;
            session.RevokedAtUtc = UtcNow;

            // revoke all refresh tokens for this session
            var tokens = await _unitOfWork.Repository<RefreshTokenEntity>()
                .ListAsync(
                    new Spec<RefreshTokenEntity>()
                        .Where(t => t.SessionId == session.Id)
                        .AsTracking(),
                    ct);

            foreach (var t in tokens)
            {
                if (!t.IsRevoked)
                {
                    t.IsRevoked = true;
                    t.RevokedAtUtc = UtcNow;
                }
            }

            await _unitOfWork.SaveChangesAsync(ct);

            if (useTransaction)
                await _unitOfWork.CommitTransactionAsync(ct);

            // security log (do not log tokens)
            _logger.LogWarning("Session {SessionId} revoked due to suspicious activity: {Reason}. UserId={UserId}", session.Id, reason, session.UserId);
        }
        catch
        {
            if (useTransaction)
                await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    private async Task LimitActiveSessionsAsync(int userId, int maxSessions, CancellationToken ct)
    {
        var sessions = await _unitOfWork.Repository<UserSession>()
            .ListAsync(
                new Spec<UserSession>()
                    .Where(x => x.UserId == userId && x.Status == UserSession.SessionStatus.Active)
                    .OrderBy(s => s.CreatedAtUtc)
                    .AsTracking(),
                ct);

        var removeCount = sessions.Count - maxSessions + 1;

        if (removeCount <= 0)
            return;

        foreach (var s in sessions.Take(removeCount))
        {
            // revoke session and its refresh tokens (do not start a nested transaction here)
            await RevokeSessionAndTokensAsync(s, "Session limit exceeded", ct, useTransaction: false);
        }
    }
}