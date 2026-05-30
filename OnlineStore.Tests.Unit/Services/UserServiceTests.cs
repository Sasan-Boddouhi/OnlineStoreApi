using System.Linq.Expressions;
using Application.Common.Queries;
using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Interfaces.Security;
using AutoMapper;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace OnlineStore.Tests.Unit.Services;

public class UserServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IPasswordHasher> _hasherMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly Mock<ILogger<UserService>> _loggerMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<IMapper> _mapperMock;

    private readonly Mock<IGenericRepository<User>> _userRepoMock;
    private readonly Mock<IGenericRepository<Address>> _addressRepoMock;

    private readonly UserService _service;

    public UserServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _hasherMock = new Mock<IPasswordHasher>();
        _currentUserMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<UserService>>();
        _mapperMock = new Mock<IMapper>();

        // استفاده از یک حافظهٔ نهان واقعی
        _cache = new MemoryCache(new MemoryCacheOptions());

        _userRepoMock = new Mock<IGenericRepository<User>>();
        _addressRepoMock = new Mock<IGenericRepository<Address>>();

        _uowMock.Setup(u => u.Repository<User>()).Returns(_userRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Address>()).Returns(_addressRepoMock.Object);

        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _service = new UserService(
            _uowMock.Object,
            _hasherMock.Object,
            _currentUserMock.Object,
            _loggerMock.Object,
            _cache,
            _mapperMock.Object);
    }

    // Helper to create a fully initialized User
    private static User CreateValidUser(int id = 1, string phone = "09120000000", string firstName = "Test", string lastName = "User")
        => new()
        {
            UserId = id,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phone,
            PasswordHash = "hashed",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };

    // ---- GetByIdAsync ----
    [Fact]
    public async Task GetByIdAsync_UserExists_ReturnsDto()
    {
        var userDto = new UserDto { UserId = 1, PhoneNumber = "09120000000" };
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDto);

        var result = await _service.GetByIdAsync(1);
        result.Should().BeEquivalentTo(userDto);
    }

    [Fact]
    public async Task GetByIdAsync_UserNotFound_ReturnsNull()
    {
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDto?)null);

        var result = await _service.GetByIdAsync(1);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithCacheHit_ReturnsCachedDto()
    {
        // Arrange: ابتدا یک بار داده را واکشی کن تا کش شود
        var userDto = new UserDto { UserId = 1, PhoneNumber = "09120000000" };
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDto);

        // بار اول: از پایگاه داده می‌آید و کش می‌شود
        var firstResult = await _service.GetByIdAsync(1, includeRoles: true);
        firstResult.Should().BeEquivalentTo(userDto);

        // بار دوم: باید از کش برگردد (بدون فراخوانی ریپازیتوری)
        _userRepoMock.Reset(); // ریست کن تا ببینیم دوباره صدا زده نمی‌شود
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserDto?)null); // اگر صدا زده شود، null برمی‌گرداند

        var secondResult = await _service.GetByIdAsync(1, includeRoles: true);
        secondResult.Should().BeEquivalentTo(userDto); // همچنان باید همان dto را برگرداند (از کش)
    }

    // ---- GetByPhoneNumberAsync ----
    [Fact]
    public async Task GetByPhoneNumberAsync_UserExists_ReturnsDto()
    {
        var userDto = new UserDto { PhoneNumber = "09120000000" };
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDto);

        var result = await _service.GetByPhoneNumberAsync("09120000000");
        result.Should().BeEquivalentTo(userDto);
    }

    // ---- GetCurrentUserAsync ----
    [Fact]
    public async Task GetCurrentUserAsync_Authenticated_ReturnsDto()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(1);
        var userDto = new UserDto { UserId = 1 };
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDto);

        var result = await _service.GetCurrentUserAsync();
        result.Should().BeEquivalentTo(userDto);
    }

    [Fact]
    public async Task GetCurrentUserAsync_NotAuthenticated_ReturnsNull()
    {
        _currentUserMock.Setup(c => c.GetCurrentUserId()).Returns(0);
        var result = await _service.GetCurrentUserAsync();
        result.Should().BeNull();
    }

    // ---- CreateAsync ----
    [Fact]
    public async Task CreateAsync_ValidDto_CreatesUser()
    {
        var dto = new CreateUserDto
        {
            PhoneNumber = "09120000000",
            Password = "Test@123",
            FirstName = "Ali",
            LastName = "Rezaei",
            DateOfBirth = "1370/01/01"
        };
        var user = CreateValidUser(5, dto.PhoneNumber, dto.FirstName, dto.LastName);
        var userDto = new UserDto { UserId = 5, PhoneNumber = dto.PhoneNumber };

        _userRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<User>(dto)).Returns(user);
        _hasherMock.Setup(h => h.Hash(dto.Password)).Returns("hashed");
        _userRepoMock.Setup(r => r.AddAsync(user, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1).ReturnsAsync(1);

        // شبیه‌سازی GetByIdAsync که بعد از ایجاد فراخوانی می‌شود
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDto);

        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.PhoneNumber.Should().Be(dto.PhoneNumber);
    }

    [Fact]
    public async Task CreateAsync_DuplicatePhone_ThrowsBusinessException()
    {
        var dto = new CreateUserDto { PhoneNumber = "09120000000", Password = "Test@123", FirstName = "Ali", LastName = "Rezaei" };
        _userRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<BusinessException>(() => _service.CreateAsync(dto));
    }

    // ---- UpdateAsync ----
    [Fact]
    public async Task UpdateAsync_UserExists_UpdatesAndReturnsDto()
    {
        var dto = new UpdateUserDto { UserId = 1, PhoneNumber = "09120000001", FirstName = "Ali", LastName = "Updated" };
        var user = CreateValidUser(1, "09120000000", "Old", "User");
        var userDto = new UserDto { UserId = 1, PhoneNumber = "09120000001" };

        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _userRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<User, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _userRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<User>>(),
                It.IsAny<Expression<Func<User, UserDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(userDto);

        var result = await _service.UpdateAsync(dto);
        result.Should().NotBeNull();
        result!.PhoneNumber.Should().Be("09120000001");
    }

    [Fact]
    public async Task UpdateAsync_UserNotFound_ReturnsNull()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var result = await _service.UpdateAsync(new UpdateUserDto { UserId = 999 });
        result.Should().BeNull();
    }

    // ---- DeleteAsync ----
    [Fact]
    public async Task DeleteAsync_UserExists_ReturnsTrue()
    {
        var user = CreateValidUser(1);
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _service.DeleteAsync(1);
        result.Should().BeTrue();
        _userRepoMock.Verify(r => r.Delete(user), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_UserNotFound_ReturnsFalse()
    {
        _userRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        var result = await _service.DeleteAsync(1);
        result.Should().BeFalse();
    }

    // ---- SetActiveStatusAsync ----
    [Fact]
    public async Task SetActiveStatusAsync_UserExists_UpdatesStatus()
    {
        var user = CreateValidUser(1);
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _service.SetActiveStatusAsync(1, false);
        result.Should().BeTrue();
        user.IsActive.Should().BeFalse();
    }

    // ---- GetRolesAsync ----
    [Fact]
    public async Task GetRolesAsync_ReturnsDistinctRoles()
    {
        var users = new List<User>
        {
            CreateValidUser(1, phone: "09120000001", firstName: "Cust", lastName: "Omer"),
            CreateValidUser(2, phone: "09120000002", firstName: "Emp", lastName: "Loyee")
        };
        // Make second user an employee with Admin type
        users[1].UserType = UserType.Employee;
        users[1].Employee = new Employee
        {
            EmployeeId = 1,
            UserId = 2,
            EmployeeTypeId = 10,
            EmployeeNumber = "E-001",
            Salary = 5000,
            HireDate = DateTime.Today,
            EmployeeType = new EmployeeType
            {
                EmployeeTypeId = 10,
                TypeName = "Admin",
                DisplayName = "Administrator"
            }
        };

        _userRepoMock.Setup(r => r.ListAsync(It.IsAny<Spec<User>>(), It.IsAny<CancellationToken>())).ReturnsAsync(users);

        var roles = await _service.GetRolesAsync();
        roles.Should().Contain("Customer");
        roles.Should().Contain("Admin");
    }
}