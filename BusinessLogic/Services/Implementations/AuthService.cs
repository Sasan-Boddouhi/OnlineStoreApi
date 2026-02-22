using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Security;
using AutoMapper;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Auth;
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
                DateOfBirth = dto.DateOfBirth,
                PasswordHash = _passwordHasher.Hash(dto.Password),
                UserType = UserType.Customer,
                IsActive = true
            };

            await _unitOfWork.Repository<User>().AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            await _unitOfWork.CommitTransactionAsync();

            // ایجاد خانواده جدید با زمان ایجاد هم‌اکنون
            return await CreateAuthResultAsync(user.UserId, Guid.NewGuid(), DateTime.UtcNow);
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
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("User logged in successfully: {UserId}", user.UserId);
            return await CreateAuthResultAsync(user.UserId, Guid.NewGuid(), DateTime.UtcNow);
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

        var tokenEntity = await _unitOfWork
            .Repository<RefreshTokenEntity>()
            .FirstOrDefaultAsync(spec);

        if (tokenEntity is null)
        {
            _logger.LogWarning("Refresh token not found by identifier");
            return null;
        }

        if (!_passwordHasher.Verify(refreshToken, tokenEntity.TokenHash))
        {
            _logger.LogWarning("Refresh token hash verification failed");
            return null;
        }

        if (tokenEntity.AbsoluteExpiry <= DateTime.UtcNow)
        {
            _logger.LogWarning("Absolute expiration reached for family {FamilyId}, user {UserId}",
                tokenEntity.FamilyId, tokenEntity.UserId);
            await RevokeTokenFamily(tokenEntity.FamilyId);
            return null;
        }

        if (tokenEntity.IsRevoked || tokenEntity.ExpiryDate <= DateTime.UtcNow)
        {
            _logger.LogWarning("Token revoked or expired, revoking family {FamilyId}", tokenEntity.FamilyId);
            await RevokeTokenFamily(tokenEntity.FamilyId);
            return null;
        }

        await _unitOfWork.BeginTransactionAsync();
        try
        {
            tokenEntity.IsRevoked = true;
            tokenEntity.RevokedAtUtc = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();

            var newAuthResult = await CreateAuthResultAsync(tokenEntity.UserId, tokenEntity.FamilyId, tokenEntity.FamilyCreatedAt);

            await _unitOfWork.CommitTransactionAsync();
            return newAuthResult;
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrent refresh detected for token {Identifier}, revoking family {FamilyId}", identifier, tokenEntity.FamilyId);
            await _unitOfWork.RollbackTransactionAsync();
            await RevokeTokenFamily(tokenEntity.FamilyId);
            return null;
        }
        catch (Exception ex)
        {
            await _unitOfWork.RollbackTransactionAsync();
            _logger.LogError(ex, "Error during token refresh");
            throw;
        }
    }

    #endregion

    #region Logout

    public async Task LogoutAsync(int userId)
    {
        var spec = new ActiveUserRefreshTokensSpecification(userId);

        var tokens = await _unitOfWork
            .Repository<RefreshTokenEntity>()
            .ListAsync(spec);

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAtUtc = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync();
    }

    #endregion

    #region Core Auth Logic

    private async Task<AuthResultDto> CreateAuthResultAsync(int userId, Guid familyId, DateTime familyCreatedAt)
    {
        var (user, accessToken) = await GenerateAccessTokenAsync(userId);

        var refreshToken = await CreateRefreshTokenAsync(userId, familyId, familyCreatedAt);

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

    private async Task<string> CreateRefreshTokenAsync(int userId, Guid familyId, DateTime familyCreatedAt)
    {
        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        var entity = new RefreshTokenEntity
        {
            UserId = userId,
            TokenHash = _passwordHasher.Hash(refreshToken),
            TokenIdentifier = ComputeSha256Hash(refreshToken),
            FamilyId = familyId,
            FamilyCreatedAt = familyCreatedAt,
            AbsoluteExpiry = familyCreatedAt.Add(AbsoluteExpirationPeriod),
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRevoked = false
        };

        await _unitOfWork.Repository<RefreshTokenEntity>().AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return refreshToken;
    }

    private async Task RevokeTokenFamily(Guid familyId)
    {
        var spec = new RefreshTokenFamilySpecification(familyId);

        var tokens = await _unitOfWork
            .Repository<RefreshTokenEntity>()
            .ListAsync(spec);

        foreach (var token in tokens)
        {
            token.IsRevoked = true;
            token.RevokedAtUtc = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private static string ComputeSha256Hash(string rawData)
    {
        using var sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawData));
        return Convert.ToBase64String(bytes);
    }

    #endregion
}