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

public sealed class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthService> _logger;

    private static readonly TimeSpan AbsoluteExpirationPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    private const int DefaultMaxSessions = 5;

    public AuthService(
        IUnitOfWork unitOfWork,
        IJwtTokenService jwtTokenService,
        IPasswordHasher passwordHasher,
        IMapper mapper,
        ILogger<AuthService> logger)
    {
        _unitOfWork = unitOfWork;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
        _mapper = mapper;
        _logger = logger;
    }

    // ================= REGISTER =================
    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            var exists = await _unitOfWork.Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber, ct);

            if (exists)
                throw new BusinessException("شماره تماس تکراری است.", "USER_PHONE_EXISTS");

            var user = new User
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                PhoneNumber = dto.PhoneNumber,
                Email = dto.Email,
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

            // NOTE: do NOT call LimitActiveSessionsAsync here (no active sessions yet)

            var session = CreateSession(user.UserId, new SessionMetadataDto(dto.DeviceId, dto.DeviceName, dto.IpAddress, dto.UserAgent));

            _logger.LogInformation("User {UserId} registered and creating initial session from IP {IP}", user.UserId, dto.IpAddress);

            await _unitOfWork.Repository<UserSession>().AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            // Create tokens inside same transaction so registration is atomic
            var authResult = await CreateAuthResultAsync(user, session, ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            return authResult;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    // ================= LOGIN =================
    public async Task<AuthResultDto?> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        const int maxFailed = 5;
        const int lockMinutes = 15;

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
            _logger.LogWarning("Login failed. User not found {Phone}", dto.PhoneNumber);
            return null;
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            return null;

        if (!_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= maxFailed)
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(lockMinutes);

            await _unitOfWork.SaveChangesAsync(ct);

            _logger.LogWarning("Invalid password for {UserId}", user.UserId);
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

            var session = CreateSession(user.UserId, new SessionMetadataDto(dto.DeviceId, dto.DeviceName, dto.IpAddress, dto.UserAgent));

            _logger.LogInformation("User {UserId} logged in from {IP}", user.UserId, dto.IpAddress);

            await _unitOfWork.Repository<UserSession>().AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            var result = await CreateAuthResultAsync(user, session, ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            return result;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    // ================= REFRESH =================
    public async Task<AuthResultDto?> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
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
            return null;

        // If expired by expiry date -> revoke and return null
        if (token.ExpiryDate < DateTime.UtcNow)
        {
            token.IsRevoked = true;
            token.RevokedAtUtc = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(ct);
            _logger.LogInformation("Expired refresh token used (UserId={UserId}, SessionId={SessionId})", token.UserId, token.SessionId);
            return null;
        }

        // Refresh token reuse detection:
        if (token.IsRevoked)
        {
            if (token.ReplacedByTokenId == null)
            {
                // token already revoked and not rotated -> possible reuse attack
                _logger.LogWarning("Refresh token reuse detected for UserId={UserId}, SessionId={SessionId}", token.UserId, token.SessionId);
                if (token.Session != null)
                {
                    // call without starting a nested transaction (caller may already be in a transaction)
                    await RevokeSessionAndTokensAsync(token.Session, "Refresh token reuse detected", ct, useTransaction: false);
                }
            }
            else
            {
                // Token was revoked due to normal rotation (ReplacedByTokenId != null).
                _logger.LogInformation("Stale refresh token presented after normal rotation (UserId={UserId}, SessionId={SessionId})", token.UserId, token.SessionId);
            }
            return null;
        }

        // Verify token value (hash)
        if (!_passwordHasher.Verify(refreshToken, token.TokenHash))
        {
            // signature mismatch -> treat as suspicious and revoke session
            _logger.LogWarning("Refresh token signature mismatch for UserId={UserId}, SessionId={SessionId}", token.UserId, token.SessionId);
            if (token.Session != null)
            {
                await RevokeSessionAndTokensAsync(token.Session, "Refresh token signature mismatch", ct, useTransaction: false);
            }
            return null;
        }

        if (token.Session.Status != UserSession.SessionStatus.Active)
            return null;

        if (token.Session.IsIdleExpired(IdleTimeout) || token.Session.IsAbsoluteExpired())
        {
            token.Session.Status = UserSession.SessionStatus.Expired;
            await _unitOfWork.SaveChangesAsync(ct);
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
                CreatedAt = DateTime.UtcNow,
                ExpiryDate = DateTime.UtcNow.AddDays(7),
                AbsoluteExpiry = token.AbsoluteExpiry,
                FamilyCreatedAt = token.FamilyCreatedAt, // keep family timestamp
                IsRevoked = false
            };

            await _unitOfWork.Repository<RefreshTokenEntity>().AddAsync(newEntity, ct);
            await _unitOfWork.SaveChangesAsync(ct); // obtain newEntity.Id

            // Mark old token as rotated (revoked) and point to the replacement
            token.IsRevoked = true;
            token.RevokedAtUtc = DateTime.UtcNow;
            token.ReplacedByTokenId = newEntity.Id;

            token.Session.LastActivityUtc = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync(ct);

            var accessToken = GenerateAccessToken(token.User, token.Session.Id);

            await _unitOfWork.CommitTransactionAsync(ct);

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

            return null;
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }
    }

    // ================= LOGOUT =================
    public async Task LogoutSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await _unitOfWork.Repository<UserSession>()
            .GetByIdAsync(sessionId, ct);

        if (session is null) return;

        // if already not active, nothing to do
        if (session.Status != UserSession.SessionStatus.Active)
            return;

        // revoke session and all related refresh tokens
        await RevokeSessionAndTokensAsync(session, "User logout", ct);
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
        var accessToken = GenerateAccessToken(user, session.Id);

        var refreshToken = await CreateRefreshTokenAsync(
            user.UserId,
            session.Id,
            session.CreatedAtUtc,
            ct);

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
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow,
            AbsoluteExpiryUtc = DateTime.UtcNow.Add(AbsoluteExpirationPeriod),
            Status = UserSession.SessionStatus.Active
        };
    }

    private string GenerateAccessToken(User user, Guid sessionId)
    {
        var role = user.Employee?.EmployeeType?.TypeName;

        if (string.IsNullOrEmpty(role))
        {
            role = user.UserType == UserType.Employee
                ? "Employee"
                : "Customer";
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("SessionId", sessionId.ToString()),
            new(ClaimTypes.Role, role),
            new("FullName", user.FullName ?? ""),
            new("PhoneNumber", user.PhoneNumber),
            new("SecurityStamp", user.SecurityStamp)
        };

        return _jwtTokenService.GenerateToken(claims);
    }

    private async Task<string> CreateRefreshTokenAsync(int userId, Guid sessionId, DateTime createdAt, CancellationToken ct)
    {
        var token = _jwtTokenService.GenerateRefreshToken();

        var entity = new RefreshTokenEntity
        {
            UserId = userId,
            SessionId = sessionId,
            TokenHash = _passwordHasher.Hash(token),
            TokenIdentifier = ComputeSha256Hash(token),
            FamilyCreatedAt = createdAt,
            AbsoluteExpiry = createdAt.Add(AbsoluteExpirationPeriod),
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _unitOfWork.Repository<RefreshTokenEntity>().AddAsync(entity, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return token;
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
            session.RevokedAtUtc = DateTime.UtcNow;

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
                    t.RevokedAtUtc = DateTime.UtcNow;
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