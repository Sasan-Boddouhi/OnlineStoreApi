using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.City;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Application.Common.Specifications;

namespace OnlineStore.Tests.Unit.Services;

public class CityServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<CityService>> _loggerMock;
    private readonly CityService _service;

    private readonly Mock<IGenericRepository<City>> _cityRepoMock;
    private readonly Mock<IGenericRepository<Province>> _provinceRepoMock;

    public CityServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<CityService>>();

        _cityRepoMock = new Mock<IGenericRepository<City>>();
        _provinceRepoMock = new Mock<IGenericRepository<Province>>();

        _uowMock.Setup(u => u.Repository<City>()).Returns(_cityRepoMock.Object);
        _uowMock.Setup(u => u.Repository<Province>()).Returns(_provinceRepoMock.Object);

        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _service = new CityService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    // ---- Reflection helpers ----
    private static CreateCityDto CreateCreateDto(string cityName, int provinceId)
    {
        var dto = (CreateCityDto)Activator.CreateInstance(typeof(CreateCityDto), nonPublic: true)!;
        typeof(CreateCityDto).GetProperty("CityName")?.SetValue(dto, cityName);
        typeof(CreateCityDto).GetProperty("ProvinceId")?.SetValue(dto, provinceId);
        return dto;
    }

    private static UpdateCityDto CreateUpdateDto(int cityId, string cityName, int provinceId)
    {
        var dto = (UpdateCityDto)Activator.CreateInstance(typeof(UpdateCityDto), nonPublic: true)!;
        typeof(UpdateCityDto).GetProperty("CityId")?.SetValue(dto, cityId);
        typeof(UpdateCityDto).GetProperty("CityName")?.SetValue(dto, cityName);
        typeof(UpdateCityDto).GetProperty("ProvinceId")?.SetValue(dto, provinceId);
        return dto;
    }

    private static CityDto CreateCityDto(int cityId, string cityName)
    {
        var dto = (CityDto)Activator.CreateInstance(typeof(CityDto), nonPublic: true)!;
        typeof(CityDto).GetProperty("CityId")?.SetValue(dto, cityId);
        typeof(CityDto).GetProperty("CityName")?.SetValue(dto, cityName);
        return dto;
    }

    private static City CreateCity(int id = 1, string name = "Tehran", int provinceId = 1)
        => new() { CityId = id, CityName = name, ProvinceId = provinceId };

    // ------------- CreateAsync ---------------
    [Fact]
    public async Task CreateAsync_ValidDto_CreatesCity()
    {
        var dto = CreateCreateDto("Shiraz", 1);
        var city = new City { CityId = 5, CityName = "Shiraz", ProvinceId = 1 };
        var cityDto = CreateCityDto(5, "Shiraz");

        _provinceRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Province, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _cityRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<City, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<City>(dto)).Returns(city);
        _cityRepoMock.Setup(r => r.AddAsync(city, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1).ReturnsAsync(1);
        _cityRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<City>>(),
                It.IsAny<Expression<Func<City, CityDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cityDto);

        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        typeof(CityDto).GetProperty("CityName")?.GetValue(result).Should().Be("Shiraz");
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsBusinessException()
    {
        var dto = CreateCreateDto("", 1);
        _provinceRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Province, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Func<Task> act = () => _service.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("خطا در ایجاد شهر");
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsBusinessException()
    {
        var dto = CreateCreateDto("Tehran", 1);
        _provinceRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Province, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _cityRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<City, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Func<Task> act = () => _service.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("خطا در ایجاد شهر");
    }

    // ------------- UpdateAsync ---------------
    [Fact]
    public async Task UpdateAsync_ValidDto_UpdatesCity()
    {
        var dto = CreateUpdateDto(1, "NewName", 1);
        var existing = CreateCity(1, "OldName", 1);
        var updatedDto = CreateCityDto(1, "NewName");

        _cityRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _cityRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<City, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _cityRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<City>>(),
                It.IsAny<Expression<Func<City, CityDto>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedDto);

        var result = await _service.UpdateAsync(dto);
        result.Should().NotBeNull();
        typeof(CityDto).GetProperty("CityName")?.GetValue(result).Should().Be("NewName");
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNull()
    {
        _cityRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((City?)null);
        var result = await _service.UpdateAsync(CreateUpdateDto(999, "x", 1));
        result.Should().BeNull();
    }

    // ------------- DeleteAsync ---------------
    [Fact]
    public async Task DeleteAsync_Exists_ReturnsTrue()
    {
        var city = CreateCity(1);
        _cityRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(city);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var result = await _service.DeleteAsync(1);
        result.Should().BeTrue();
    }

    // ------------- GetByIdAsync ---------------
    [Fact]
    public async Task GetByIdAsync_Exists_ReturnsDto()
    {
        var dto = CreateCityDto(1, "Test");
        _cityRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<City>>(),
                It.IsAny<Expression<Func<City, CityDto>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        var result = await _service.GetByIdAsync(1);
        result.Should().NotBeNull();
        typeof(CityDto).GetProperty("CityId")?.GetValue(result).Should().Be(1);
    }
}