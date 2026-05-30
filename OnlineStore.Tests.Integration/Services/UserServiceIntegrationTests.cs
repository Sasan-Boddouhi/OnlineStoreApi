using Application.Entities;
using Application.Exceptions;
using BusinessLogic.DTOs.User;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class UserServiceIntegrationTests : BaseIntegrationTest
{
    private IUserService UserService => GetService<IUserService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public UserServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task CreateAsync_ValidUser_CreatesAndReturnsDto()
    {
        var dto = new CreateUserDto
        {
            PhoneNumber = "09120001111",
            Password = "Strong@123",
            FirstName = "Ali",
            LastName = "Rezaei",
            DateOfBirth = "1370/01/01"
        };

        var result = await UserService.CreateAsync(dto);

        result.Should().NotBeNull();
        result.UserId.Should().BeGreaterThan(0);
        result.PhoneNumber.Should().Be(dto.PhoneNumber);

        var fromDb = await DbContext.User.FirstOrDefaultAsync(u => u.PhoneNumber == dto.PhoneNumber);
        fromDb.Should().NotBeNull();
        fromDb!.FirstName.Should().Be("Ali");
    }

    [Fact]
    public async Task CreateAsync_DuplicatePhone_ThrowsBusinessException()
    {
        var dto = new CreateUserDto
        {
            PhoneNumber = "09123456789", // admin phone in seed
            Password = "Test@123",
            FirstName = "Test",
            LastName = "User",
            DateOfBirth = "1370/01/01"
        };

        Func<Task> act = () => UserService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*قبلاً ثبت شده*");
    }

    [Fact]
    public async Task GetByIdAsync_ExistingUser_ReturnsDto()
    {
        var adminPhone = "09123456789";
        var user = await DbContext.User.FirstAsync(u => u.PhoneNumber == adminPhone);

        var result = await UserService.GetByIdAsync(user.UserId);
        result.Should().NotBeNull();
        result!.PhoneNumber.Should().Be(adminPhone);
    }

    [Fact]
    public async Task GetByPhoneNumberAsync_ExistingUser_ReturnsDto()
    {
        var result = await UserService.GetByPhoneNumberAsync("09123456789");
        result.Should().NotBeNull();
        result!.PhoneNumber.Should().Be("09123456789");
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_UpdatesUser()
    {
        var admin = await DbContext.User.FirstAsync(u => u.PhoneNumber == "09123456789");
        var dto = new UpdateUserDto
        {
            UserId = admin.UserId,
            PhoneNumber = "09123456789",
            FirstName = "Updated",
            LastName = "Admin"
        };

        var result = await UserService.UpdateAsync(dto);
        result.Should().NotBeNull();
        result!.FirstName.Should().Be("Updated");

        var fromDb = await DbContext.User.FindAsync(admin.UserId);
        fromDb!.FirstName.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteAsync_ExistingUser_ReturnsTrue()
    {
        var user = await DbContext.User.FirstAsync(u => u.PhoneNumber == "09123456789");
        var result = await UserService.DeleteAsync(user.UserId);
        result.Should().BeTrue();

        var deleted = await DbContext.User.FindAsync(user.UserId);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task SetActiveStatusAsync_ChangesActiveStatus()
    {
        var admin = await DbContext.User.FirstAsync(u => u.PhoneNumber == "09123456789");
        var result = await UserService.SetActiveStatusAsync(admin.UserId, false);
        result.Should().BeTrue();

        var updated = await DbContext.User.FindAsync(admin.UserId);
        updated!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetRolesAsync_ReturnsRoles()
    {
        var roles = await UserService.GetRolesAsync();
        roles.Should().NotBeEmpty();
        roles.Should().Contain(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase));
    }
}