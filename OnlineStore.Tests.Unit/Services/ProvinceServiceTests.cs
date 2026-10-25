using System.Linq.Expressions;
using System.Reflection;
using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.Province;
using BusinessLogic.DTOs.Shared;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Application.Common.Specifications;

namespace OnlineStore.Tests.Unit.Services;

public class ProvinceServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<ProvinceService>> _loggerMock;
    private readonly ProvinceService _service;
    private readonly Mock<IGenericRepository<Province>> _provinceRepoMock;

    public ProvinceServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<ProvinceService>>();
        _provinceRepoMock = new Mock<IGenericRepository<Province>>();

        _uowMock.Setup(u => u.Repository<Province>()).Returns(_provinceRepoMock.Object);
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        _service = new ProvinceService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    private static Province CreateProvince(int id = 1, string name = "Fars")
        => new() { ProvinceId = id, ProvinceName = name };

    // ---- Reflection helpers for DTOs with private setters ----
    private static CreateProvinceDto CreateCreateDto(string name)
    {
        var dto = (CreateProvinceDto)Activator.CreateInstance(typeof(CreateProvinceDto), nonPublic: true)!;
        typeof(CreateProvinceDto).GetProperty("ProvinceName")?.SetValue(dto, name);
        return dto;
    }

    private static UpdateProvinceDto CreateUpdateDto(int id, string name)
    {
        var dto = (UpdateProvinceDto)Activator.CreateInstance(typeof(UpdateProvinceDto), nonPublic: true)!;
        typeof(UpdateProvinceDto).GetProperty("ProvinceId")?.SetValue(dto, id);
        typeof(UpdateProvinceDto).GetProperty("ProvinceName")?.SetValue(dto, name);
        return dto;
    }

    private static ProvinceDto CreateProvinceDto(int id, string name)
    {
        var dto = (ProvinceDto)Activator.CreateInstance(typeof(ProvinceDto), nonPublic: true)!;
        typeof(ProvinceDto).GetProperty("ProvinceId")?.SetValue(dto, id);
        typeof(ProvinceDto).GetProperty("ProvinceName")?.SetValue(dto, name);
        return dto;
    }

    [Fact]
    public async Task CreateAsync_ValidName_CreatesProvince()
    {
        var dto = CreateCreateDto("Tehran");
        var entity = new Province { ProvinceId = 5, ProvinceName = "Tehran" };
        var resultDto = CreateProvinceDto(5, "Tehran");

        _provinceRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Province, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<Province>(dto)).Returns(entity);
        _provinceRepoMock.Setup(r => r.AddAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1).ReturnsAsync(1);
        _provinceRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Province>>(),
                It.IsAny<Expression<Func<Province, ProvinceDto>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(resultDto);

        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        typeof(ProvinceDto).GetProperty("ProvinceName")?.GetValue(result).Should().Be("Tehran");
    }

    [Fact]
    public async Task CreateAsync_EmptyName_ThrowsBusinessException()
    {
        var dto = CreateCreateDto("");
        Func<Task> act = () => _service.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*خطا در ایجاد استان*");
    }

    [Fact]
    public async Task CreateAsync_DuplicateName_ThrowsBusinessException()
    {
        var dto = CreateCreateDto("Fars");
        _provinceRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Province, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        Func<Task> act = () => _service.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>().WithMessage("*خطا در ایجاد استان*");
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_UpdatesProvince()
    {
        var dto = CreateUpdateDto(1, "NewName");
        var existing = CreateProvince(1, "OldName");
        var updatedDto = CreateProvinceDto(1, "NewName");

        _provinceRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        _provinceRepoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<Province, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _provinceRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Province>>(),
                It.IsAny<Expression<Func<Province, ProvinceDto>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(updatedDto);

        var result = await _service.UpdateAsync(dto);
        result.Should().NotBeNull();
        typeof(ProvinceDto).GetProperty("ProvinceName")?.GetValue(result).Should().Be("NewName");
    }

    [Fact]
    public async Task DeleteAsync_ExistingProvince_ReturnsTrue()
    {
        var entity = CreateProvince(1);
        _provinceRepoMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(entity);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var result = await _service.DeleteAsync(1);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsDto()
    {
        var dto = CreateProvinceDto(1, "Test");
        _provinceRepoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<Province>>(),
                It.IsAny<Expression<Func<Province, ProvinceDto>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        var result = await _service.GetByIdAsync(1);
        result.Should().NotBeNull();
    }
}