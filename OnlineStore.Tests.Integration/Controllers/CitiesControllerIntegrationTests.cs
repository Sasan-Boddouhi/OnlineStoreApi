using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.Entities;
using BusinessLogic.DTOs.City;
using BusinessLogic.DTOs.Shared;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class CitiesControllerIntegrationTests : ControllerIntegrationTestBase
{
    public CitiesControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task GetAll_AllowAnonymous_ReturnsOk()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync("/api/v1/cities");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<CityDto>>();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_AdminToken_ReturnsCreated()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var dto = new CreateCityDto
        {
            CityName = $"City-{Guid.NewGuid():N}",
            ProvinceId = province.ProvinceId
        };

        var response = await Client.PostAsJsonAsync("/api/v1/cities", dto);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CityDto>();
        created.Should().NotBeNull();
        created!.CityName.Should().Be(dto.CityName);
    }

    [Fact]
    public async Task Create_Unauthorized_Returns401()
    {
        Client.DefaultRequestHeaders.Authorization = null;

        var dto = new CreateCityDto
        {
            CityName = "UnauthCity",
            ProvinceId = 1
        };

        var response = await Client.PostAsJsonAsync("/api/v1/cities", dto);

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

        var city = new City { CityName = $"Orig-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        db.City.Add(city);
        await db.SaveChangesAsync();

        var updateDto = new UpdateCityDto
        {
            CityId = city.CityId,
            CityName = $"Updated-{Guid.NewGuid():N}"
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/cities/{city.CityId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Delete_AdminToken_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var db = GetService<AppDbContext>();
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        db.Province.Add(province);
        await db.SaveChangesAsync();

        var city = new City { CityName = $"ToDel-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        db.City.Add(city);
        await db.SaveChangesAsync();

        var response = await Client.DeleteAsync($"/api/v1/cities/{city.CityId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
