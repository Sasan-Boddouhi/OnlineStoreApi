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

    // ================= REGISTER =================
    public async Task<AuthResultDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            var exists = await _unitOfWork.Repository<User>()
                .AnyAsync(x => x.PhoneNumber == dto.PhoneNumber, ct);

            if (exists)
                throw new BusinessException("شماره تماس تکراری است.");

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

            var session = CreateSession(user.UserId, dto);

            await _unitOfWork.Repository<UserSession>().AddAsync(session, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            return await CreateAuthResultAsync(user, session, ct);
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
            return null;

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.UtcNow)
            return null;

        if (!_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts >= maxFailed)
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(lockMinutes);

            await _unitOfWork.SaveChangesAsync(ct);
            return null;
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;

        var session = CreateSession(user.UserId, dto);

        await _unitOfWork.Repository<UserSession>().AddAsync(session, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await CreateAuthResultAsync(user, session, ct);
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

        if (!_passwordHasher.Verify(refreshToken, token.TokenHash))
            return null;

        if (token.IsRevoked || token.Session.Status != UserSession.SessionStatus.Active)
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
            token.IsRevoked = true;
            token.RevokedAtUtc = DateTime.UtcNow;
            token.Session.LastActivityUtc = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync(ct);

            var result = await CreateAuthResultAsync(token.User, token.Session, ct);

            await _unitOfWork.CommitTransactionAsync(ct);

            return result;
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

        session.Status = UserSession.SessionStatus.Revoked;
        session.RevokedAtUtc = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task LogoutAllAsync(int userId, CancellationToken ct = default)
    {
        var sessions = await _unitOfWork.Repository<UserSession>()
            .ListAsync(
                new Spec<UserSession>()
                    .Where(x => x.UserId == userId && x.Status == UserSession.SessionStatus.Active)
                    .AsTracking(),
                ct);

        foreach (var s in sessions)
        {
            s.Status = UserSession.SessionStatus.Revoked;
            s.RevokedAtUtc = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync(ct);
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

    private UserSession CreateSession(int userId, dynamic dto)
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
}