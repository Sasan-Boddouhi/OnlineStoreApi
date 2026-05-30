using Application.Entities;
using Application.Exceptions;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class EmployeeServiceIntegrationTests : BaseIntegrationTest
{
    private IEmployeeService EmployeeService => GetService<IEmployeeService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public EmployeeServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<User> CreateEmployeeUserAsync()
    {
        var user = new User
        {
            FirstName = "Emp",
            LastName = "Loyee",
            PhoneNumber = $"0912{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Employee,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();
        return user;
    }

    private async Task<EmployeeType> CreateEmployeeTypeAsync()
    {
        var type = new EmployeeType
        {
            TypeName = "TestType",
            DisplayName = "Test Type"
        };
        DbContext.EmployeeType.Add(type);
        await DbContext.SaveChangesAsync();
        return type;
    }

    [Fact]
    public async Task CreateAsync_ValidData_CreatesEmployee()
    {
        var user = await CreateEmployeeUserAsync();
        var type = await CreateEmployeeTypeAsync();

        var dto = new CreateEmployeeDto
        {
            UserId = user.UserId,
            EmployeeTypeId = type.EmployeeTypeId,
            EmployeeNumber = "EMP-001",
            Salary = 5000,
            HireDate = DateTime.Today
        };

        var result = await EmployeeService.CreateAsync(dto);
        result.Should().NotBeNull();
        result.EmployeeId.Should().BeGreaterThan(0);
        result.EmployeeNumber.Should().Be("EMP-001");

        var fromDb = await DbContext.Employee.FindAsync(result.EmployeeId);
        fromDb.Should().NotBeNull();
        fromDb!.Salary.Should().Be(5000);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNumber_ThrowsBusinessException()
    {
        var user = await CreateEmployeeUserAsync();
        var type = await CreateEmployeeTypeAsync();

        var dto = new CreateEmployeeDto
        {
            UserId = user.UserId,
            EmployeeTypeId = type.EmployeeTypeId,
            EmployeeNumber = "EMP-002",
            Salary = 5000,
            HireDate = DateTime.Today
        };

        await EmployeeService.CreateAsync(dto);

        var user2 = await CreateEmployeeUserAsync();
        var dto2 = new CreateEmployeeDto
        {
            UserId = user2.UserId,
            EmployeeTypeId = type.EmployeeTypeId,
            EmployeeNumber = "EMP-002",
            Salary = 5000,
            HireDate = DateTime.Today
        };

        Func<Task> act = () => EmployeeService.CreateAsync(dto2);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*خطا در ایجاد کارمند*");
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_UpdatesEmployee()
    {
        var user = await CreateEmployeeUserAsync();
        var type = await CreateEmployeeTypeAsync();
        var createDto = new CreateEmployeeDto
        {
            UserId = user.UserId,
            EmployeeTypeId = type.EmployeeTypeId,
            EmployeeNumber = "EMP-003",
            Salary = 4000,
            HireDate = DateTime.Today
        };
        var created = await EmployeeService.CreateAsync(createDto);

        // پاک کردن ردیاب برای جلوگیری از تداخل
        DbContext.ChangeTracker.Clear();

        // مقداردهی کلیدهای خارجی و شماره پرسنلی فعلی برای جلوگیری از نقض FK
        var updateDto = new UpdateEmployeeDto
        {
            EmployeeId = created.EmployeeId,
            EmployeeTypeId = type.EmployeeTypeId,
            EmployeeNumber = "EMP-003", // همان شماره قبلی
            Salary = 7000
        };

        var updated = await EmployeeService.UpdateAsync(updateDto);
        updated.Should().NotBeNull();
        updated!.Salary.Should().Be(7000);

        var fromDb = await DbContext.Employee.FindAsync(created.EmployeeId);
        fromDb!.Salary.Should().Be(7000);
    }

    [Fact]
    public async Task DeleteAsync_ExistingEmployee_DeletesAndReturnsTrue()
    {
        var user = await CreateEmployeeUserAsync();
        var type = await CreateEmployeeTypeAsync();
        var dto = new CreateEmployeeDto
        {
            UserId = user.UserId,
            EmployeeTypeId = type.EmployeeTypeId,
            EmployeeNumber = "EMP-004",
            Salary = 5000,
            HireDate = DateTime.Today
        };
        var created = await EmployeeService.CreateAsync(dto);

        DbContext.ChangeTracker.Clear();

        var result = await EmployeeService.DeleteAsync(created.EmployeeId);
        result.Should().BeTrue();

        var deleted = await DbContext.Employee.FindAsync(created.EmployeeId);
        deleted.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_Existing_ReturnsDto()
    {
        var user = await CreateEmployeeUserAsync();
        var type = await CreateEmployeeTypeAsync();
        var dto = new CreateEmployeeDto
        {
            UserId = user.UserId,
            EmployeeTypeId = type.EmployeeTypeId,
            EmployeeNumber = "EMP-005",
            Salary = 5000,
            HireDate = DateTime.Today
        };
        var created = await EmployeeService.CreateAsync(dto);

        DbContext.ChangeTracker.Clear();

        var result = await EmployeeService.GetByIdAsync(created.EmployeeId);
        result.Should().NotBeNull();
        result!.EmployeeNumber.Should().Be("EMP-005");
    }
}