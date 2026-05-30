using System.Reflection;
using Application.Entities;
using BusinessLogic.DTOs.Province;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class ProvinceServiceIntegrationTests : BaseIntegrationTest
{
    private IProvinceService ProvinceService => GetService<IProvinceService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public ProvinceServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private static CreateProvinceDto CreateDto(string name)
    {
        var dto = (CreateProvinceDto)Activator.CreateInstance(typeof(CreateProvinceDto), nonPublic: true)!;
        typeof(CreateProvinceDto).GetProperty("ProvinceName")?.SetValue(dto, name);
        return dto;
    }

    private static UpdateProvinceDto UpdateDto(int id, string name)
    {
        var dto = (UpdateProvinceDto)Activator.CreateInstance(typeof(UpdateProvinceDto), nonPublic: true)!;
        typeof(UpdateProvinceDto).GetProperty("ProvinceId")?.SetValue(dto, id);
        typeof(UpdateProvinceDto).GetProperty("ProvinceName")?.SetValue(dto, name);
        return dto;
    }

    [Fact]
    public async Task CreateAsync_ValidProvince_Creates()
    {
        var dto = CreateDto("Gilan");
        var result = await ProvinceService.CreateAsync(dto);
        result.Should().NotBeNull();
        var id = (int)typeof(ProvinceDto).GetProperty("ProvinceId")!.GetValue(result)!;
        id.Should().BeGreaterThan(0);
        var fromDb = await DbContext.Province.FindAsync(id);
        fromDb!.ProvinceName.Should().Be("Gilan");
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_Updates()
    {
        var province = new Province { ProvinceName = "Old" };
        DbContext.Province.Add(province); await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var dto = UpdateDto(province.ProvinceId, "New");
        var updated = await ProvinceService.UpdateAsync(dto);
        var name = (string)typeof(ProvinceDto).GetProperty("ProvinceName")!.GetValue(updated!)!;
        name.Should().Be("New");
    }

    [Fact]
    public async Task DeleteAsync_Existing_Deletes()
    {
        var province = new Province { ProvinceName = "ToDelete" };
        DbContext.Province.Add(province); await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await ProvinceService.DeleteAsync(province.ProvinceId);
        result.Should().BeTrue();
    }
}