using System.Linq.Expressions;
using Application.Entities;
using Application.Interfaces;
using AutoMapper;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Application.Common.Specifications;

namespace OnlineStore.Tests.Unit.Services;

public class EmployeeTypeServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<ILogger<EmployeeTypeService>> _loggerMock;
    private readonly EmployeeTypeService _service;
    private readonly Mock<IGenericRepository<EmployeeType>> _repoMock;

    public EmployeeTypeServiceTests()
    {
        _uowMock = new Mock<IUnitOfWork>();
        _mapperMock = new Mock<IMapper>();
        _loggerMock = new Mock<ILogger<EmployeeTypeService>>();
        _repoMock = new Mock<IGenericRepository<EmployeeType>>();
        _uowMock.Setup(u => u.Repository<EmployeeType>()).Returns(_repoMock.Object);
        _uowMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _service = new EmployeeTypeService(_uowMock.Object, _mapperMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateAsync_ValidDto_Creates()
    {
        var dto = new CreateEmployeeTypeDto { TypeName = "Manager" };
        var entity = new EmployeeType { EmployeeTypeId = 1, TypeName = "Manager", DisplayName = "Manager" };
        var dtoResult = new EmployeeTypeDto { EmployeeTypeId = 1, TypeName = "Manager" };
        _repoMock.Setup(r => r.AnyAsync(It.IsAny<Expression<Func<EmployeeType, bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _mapperMock.Setup(m => m.Map<EmployeeType>(dto)).Returns(entity);
        _repoMock.Setup(r => r.AddAsync(entity, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _uowMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _repoMock.Setup(r => r.FirstOrDefaultAsync(
                It.IsAny<Spec<EmployeeType>>(),
                It.IsAny<Expression<Func<EmployeeType, EmployeeTypeDto>>>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(dtoResult);
        var result = await _service.CreateAsync(dto);
        result.Should().NotBeNull();
        result.TypeName.Should().Be("Manager");
    }
}