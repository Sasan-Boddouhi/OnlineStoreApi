using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Application.Common.Specifications;

namespace OnlineStore.Tests.Unit.Services;

public class EmployeeServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<EmployeeService>> _loggerMock;
    private readonly EmployeeService _service;

    private readonly Mock<IGenericRepository<Employee>> _employeeRepoMock;
    private readonly Mock<IGenericRepository<User>> _userRepoMock;
    private readonly Mock<IGenericRepository<EmployeeType>> _employeeTypeRepoMock;

    public EmployeeServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<EmployeeService>>();

        _employeeRepoMock = new Mock<IGenericRepository<Employee>>();
        _userRepoMock = new Mock<IGenericRepository<User>>();
        _employeeTypeRepoMock = new Mock<IGenericRepository<EmployeeType>>();

        _uowMock.Setup(u => u.Repository<Employee>()).Returns(_employeeRepoMock.Object);
        _uowMock.Setup(u => u.Repository<User>()).Returns(_userRepoMock.Object);
        _uowMock.Setup(u => u.Repository<EmployeeType>()).Returns(_employeeTypeRepoMock.Object);

        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _service = new EmployeeService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    // ---------- Helpers ----------
    private static User CreateValidUser(int id = 1, UserType type = UserType.Employee)
        => new()
        {
            UserId = id,
            FirstName = "Test",
            LastName = "User",
            PhoneNumber = "09120000000",
            PasswordHash = "hashed",
            UserType = type,
            SecurityStamp = Guid.NewGuid().ToString()
        };

    private static EmployeeType CreateValidEmployeeType(int id = 10)
        => new()
        {
            EmployeeTypeId = id,
            TypeName = "Manager",
            DisplayName = "Manager"
        };

    private static Employee CreateValidEmployee(int id = 1, int userId = 1, int typeId = 10, string empNumber = "E-001")
    {
        var emp = new Employee
        {
            EmployeeId = id,
            UserId = userId,
            EmployeeTypeId = typeId,
            EmployeeNumber = empNumber,
            Salary = 5000,
            HireDate = DateTime.Today
        };
        return emp;
    }

    // ------------- CreateAsync ---------------
    [Fact]
    public async Task CreateAsync_ValidDto_CreatesEmployee()
    {
        var dto = new CreateEmployeeDto
        {
            UserId = 1,
            EmployeeTypeId = 10,
            EmployeeNumber = "E-001",
            Salary = 5000,
            HireDate = DateTime.Today
        };
        var user = CreateValidUser(1);
        var empType = CreateValidEmployeeType(10);
        var employee = CreateValidEmployee(1);
        var employeeDto = new EmployeeDto { EmployeeId = 1 };

        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _employeeTypeRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<EmployeeType, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _employeeRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Employee, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<Employee>(dto)).Returns(employee);
        _employeeRepoMock.Setup(r => r.AddAsync(employee, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1).ReturnsAsync(1);
        _employeeRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Employee>>(),
                It.IsAny<Expression<Func<Employee, EmployeeDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(employeeDto);

        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.EmployeeId.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmployeeNumber_ThrowsBusinessException()
    {
        var dto = new CreateEmployeeDto
        {
            UserId = 1,
            EmployeeTypeId = 10,
            EmployeeNumber = "E-001",
            Salary = 5000,
            HireDate = DateTime.Today
        };
        var user = CreateValidUser(1);
        var empType = CreateValidEmployeeType(10);
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _employeeTypeRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<EmployeeType, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _employeeRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Employee, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true); // duplicate number

        Func<Task> act = () => _service.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*خطا در ایجاد کارمند*");
    }

    [Fact]
    public async Task CreateAsync_InvalidUserType_ThrowsBusinessException()
    {
        var dto = new CreateEmployeeDto
        {
            UserId = 1,
            EmployeeTypeId = 10,
            EmployeeNumber = "E-001",
            Salary = 5000,
            HireDate = DateTime.Today
        };
        var user = CreateValidUser(1, UserType.Customer); // Not Employee type
        _userRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _employeeTypeRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<EmployeeType, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Func<Task> act = () => _service.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*خطا در ایجاد کارمند*");
    }

    // ------------- UpdateAsync ---------------
    [Fact]
    public async Task UpdateAsync_ValidDto_UpdatesAndReturnsDto()
    {
        var dto = new UpdateEmployeeDto
        {
            EmployeeId = 1,
            EmployeeTypeId = 10,
            EmployeeNumber = "E-002",
            Salary = 6000
        };
        var existingEmployee = CreateValidEmployee(1);
        var empType = CreateValidEmployeeType(10);
        var updatedDto = new EmployeeDto { EmployeeId = 1, EmployeeNumber = "E-002" };

        _employeeRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existingEmployee);
        _employeeTypeRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<EmployeeType, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _employeeRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Employee, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _employeeRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Employee>>(),
                It.IsAny<Expression<Func<Employee, EmployeeDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        var result = await _service.UpdateAsync(dto);
        result.Should().NotBeNull();
        result!.EmployeeNumber.Should().Be("E-002");
    }

    [Fact]
    public async Task UpdateAsync_EmployeeNotFound_ReturnsNull()
    {
        _employeeRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        var result = await _service.UpdateAsync(new UpdateEmployeeDto { EmployeeId = 999 });
        result.Should().BeNull();
    }

    // ------------- DeleteAsync ---------------
    [Fact]
    public async Task DeleteAsync_ExistingEmployee_ReturnsTrue()
    {
        var employee = CreateValidEmployee(1);
        _employeeRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(employee);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await _service.DeleteAsync(1);
        result.Should().BeTrue();
        _employeeRepoMock.Verify(r => r.Delete(employee), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        _employeeRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((Employee?)null);
        var result = await _service.DeleteAsync(1);
        result.Should().BeFalse();
    }

    // ------------- GetByIdAsync ---------------
    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsDto()
    {
        var dto = new EmployeeDto { EmployeeId = 1 };
        _employeeRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Employee>>(),
                It.IsAny<Expression<Func<Employee, EmployeeDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        var result = await _service.GetByIdAsync(1);
        result.Should().BeEquivalentTo(dto);
    }

    // ------------- GetByUserIdAsync ---------------
    [Fact]
    public async Task GetByUserIdAsync_Exists_ReturnsDto()
    {
        var dto = new EmployeeDto { UserId = 1 };
        _employeeRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Employee>>(),
                It.IsAny<Expression<Func<Employee, EmployeeDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        var result = await _service.GetByUserIdAsync(1);
        result.Should().BeEquivalentTo(dto);
    }

    // ------------- GetByQueryAsync ---------------
    [Fact]
    public async Task GetByQueryAsync_ReturnsPagedResult()
    {
        var query = new QueryContract<Employee> { Page = 1, Size = 2 };
        var dtos = new List<EmployeeDto> { new() { EmployeeId = 1 }, new() { EmployeeId = 2 } };
        _employeeRepoMock.Setup(r => r.ListAsync(
                It.IsAny<Spec<Employee>>(),
                It.IsAny<Expression<Func<Employee, EmployeeDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dtos);
        _employeeRepoMock.Setup(r => r.CountAsync(It.IsAny<Spec<Employee>>(), It.IsAny<CancellationToken>())).ReturnsAsync(5);

        var result = await _service.GetByQueryAsync(query);
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
    }
}