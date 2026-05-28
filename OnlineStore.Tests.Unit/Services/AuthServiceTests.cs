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
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IJwtTokenService> _jwt = new();
    private readonly Mock<IPasswordHasher> _hasher = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<ILogger<AuthService>> _logger = new();

    private readonly Mock<IGenericRepository<User>> _userRepo = new();
    private readonly Mock<IGenericRepository<UserSession>> _sessionRepo = new();

    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _uow.Setup(u => u.Repository<User>()).Returns(_userRepo.Object);
        _uow.Setup(u => u.Repository<UserSession>()).Returns(_sessionRepo.Object);

        _service = new AuthService(
            _uow.Object,
            _jwt.Object,
            _hasher.Object,
            _mapper.Object,
            _logger.Object);
    }

    // --------------------------------------------------
    // REGISTER - SUCCESS
    // --------------------------------------------------
    [Fact]
    public async Task RegisterAsync_ValidDto_ReturnsResult()
    {
        // Arrange
        var registerDto = new RegisterDto
        {
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09123456789",
            Password = "Password123!",
            DateOfBirth = "1370/01/01",
            DeviceId = "device123",
            DeviceName = "UnitTest"
        };

        // Mock UnitOfWork و Repository‌ها
        var mockUnitOfWork = new Mock<IUnitOfWork>();
        var mockUserRepo = new Mock<IGenericRepository<User>>();
        var mockSessionRepo = new Mock<IGenericRepository<UserSession>>();
        var mockRefreshTokenRepo = new Mock<IGenericRepository<RefreshTokenEntity>>();

        mockUnitOfWork.Setup(x => x.Repository<User>()).Returns(mockUserRepo.Object);
        mockUnitOfWork.Setup(x => x.Repository<UserSession>()).Returns(mockSessionRepo.Object);
        mockUnitOfWork.Setup(x => x.Repository<RefreshTokenEntity>()).Returns(mockRefreshTokenRepo.Object);

        // تنظیم تراکنش‌ها – کلیدی
        mockUnitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
        mockUnitOfWork.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
        mockUnitOfWork.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);

        // بررسی نبودن کاربر تکراری
        mockUserRepo.Setup(x => x.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(false);

        // ذخیره‌سازی
        mockUserRepo.Setup(x => x.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
        mockSessionRepo.Setup(x => x.AddAsync(It.IsAny<UserSession>(), It.IsAny<CancellationToken>()))
                       .Returns(Task.CompletedTask);
        mockRefreshTokenRepo.Setup(x => x.AddAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<CancellationToken>()))
                            .Returns(Task.CompletedTask);

        // ترتیب SaveChangesAsync: اول برای User، بعد برای Session (و در RefreshToken نیز یکی دیگر)
        mockUnitOfWork.SetupSequence(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(1)
                      .ReturnsAsync(1)
                      .ReturnsAsync(1);

        // موک PasswordHasher
        var mockHasher = new Mock<IPasswordHasher>();
        mockHasher.Setup(x => x.Hash(registerDto.Password)).Returns("hashedPassword");
        mockHasher.Setup(x => x.Hash(It.IsAny<string>())).Returns("hashedRefresh");

        // موک JwtTokenService
        var mockJwtService = new Mock<IJwtTokenService>();
        mockJwtService.Setup(x => x.GenerateToken(It.IsAny<List<Claim>>())).Returns("access_token");
        mockJwtService.Setup(x => x.GenerateRefreshToken()).Returns("refresh_token_string");

        // موک Mapper
        var mockMapper = new Mock<IMapper>();
        mockMapper.Setup(m => m.Map<UserDto>(It.IsAny<User>())).Returns(new UserDto());

        // موک Logger
        var mockLogger = new Mock<ILogger<AuthService>>();

        var authService = new AuthService(
            mockUnitOfWork.Object,
            mockJwtService.Object,
            mockHasher.Object,
            mockMapper.Object,
            mockLogger.Object);

        // Act
        var result = await authService.RegisterAsync(registerDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("access_token", result.AccessToken);
        Assert.Equal("refresh_token_string", result.RefreshToken);
    }

    // --------------------------------------------------
    // REGISTER - DUPLICATE PHONE
    // --------------------------------------------------
    [Fact]
    public async Task RegisterAsync_DuplicatePhone_ThrowsException()
    {
        var dto = new RegisterDto
        {
            PhoneNumber = "09120000000",
            FirstName = "Ali",
            LastName = "Ahmadi",
            Password = "123456"
        };

        _userRepo.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), default))
            .ReturnsAsync(true);

        await Assert.ThrowsAsync<BusinessException>(() =>
            _service.RegisterAsync(dto));
    }

    // --------------------------------------------------
    // LOGIN - SUCCESS
    // --------------------------------------------------
    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsResult()
    {
        // Arrange
        var userId = 1;
        var sessionId = Guid.NewGuid();
        var user = new User
        {
            UserId = userId,
            FirstName = "Test",  
            LastName = "User",        
            PhoneNumber = "09123456789",
            PasswordHash = "hashed",
            IsActive = true,
            UserType = UserType.Customer,
            SecurityStamp = "stamp"
        };

        var loginDto = new LoginDto
        {
            PhoneNumber = "09123456789",
            Password = "123456",
            DeviceId = "device",
            DeviceName = "test"
        };

        // Mock dependencies
        var mockUnitOfWork = new Mock<IUnitOfWork>();
        var mockUserRepo = new Mock<IGenericRepository<User>>();
        var mockSessionRepo = new Mock<IGenericRepository<UserSession>>();
        var mockRefreshTokenRepo = new Mock<IGenericRepository<RefreshTokenEntity>>();

        mockUnitOfWork.Setup(x => x.Repository<User>()).Returns(mockUserRepo.Object);
        mockUnitOfWork.Setup(x => x.Repository<UserSession>()).Returns(mockSessionRepo.Object);
        mockUnitOfWork.Setup(x => x.Repository<RefreshTokenEntity>()).Returns(mockRefreshTokenRepo.Object);

        // Setup user retrieval
        var spec = It.IsAny<Spec<User>>(); // or use actual spec matching
        mockUserRepo.Setup(x => x.FirstOrDefaultAsync(It.IsAny<Spec<User>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(user);

        mockUnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(1);

        var mockJwtService = new Mock<IJwtTokenService>();
        mockJwtService.Setup(x => x.GenerateToken(It.IsAny<List<Claim>>()))
                      .Returns("access_token");
        mockJwtService.Setup(x => x.GenerateRefreshToken())
                      .Returns("refresh_token_string"); // کلیدی

        var mockHasher = new Mock<IPasswordHasher>();
        mockHasher.Setup(x => x.Verify(loginDto.Password, user.PasswordHash))
                  .Returns(true);
        mockHasher.Setup(x => x.Hash(It.IsAny<string>()))
                  .Returns("hashed_refresh_token");

        var mockMapper = new Mock<IMapper>();
        mockMapper.Setup(m => m.Map<UserDto>(It.IsAny<User>()))
                  .Returns(new UserDto { PhoneNumber = user.PhoneNumber });

        var logger = Mock.Of<ILogger<AuthService>>();

        var authService = new AuthService(
            mockUnitOfWork.Object,
            mockJwtService.Object,
            mockHasher.Object,
            mockMapper.Object,
            logger);

        // Act
        var result = await authService.LoginAsync(loginDto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("access_token", result.AccessToken);
        Assert.Equal("refresh_token_string", result.RefreshToken);
    }

    // --------------------------------------------------
    // LOGIN - WRONG PASSWORD
    // --------------------------------------------------
    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsNull()
    {
        var dto = new LoginDto
        {
            PhoneNumber = "09120000000",
            Password = "wrong"
        };

        var user = new User
        {
            UserId = 1,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09120000000",
            PasswordHash = "hashed",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        _userRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<User>>(), default))
            .ReturnsAsync(user);

        _hasher.Setup(h => h.Verify(dto.Password, user.PasswordHash))
            .Returns(false);

        var result = await _service.LoginAsync(dto);

        result.Should().BeNull();
    }

    // --------------------------------------------------
    // LOGIN - USER NOT FOUND
    // --------------------------------------------------
    [Fact]
    public async Task LoginAsync_UserNotFound_ReturnsNull()
    {
        _userRepo.Setup(r => r.FirstOrDefaultAsync(It.IsAny<Spec<User>>(), default))
            .ReturnsAsync((User?)null);

        var result = await _service.LoginAsync(new LoginDto());

        result.Should().BeNull();
    }
}