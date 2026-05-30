using System.Linq.Expressions;
using System.Security.Claims;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Security;
using AutoMapper;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Implementations;
using BusinessLogic.Services.Interfaces;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace OnlineStore.Tests.Unit.Services;

public class AuthServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IJwtTokenService> _jwtMock;
    private readonly Mock<IPasswordHasher> _hasherMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<AuthService>> _loggerMock;

    private readonly Mock<IGenericRepository<User>> _userRepoMock;
    private readonly Mock<IGenericRepository<UserSession>> _sessionRepoMock;
    private readonly Mock<IGenericRepository<RefreshTokenEntity>> _refreshTokenRepoMock;

    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _jwtMock = new Mock<IJwtTokenService>();
        _hasherMock = new Mock<IPasswordHasher>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<AuthService>>();

        _userRepoMock = new Mock<IGenericRepository<User>>();
        _sessionRepoMock = new Mock<IGenericRepository<UserSession>>();
        _refreshTokenRepoMock = new Mock<IGenericRepository<RefreshTokenEntity>>();

        // تنظیم Repositoryهای پایه
        _uowMock.Setup(u => u.Repository<User>()).Returns(_userRepoMock.Object);
        _uowMock.Setup(u => u.Repository<UserSession>()).Returns(_sessionRepoMock.Object);
        _uowMock.Setup(u => u.Repository<RefreshTokenEntity>()).Returns(_refreshTokenRepoMock.Object);

        // تنظیم تراکنش‌ها به‌صورت پیش‌فرض (برای همه تست‌ها)
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _service = new AuthService(
            _uowMock.Object,
            _jwtMock.Object,
            _hasherMock.Object,
            _mapperMock.Object,
            _loggerMock.Object);
    }

    // ---------------------------------------------------------------
    // REGISTER - SUCCESS
    // ---------------------------------------------------------------
    [Fact]
    public async Task RegisterAsync_ValidDto_ReturnsAuthResult()
    {
        // Arrange
        var dto = new RegisterDto
        {
            FirstName = "Ali",
            LastName = "Rezaei",
            PhoneNumber = "09121111111",
            Password = "Strong@123",
            DateOfBirth = "1370/01/01",
            DeviceId = "dev1"
        };

        _userRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(false);

        _userRepoMock.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _sessionRepoMock.Setup(r => r.AddAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _refreshTokenRepoMock.Setup(r => r.AddAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _uowMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1)   // after User added
                .ReturnsAsync(1)   // after Session added
                .ReturnsAsync(1);  // after RefreshToken added

        _hasherMock.Setup(h => h.Hash(dto.Password)).Returns("hashed");
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed_refresh");

        _jwtMock.Setup(j => j.GenerateToken(It.IsAny<List<Claim>>())).Returns("access");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("refresh_raw");

        _mapperMock.Setup(m => m.Map<UserDto>(It.IsAny<User>())).Returns(new UserDto { PhoneNumber = dto.PhoneNumber });

        // Act
        var result = await _service.RegisterAsync(dto);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().Be("access");
        result.RefreshToken.Should().Be("refresh_raw");
    }

    // ---------------------------------------------------------------
    // REGISTER - DUPLICATE PHONE
    // ---------------------------------------------------------------
    [Fact]
    public async Task RegisterAsync_DuplicatePhone_ThrowsBusinessException()
    {
        var dto = new RegisterDto
        {
            PhoneNumber = "09120000000",
            FirstName = "Ali",
            LastName = "Ahmadi",
            Password = "123456"
        };

        _userRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(true);

        Func<Task> act = () => _service.RegisterAsync(dto);
        await act.Should().ThrowAsync<BusinessException>()
                 .WithMessage("*شماره تماس تکراری*");

        // Rollback باید یک بار فراخوانی شده باشد
        _uowMock.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------
    // LOGIN - SUCCESS
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsAuthResult()
    {
        var user = new User
        {
            UserId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09121111111",
            PasswordHash = "hashed",
            IsActive = true,
            UserType = UserType.Customer,
            SecurityStamp = "stamp"
        };

        var loginDto = new LoginDto
        {
            PhoneNumber = "09121111111",
            Password = "123456",
            DeviceId = "dev"
        };

        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<User>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        _hasherMock.Setup(h => h.Verify(loginDto.Password, user.PasswordHash)).Returns(true);
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("hashed_refresh");
        _jwtMock.Setup(j => j.GenerateToken(It.IsAny<List<Claim>>())).Returns("access");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("refresh_raw");

        _sessionRepoMock.Setup(r => r.AddAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _refreshTokenRepoMock.Setup(r => r.AddAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _uowMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1)  // after session added
                .ReturnsAsync(1); // after refresh token added

        _mapperMock.Setup(m => m.Map<UserDto>(It.IsAny<User>())).Returns(new UserDto { PhoneNumber = user.PhoneNumber });

        var result = await _service.LoginAsync(loginDto);

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("access");
        result.RefreshToken.Should().Be("refresh_raw");
    }

    // ---------------------------------------------------------------
    // LOGIN - WRONG PASSWORD
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        var user = new User
        {
            UserId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09120000000",
            PasswordHash = "hashed",
            IsActive = true,
            UserType = UserType.Customer,
            SecurityStamp = "stamp"
        };
        var loginDto = new LoginDto { PhoneNumber = "09120000000", Password = "wrong" };

        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<User>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);
        _hasherMock.Setup(h => h.Verify(loginDto.Password, user.PasswordHash)).Returns(false);

        var result = await _service.LoginAsync(loginDto);
        result.Should().BeNull();

        // باید تعداد تلاش‌های ناموفق افزایش یابد
        user.FailedLoginAttempts.Should().Be(1);
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------
    // LOGIN - USER LOCKED OUT
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoginAsync_UserLockedOut_ReturnsNull()
    {
        var user = new User
        {
            UserId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09120000000",
            PasswordHash = "hashed",
            IsActive = true,
            UserType = UserType.Customer,
            SecurityStamp = "stamp",
            LockoutEnd = DateTime.UtcNow.AddMinutes(10) // هنوز در قفل
        };

        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<User>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

        var result = await _service.LoginAsync(new LoginDto { PhoneNumber = "09120000000", Password = "any" });
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // LOGIN - USER NOT FOUND
    // ---------------------------------------------------------------
    [Fact]
    public async Task LoginAsync_UserNotFound_ReturnsNull()
    {
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<User>>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((User?)null);

        var result = await _service.LoginAsync(new LoginDto());
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // REFRESH - VALID TOKEN
    // ---------------------------------------------------------------
    [Fact]
    public async Task RefreshTokenAsync_ValidRefreshToken_ReturnsNewAuthResult()
    {
        var rawToken = "valid_raw_refresh";
        var identifier = ComputeSha256(rawToken); // باید محاسبه کنیم
        var user = new User
        {
            UserId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09121111111",
            PasswordHash = "hashed",
            IsActive = true,
            UserType = UserType.Customer,
            SecurityStamp = "stamp"
        };
        var session = new UserSession
        {
            Id = Guid.NewGuid(),
            UserId = 1,
            Status = UserSession.SessionStatus.Active,
            CreatedAtUtc = DateTime.UtcNow,
            LastActivityUtc = DateTime.UtcNow,
            AbsoluteExpiryUtc = DateTime.UtcNow.AddDays(30)
        };
        var refreshEntity = new RefreshTokenEntity
        {
            TokenIdentifier = identifier,
            TokenHash = "hashed_refresh",
            IsRevoked = false,
            Session = session,
            SessionId = session.Id,
            User = user,
            UserId = user.UserId
        };

        var mockRefreshRepo = new Mock<IGenericRepository<RefreshTokenEntity>>();
        _uowMock.Setup(u => u.Repository<RefreshTokenEntity>()).Returns(mockRefreshRepo.Object);

        mockRefreshRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<RefreshTokenEntity>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(refreshEntity);

        _hasherMock.Setup(h => h.Verify(rawToken, refreshEntity.TokenHash)).Returns(true);
        _hasherMock.Setup(h => h.Hash(It.IsAny<string>())).Returns("new_hashed_refresh");
        _jwtMock.Setup(j => j.GenerateToken(It.IsAny<List<Claim>>())).Returns("new_access");
        _jwtMock.Setup(j => j.GenerateRefreshToken()).Returns("new_raw_refresh");
        _mapperMock.Setup(m => m.Map<UserDto>(It.IsAny<User>())).Returns(new UserDto { PhoneNumber = user.PhoneNumber });

        // SaveChanges for revoking old token and creating new
        _uowMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(1)   // after revoking old token
                .ReturnsAsync(1);  // after adding new refresh token

        var result = await _service.RefreshTokenAsync(rawToken);

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("new_access");
        result.RefreshToken.Should().Be("new_raw_refresh");
        refreshEntity.IsRevoked.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // REFRESH - INVALID TOKEN
    // ---------------------------------------------------------------
    [Fact]
    public async Task RefreshTokenAsync_InvalidToken_ReturnsNull()
    {
        var mockRefreshRepo = new Mock<IGenericRepository<RefreshTokenEntity>>();
        _uowMock.Setup(u => u.Repository<RefreshTokenEntity>()).Returns(mockRefreshRepo.Object);

        mockRefreshRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<RefreshTokenEntity>>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync((RefreshTokenEntity?)null);

        var result = await _service.RefreshTokenAsync("invalid");
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------
    // LOGOUT SESSION
    // ---------------------------------------------------------------
    [Fact]
    public async Task LogoutSessionAsync_ExistingSession_RevokesSession()
    {
        var sessionId = Guid.NewGuid();
        var session = new UserSession { Id = sessionId, Status = UserSession.SessionStatus.Active };

        _sessionRepoMock.Setup(r => r.GetByIdAsync(sessionId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(session);

        await _service.LogoutSessionAsync(sessionId);

        session.Status.Should().Be(UserSession.SessionStatus.Revoked);
        session.RevokedAtUtc.Should().NotBeNull();
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogoutSessionAsync_NonExistentSession_DoesNothing()
    {
        _sessionRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((UserSession?)null);

        await _service.LogoutSessionAsync(Guid.NewGuid());
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------
    // LOGOUT ALL
    // ---------------------------------------------------------------
    [Fact]
    public async Task LogoutAllAsync_RevokesAllActiveSessions()
    {
        var userId = 1;
        var sessions = new List<UserSession>
        {
            new() { Id = Guid.NewGuid(), UserId = userId, Status = UserSession.SessionStatus.Active },
            new() { Id = Guid.NewGuid(), UserId = userId, Status = UserSession.SessionStatus.Active }
        };

        _sessionRepoMock.Setup(r => r.ListAsync(
                It.IsAny<Spec<UserSession>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessions);

        await _service.LogoutAllAsync(userId);

        sessions.Should().AllSatisfy(s =>
        {
            s.Status.Should().Be(UserSession.SessionStatus.Revoked);
            s.RevokedAtUtc.Should().NotBeNull();
        });
        _uowMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string ComputeSha256(string raw)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw)));
    }
}