using System.Reflection;
using Application.Entities;
using BusinessLogic.DTOs.City;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class CityServiceIntegrationTests : BaseIntegrationTest
{
    private ICityService CityService => GetService<ICityService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public CityServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private static CreateCityDto CreateDto(string cityName, int provinceId)
    {
        var dto = (CreateCityDto)Activator.CreateInstance(typeof(CreateCityDto), nonPublic: true)!;
        typeof(CreateCityDto).GetProperty("CityName")?.SetValue(dto, cityName);
        typeof(CreateCityDto).GetProperty("ProvinceId")?.SetValue(dto, provinceId);
        return dto;
    }

    private static UpdateCityDto UpdateDto(int cityId, string cityName, int provinceId)
    {
        var dto = (UpdateCityDto)Activator.CreateInstance(typeof(UpdateCityDto), nonPublic: true)!;
        typeof(UpdateCityDto).GetProperty("CityId")?.SetValue(dto, cityId);
        typeof(UpdateCityDto).GetProperty("CityName")?.SetValue(dto, cityName);
        typeof(UpdateCityDto).GetProperty("ProvinceId")?.SetValue(dto, provinceId);
        return dto;
    }

    private async Task<Province> CreateProvinceAsync()
    {
        var province = new Province { ProvinceName = "Test Province" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();
        return province;
    }

    [Fact]
    public async Task CreateAsync_ValidCity_CreatesCity()
    {
        var province = await CreateProvinceAsync();
        var dto = CreateDto("Shiraz", province.ProvinceId);
        var result = await CityService.CreateAsync(dto);
        result.Should().NotBeNull();
        var cityId = (int)typeof(CityDto).GetProperty("CityId")!.GetValue(result)!;
        cityId.Should().BeGreaterThan(0);
        var fromDb = await DbContext.City.FindAsync(cityId);
        fromDb!.CityName.Should().Be("Shiraz");
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_UpdatesCity()
    {
        var province = await CreateProvinceAsync();
        var city = new City { CityName = "Old", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city); await DbContext.SaveChangesAsync();

        DbContext.ChangeTracker.Clear();
        var dto = UpdateDto(city.CityId, "New", province.ProvinceId);
        var updated = await CityService.UpdateAsync(dto);
        var name = (string)typeof(CityDto).GetProperty("CityName")!.GetValue(updated!)!;
        name.Should().Be("New");
    }

    [Fact]
    public async Task DeleteAsync_ExistingCity_Deletes()
    {
        var province = await CreateProvinceAsync();
        var city = new City { CityName = "ToDelete", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city); await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var result = await CityService.DeleteAsync(city.CityId);
        result.Should().BeTrue();
    }
}