using Application.Entities;
using BusinessLogic.DTOs.EmployeeType;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class EmployeeTypeServiceIntegrationTests : BaseIntegrationTest
{
    private IEmployeeTypeService EmployeeTypeService => GetService<IEmployeeTypeService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public EmployeeTypeServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task CreateAsync_Valid_TypeCreated()
    {
        var dto = new CreateEmployeeTypeDto { TypeName = "Tester" };
        var result = await EmployeeTypeService.CreateAsync(dto);
        result.Should().NotBeNull();
        result.EmployeeTypeId.Should().BeGreaterThan(0);
    }
}