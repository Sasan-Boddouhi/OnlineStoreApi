using Application.Entities;
using Application.Exceptions;
using Application.Helper;
using Application.Interfaces;
using Application.Interfaces.Security;
using AutoMapper;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Auth;
using BusinessLogic.Specifications.Sessions;
using BusinessLogic.Specifications.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace BusinessLogic.Services.Implementations;

public sealed class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IMapper _mapper;
    private readonly ILogger<AuthService> _logger;

    private static readonly TimeSpan AbsoluteExpirationPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

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

    #region Register

    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken cancellationToken = default)
    {
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var exists = await _unitOfWork
                .Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber, cancellationToken);

            if (exists)
                throw new BusinessException("شماره تماس تکراری است.");

            var user = new User
            {
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                PhoneNumber = dto.PhoneNumber,
                Email = dto.Email,
                DateOfBirth = PersianDateHelper.ToGregorian(dto.DateOfBirth),
                PasswordHash = _passwordHasher.Hash(dto.Password),
                UserType = UserType.Customer,
                IsActive = true,
                SecurityStamp = Guid.NewGuid().ToString()
            };

            await _unitOfWork.Repository<User>().AddAsync(user, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                DeviceId = dto.DeviceId ?? "Register",
                DeviceName = dto.DeviceName,
                IpAddress = dto.IpAddress,
                UserAgent = dto.UserAgent,
                CreatedAtUtc = DateTime.UtcNow,
                LastActivityUtc = DateTime.UtcNow,
                AbsoluteExpiryUtc = DateTime.UtcNow.Add(AbsoluteExpirationPeriod),
                Status = UserSession.SessionStatus.Active
            };

            await _unitOfWork.Repository<UserSession>().AddAsync(session, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("User registered successfully with ID: {UserId}", user.UserId);
            return await CreateAuthResultAsync(user.UserId, session.Id, session.CreatedAtUtc, cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    #endregion

    #region Login

    public async Task<AuthResultDto?> LoginAsync(LoginDto dto, CancellationToken cancellationToken = default)
    {
        const int maxFailedAttempts = 5;
        const int lockoutMinutes = 15;

        _logger.LogInformation("Login attempt for phone: {PhoneNumber}", dto.PhoneNumber);

        try
        {
            var spec = new UserByPhoneSpecification(dto.PhoneNumber, includeInactive: false);
            var user = await _unitOfWork
                .Repository<User>()
                .FirstOrDefaultAsync(spec, cancellationToken);

            if (user is null)
            {
                _logger.LogWarning("Login failed: user not found or inactive for phone: {PhoneNumber}", dto.PhoneNumber);
                return null;
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            {
                _logger.LogWarning("Login failed: account locked for user {UserId} until {LockoutEnd}", user.UserId, user.LockoutEnd);
                return null;
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd <= DateTime.UtcNow)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = null;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            bool passwordValid = _passwordHasher.Verify(dto.Password, user.PasswordHash);

            if (!passwordValid)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= maxFailedAttempts)
                {
                    user.LockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                    _logger.LogWarning("User {UserId} locked out due to too many failed attempts", user.UserId);
                }
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return null;
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var session = new UserSession
            {
                Id = Guid.NewGuid(),
                UserId = user.UserId,
                DeviceId = dto.DeviceId,
                DeviceName = dto.DeviceName,
                IpAddress = dto.IpAddress,
                UserAgent = dto.UserAgent,
                CreatedAtUtc = DateTime.UtcNow,
                LastActivityUtc = DateTime.UtcNow,
                AbsoluteExpiryUtc = DateTime.UtcNow.Add(AbsoluteExpirationPeriod),
                Status = UserSession.SessionStatus.Active
            };

            await _unitOfWork.Repository<UserSession>().AddAsync(session, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("User logged in successfully: {UserId}", user.UserId);
            return await CreateAuthResultAsync(user.UserId, session.Id, session.CreatedAtUtc, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for phone: {PhoneNumber}", dto.PhoneNumber);
            throw;
        }
    }

    #endregion

    #region Refresh

    public async Task<AuthResultDto?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var identifier = ComputeSha256Hash(refreshToken);
        var spec = new RefreshTokenByIdentifierSpecification(identifier);
        var token = await _unitOfWork
            .Repository<RefreshTokenEntity>()
            .FirstOrDefaultAsync(spec, cancellationToken);

        if (token is null)
        {
            _logger.LogWarning("Refresh token not found for identifier");
            return null;
        }

        if (!_passwordHasher.Verify(refreshToken, token.TokenHash))
        {
            _logger.LogWarning("Refresh token hash verification failed");
            return null;
        }

        if (token.Session.Status != UserSession.SessionStatus.Active || token.Session.IsAbsoluteExpired())
        {
            _logger.LogInformation("Session not active or absolute expired");
            return null;
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // بررسی انقضای idle قبل از هر چیز
            if (token.Session.IsIdleExpired(IdleTimeout))
            {
                token.Session.Status = UserSession.SessionStatus.Expired;
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                _logger.LogInformation("Session expired due to idle timeout");
                return null;
            }

            // بررسی مجدد وضعیت توکن (با همان token که تحت نظر است)
            if (token.IsRevoked || token.ExpiryDate <= DateTime.UtcNow)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogInformation("Token already revoked or expired");
                return null;
            }

            // ابطال توکن فعلی
            token.IsRevoked = true;
            token.RevokedAtUtc = DateTime.UtcNow;

            // به‌روزرسانی آخرین فعالیت نشست
            token.Session.LastActivityUtc = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // ایجاد توکن جدید (خودش در تراکنش ذخیره می‌کند)
            var result = await CreateAuthResultAsync(
                token.UserId,
                token.SessionId,
                token.Session.CreatedAtUtc,
                cancellationToken);

            await _unitOfWork.CommitTransactionAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            _logger.LogWarning(ex, "Concurrency conflict during refresh token for user {UserId}", token.UserId);
            return null;
        }
    }

    #endregion

    #region Logout

    public async Task LogoutSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _unitOfWork
            .Repository<UserSession>()
            .GetByIdAsync(sessionId, cancellationToken);

        if (session is null)
            return;

        session.Status = UserSession.SessionStatus.Revoked;
        session.RevokedAtUtc = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task LogoutAllAsync(int userId, CancellationToken cancellationToken = default)
    {
        var spec = new ActiveUserSessionsSpecification(userId);
        var sessions = await _unitOfWork
            .Repository<UserSession>()
            .ListAsync(spec, cancellationToken);

        foreach (var session in sessions)
        {
            session.Status = UserSession.SessionStatus.Revoked;
            session.RevokedAtUtc = DateTime.UtcNow;
        }
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Core Auth Logic

    private async Task<AuthResultDto> CreateAuthResultAsync(
        int userId,
        Guid sessionId,
        DateTime sessionCreatedAtUtc,
        CancellationToken cancellationToken)
    {
        var (user, accessToken) = await GenerateAccessTokenAsync(userId, cancellationToken);
        var refreshToken = await CreateRefreshTokenAsync(userId, sessionId, sessionCreatedAtUtc, cancellationToken);

        return new AuthResultDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = _mapper.Map<UserDto>(user)
        };
    }

    private async Task<(User user, string accessToken)> GenerateAccessTokenAsync(int userId, CancellationToken cancellationToken)
    {
        var spec = new UserWithRoleSpecification(userId);
        var user = await _unitOfWork
            .Repository<User>()
            .FirstOrDefaultAsync(spec, cancellationToken);

        if (user is null)
            throw new BusinessException("User not found.");

        string role = user.Employee?.EmployeeType?.TypeName ??
                      (user.UserType == UserType.Customer ? "Customer" : "NoRole");

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("FullName", user.FullName),
            new Claim("PhoneNumber", user.PhoneNumber),
            new Claim("SecurityStamp", user.SecurityStamp)
        };

        var accessToken = _jwtTokenService.GenerateToken(claims);
        return (user, accessToken);
    }

    private async Task<string> CreateRefreshTokenAsync(
        int userId,
        Guid sessionId,
        DateTime sessionCreatedAt,
        CancellationToken cancellationToken)
    {
        var refreshToken = _jwtTokenService.GenerateRefreshToken();
        var entity = new RefreshTokenEntity
        {
            UserId = userId,
            SessionId = sessionId,
            TokenHash = _passwordHasher.Hash(refreshToken),
            TokenIdentifier = ComputeSha256Hash(refreshToken),
            AbsoluteExpiry = sessionCreatedAt.Add(AbsoluteExpirationPeriod),
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _unitOfWork.Repository<RefreshTokenEntity>().AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return refreshToken;
    }

    private static string ComputeSha256Hash(string rawData)
    {
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToBase64String(bytes);
    }

    #endregion
}