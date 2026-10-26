using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.Province;
using BusinessLogic.DTOs.Shared;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class ProvincesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public ProvincesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task GetAll_AllowAnonymous_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/api/v1/provinces");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProvinceDto>>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_AllowAnonymous_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var response = await Client.GetAsync($"/api/v1/provinces/{province.ProvinceId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ProvinceDto>();
        result.Should().NotBeNull();
        result!.ProvinceId.Should().Be(province.ProvinceId);
        result.ProvinceName.Should().Be(province.ProvinceName);
    }

    [Fact]
    public async Task GetById_NonExistent_ReturnsNotFound()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/api/v1/provinces/999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_AdminToken_ReturnsCreated()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateProvinceDto
        {
            ProvinceName = $"P-{Guid.NewGuid():N}"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/provinces", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ProvinceDto>();
        created.Should().NotBeNull();
        created!.ProvinceName.Should().Be(dto.ProvinceName);
    }

    [Fact]
    public async Task Create_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var dto = new CreateProvinceDto
        {
            ProvinceName = "UnauthProvince"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/provinces", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_AdminToken_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var updateDto = new UpdateProvinceDto
        {
            ProvinceId = province.ProvinceId,
            ProvinceName = $"UpdatedP-{Guid.NewGuid():N}"
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/provinces/{province.ProvinceId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_AdminToken_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"ToDelP-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var response = await Client.DeleteAsync($"/api/v1/provinces/{province.ProvinceId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
