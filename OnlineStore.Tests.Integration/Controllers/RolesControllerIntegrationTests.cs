using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Role;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class RolesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public RolesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task GetAll_Admin_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/roles");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_Admin_ReturnsOk()
    {
        var db = GetService<AppDbContext>();
        var role = await db.Role.FirstAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/roles/{role.RoleId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetByCode_Admin_ReturnsOk()
    {
        var db = GetService<AppDbContext>();
        var role = await db.Role.FirstAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync($"/api/v1/roles/code/{role.Code}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetPermissions_Admin_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/roles/permissions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_Admin_ReturnsCreated()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateRoleDto
        {
            Code = "ROLE_" + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "Test Custom Role",
            Description = "A role created by test"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/roles", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetAll_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/api/v1/roles");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_Admin_ReturnsNoContent()
    {
        var db = GetService<AppDbContext>();
        var role = new Role
        {
            Code = "DEL_ROLE_" + Guid.NewGuid().ToString("N")[..8],
            DisplayName = "To Delete",
            IsSystem = false
        };
        db.Role.Add(role);
        await db.SaveChangesAsync();

        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync($"/api/v1/roles/{role.RoleId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
