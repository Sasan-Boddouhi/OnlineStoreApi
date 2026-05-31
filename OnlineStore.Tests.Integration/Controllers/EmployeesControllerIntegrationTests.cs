using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Employee;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class EmployeesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public EmployeesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private IEmployeeService EmployeeService => GetService<IEmployeeService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    // Helper: create an EmployeeType and a User, then create an Employee
    private async Task<int> CreateEmployeeForAdminAsync()
    {
        // Ensure admin user exists (from seed)
        var adminUser = await DbContext.User.FirstAsync(u => u.PhoneNumber == "09123456789");

        // Create EmployeeType if not already (seed should have one, but to be safe)
        var empType = await DbContext.EmployeeType.FirstOrDefaultAsync(et => et.TypeName == "Admin");
        if (empType == null)
        {
            empType = new EmployeeType { TypeName = "Admin", DisplayName = "Administrator" };
            DbContext.EmployeeType.Add(empType);
            await DbContext.SaveChangesAsync();
        }

        // Check if admin already has an employee record
        var existing = await DbContext.Employee.FirstOrDefaultAsync(e => e.UserId == adminUser.UserId);
        if (existing != null) return existing.EmployeeId;

        var createDto = new CreateEmployeeDto
        {
            UserId = adminUser.UserId,
            EmployeeTypeId = empType.EmployeeTypeId,
            EmployeeNumber = "EMP-ADM-001",
            Salary = 5000,
            HireDate = DateTime.Today
        };

        var created = await EmployeeService.CreateAsync(createDto);
        return created.EmployeeId;
    }

    // ========== GET /api/employees/me ==========
    [Fact]
    public async Task GetMyProfile_Authenticated_ReturnsOk()
    {
        await CreateEmployeeForAdminAsync(); // ensure employee record exists
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/employees/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var employee = await response.Content.ReadFromJsonAsync<EmployeeDto>();
        employee.Should().NotBeNull();
        employee!.EmployeeNumber.Should().Be("E-001");
    }

    [Fact]
    public async Task GetMyProfile_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/employees/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ========== GET /api/employees ==========
    [Fact]
    public async Task GetEmployees_ReturnsOk_WithPagination()
    {
        await CreateEmployeeForAdminAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/employees?pageNumber=1&pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetEmployees_WithInvalidFilter_ReturnsBadRequest()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/employees?filter=invalidField eq 'x'");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ========== GET /api/employees/{id} ==========
    [Fact]
    public async Task GetById_Existing_ReturnsOk()
    {
        var empId = await CreateEmployeeForAdminAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/employees/{empId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var employee = await response.Content.ReadFromJsonAsync<EmployeeDto>();
        employee.Should().NotBeNull();
        employee!.EmployeeId.Should().Be(empId);
    }

    [Fact]
    public async Task GetById_NonExisting_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/employees/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ========== POST /api/employees ==========
    [Fact]
    public async Task CreateEmployee_ValidData_ReturnsCreated()
    {
        // Need a user with Employee type
        var user = new User
        {
            FirstName = "Emp",
            LastName = "Loyee",
            PhoneNumber = "09120006666",
            PasswordHash = "hash",
            UserType = UserType.Employee,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var empType = await DbContext.EmployeeType.FirstOrDefaultAsync(et => et.TypeName == "Admin");
        if (empType == null)
        {
            empType = new EmployeeType { TypeName = "Admin", DisplayName = "Administrator" };
            DbContext.EmployeeType.Add(empType);
            await DbContext.SaveChangesAsync();
        }

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateEmployeeDto
        {
            UserId = user.UserId,
            EmployeeTypeId = empType.EmployeeTypeId,
            EmployeeNumber = "EMP-TEST-001",
            Salary = 4000,
            HireDate = DateTime.Today
        };

        var response = await Client.PostAsJsonAsync("/api/employees", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<EmployeeDto>();
        created.Should().NotBeNull();
        created!.EmployeeId.Should().BeGreaterThan(0);
        response.Headers.Location!.AbsolutePath.Should().Contain(created.EmployeeId.ToString());
    }

    // ========== PUT /api/employees/{id} ==========
    [Fact]
    public async Task UpdateEmployee_ValidData_ReturnsNoContent()
    {
        var empId = await CreateEmployeeForAdminAsync();
        var getResponse = await Client.GetAsync($"/api/employees/{empId}");
        var current = await getResponse.Content.ReadFromJsonAsync<EmployeeDto>();

        var updateDto = new UpdateEmployeeDto
        {
            EmployeeId = empId,
            EmployeeTypeId = current!.EmployeeTypeId,
            EmployeeNumber = current.EmployeeNumber,
            Salary = 6000
        };

        var response = await Client.PutAsJsonAsync($"/api/employees/{empId}", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateEmployee_IdMismatch_ReturnsBadRequest()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateDto = new UpdateEmployeeDto { EmployeeId = 5 };
        var response = await Client.PutAsJsonAsync("/api/employees/10", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ========== DELETE /api/employees/{id} ==========
    [Fact]
    public async Task DeleteEmployee_Existing_ReturnsNoContent()
    {
        var empId = await CreateEmployeeForAdminAsync();
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync($"/api/employees/{empId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteEmployee_NonExisting_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync("/api/employees/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}