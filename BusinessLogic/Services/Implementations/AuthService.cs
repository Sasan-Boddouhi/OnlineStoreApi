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

    // مدت زمان اعتبار مطلق هر خانواده توکن (۳۰ روز)
    private static readonly TimeSpan AbsoluteExpirationPeriod = TimeSpan.FromDays(30);

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

    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto)
    {
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            var exists = await _unitOfWork
                .Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber);

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
                IsActive = true
            };

            await _unitOfWork.Repository<User>().AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

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

            await _unitOfWork.Repository<UserSession>().AddAsync(session);
            await _unitOfWork.SaveChangesAsync();

            await _unitOfWork.CommitTransactionAsync();

            // ایجاد خانواده جدید با زمان ایجاد هم‌اکنون
            return await CreateAuthResultAsync(user.UserId, session.Id, session.CreatedAtUtc);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }   

    #endregion

    #region Login

    public async Task<AuthResultDto?> LoginAsync(LoginDto dto)
    {
        const int maxFailedAttempts = 5;
        const int lockoutMinutes = 15;

        _logger.LogInformation("Login attempt for phone: {PhoneNumber}", dto.PhoneNumber);

        try
        {
            var spec = new UserByPhoneSpecification(dto.PhoneNumber, includeInactive: false);
            var user = await _unitOfWork
                .Repository<User>()
                .FirstOrDefaultAsync(spec);

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
                await _unitOfWork.SaveChangesAsync();
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
                await _unitOfWork.SaveChangesAsync();
                return null;
            }

            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;

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
                Status = UserSession.SessionStatus.Active
            };
            session.AbsoluteExpiryUtc = session.CreatedAtUtc.Add(AbsoluteExpirationPeriod);

            await _unitOfWork.Repository<UserSession>().AddAsync(session);
            await _unitOfWork.SaveChangesAsync();


            _logger.LogInformation("User logged in successfully: {UserId}", user.UserId);
            return await CreateAuthResultAsync(user.UserId, session.Id, session.CreatedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for phone: {PhoneNumber}", dto.PhoneNumber);
            throw;
        }
    }

    #endregion

    #region Refresh

    public async Task<AuthResultDto?> RefreshTokenAsync(string refreshToken)
    {
        var identifier = ComputeSha256Hash(refreshToken);

        var spec = new RefreshTokenByIdentifierSpecification(identifier);

        var token = await _unitOfWork
            .Repository<RefreshTokenEntity>()
            .FirstOrDefaultAsync(spec);

        if (token is null)
            return null;

        if (!_passwordHasher.Verify(refreshToken, token.TokenHash))
            return null;

        if (token.Session.Status != UserSession.SessionStatus.Active || token.Session.IsAbsoluteExpired())
            return null;

        await _unitOfWork.BeginTransactionAsync();

        try
        {
            // بارگذاری مجدد برای concurrency safe
            var freshToken = await _unitOfWork
                .Repository<RefreshTokenEntity>()
                .GetByIdAsync(token.Id);

            if (freshToken.Session.IsIdleExpired(TimeSpan.FromMinutes(30)))
            {
                freshToken.Session.Status = UserSession.SessionStatus.Expired;
                await _unitOfWork.SaveChangesAsync();
                await _unitOfWork.RollbackTransactionAsync();
                return null;
            }

            if (freshToken is null || freshToken.IsRevoked || freshToken.ExpiryDate <= DateTime.UtcNow)
            {
                await _unitOfWork.RollbackTransactionAsync();
                return null;
            }

            // ری‌ووک توکن فعلی
            freshToken.IsRevoked = true;
            freshToken.RevokedAtUtc = DateTime.UtcNow;


            freshToken.Session.LastActivityUtc = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            // ایجاد AuthResult که خودش توکن جدید می‌سازد
            var result = await CreateAuthResultAsync(
                freshToken.UserId,
                freshToken.SessionId,
                freshToken.Session.CreatedAtUtc);

            await _unitOfWork.CommitTransactionAsync();

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _unitOfWork.RollbackTransactionAsync();
            return null;
        }
    }

    #endregion

    #region Logout

    public async Task LogoutSessionAsync(Guid sessionId)
    {
        var session = await _unitOfWork
            .Repository<UserSession>()
            .GetByIdAsync(sessionId);

        if (session is null)
            return;

        await _unitOfWork.BeginTransactionAsync();

        try
        {
            session.Status = UserSession.SessionStatus.Revoked;
            session.RevokedAtUtc = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            await _unitOfWork.CommitTransactionAsync();
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task LogoutAllAsync(int userId)
    {
        var sessions = await _unitOfWork
            .Repository<UserSession>()
            .ListAsync(new ActiveUserSessionsSpecification(userId));

        foreach (var session in sessions)
        {
            session.Status = UserSession.SessionStatus.Revoked;
            session.RevokedAtUtc = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync();
    }

    #endregion

    #region Core Auth Logic

    private async Task<AuthResultDto> CreateAuthResultAsync(int userId, Guid SessionId, DateTime SessionCreatedAtUtc)
    {
        var (user, accessToken) = await GenerateAccessTokenAsync(userId);

        var refreshToken = await CreateRefreshTokenAsync(userId, SessionId, SessionCreatedAtUtc);

        return new AuthResultDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = _mapper.Map<UserDto>(user)
        };
    }

    private async Task<(User user, string accessToken)> GenerateAccessTokenAsync(int userId)
    {
        var spec = new UserWithRoleSpecification(userId);

        var user = await _unitOfWork
            .Repository<User>()
            .FirstOrDefaultAsync(spec);

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
        DateTime sessionCreatedAt)
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

        await _unitOfWork.Repository<RefreshTokenEntity>().AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

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