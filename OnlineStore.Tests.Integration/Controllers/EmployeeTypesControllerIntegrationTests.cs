using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.EmployeeType;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class EmployeeTypesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public EmployeeTypesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    // ========== GET /api/v1/employeetypes ==========
    [Fact]
    public async Task Get_ReturnsOk_WithPagination()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/employeetypes?pageNumber=1&pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_WithInvalidFilter_ReturnsBadRequest()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/employeetypes?filter=invalidField eq 'x'");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ========== GET /api/v1/employeetypes/{id} ==========
    [Fact]
    public async Task GetById_Existing_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Use seed data: should have at least one EmployeeType (Admin)
        var response = await Client.GetAsync("/api/v1/employeetypes/1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetById_NonExisting_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/employeetypes/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ========== POST /api/v1/employeetypes ==========
    [Fact]
    public async Task Create_ValidData_ReturnsCreated()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateEmployeeTypeDto { TypeName = "TestType" };
        var response = await Client.PostAsJsonAsync("/api/v1/employeetypes", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<EmployeeTypeDto>();
        created.Should().NotBeNull();
        created!.EmployeeTypeId.Should().BeGreaterThan(0);
    }

    // ========== PUT /api/v1/employeetypes/{id} ==========
    [Fact]
    public async Task Update_ValidData_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create a new type first
        var createDto = new CreateEmployeeTypeDto { TypeName = "UpdateMe" };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/employeetypes", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeeTypeDto>();

        var updateDto = new UpdateEmployeeTypeDto
        {
            EmployeeTypeId = created!.EmployeeTypeId,
            TypeName = "UpdatedType"
        };
        var response = await Client.PutAsJsonAsync($"/api/v1/employeetypes/{created.EmployeeTypeId}", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ========== DELETE /api/v1/employeetypes/{id} ==========
    [Fact]
    public async Task Delete_Existing_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create a type to delete
        var createDto = new CreateEmployeeTypeDto { TypeName = "DeleteMe" };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/employeetypes", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeeTypeDto>();

        var response = await Client.DeleteAsync($"/api/v1/employeetypes/{created!.EmployeeTypeId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_NonExisting_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync("/api/v1/employeetypes/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}