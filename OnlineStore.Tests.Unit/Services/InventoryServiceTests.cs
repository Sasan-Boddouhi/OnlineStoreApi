using Application.Common.Specifications;
using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using Application.Options;
using BusinessLogic.Services.Implementations;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace OnlineStore.Tests.Unit.Services;

public class InventoryServiceTests
{
    private readonly Mock<IUnitOfWork> _uowMock;
    private readonly Mock<IGenericRepository<Inventory>> _inventoryRepoMock;
    private readonly InventoryService _service;

    public InventoryServiceTests() : this(new InventorySettings { EnforceStock = true }) { }

    private InventoryServiceTests(InventorySettings settings)
    {
        _uowMock = new Mock<IUnitOfWork>();
        _inventoryRepoMock = new Mock<IGenericRepository<Inventory>>();

        _uowMock.Setup(u => u.Repository<Inventory>()).Returns(_inventoryRepoMock.Object);

        _service = new InventoryService(
            _uowMock.Object,
            Options.Create(settings),
            new Mock<ICurrentUserService>().Object,
            new Mock<ILogger<InventoryService>>().Object);
    }

    private void SetupRows(params Inventory[] rows)
        => _inventoryRepoMock
            .Setup(r => r.ListAsync(It.IsAny<Spec<Inventory>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

    [Fact]
    public async Task GetAvailableAsync_NoRows_ReturnsZero()
    {
        SetupRows();

        var result = await _service.GetAvailableAsync(1);

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetAvailableAsync_MultipleWarehouses_SumsQuantity()
    {
        SetupRows(
            new Inventory(listingId: 1, warehouseId: 10, initialQuantity: 5),
            new Inventory(listingId: 1, warehouseId: 11, initialQuantity: 7));

        var result = await _service.GetAvailableAsync(1);

        result.Should().Be(12);
    }

    [Fact]
    public async Task ReserveAsync_InsufficientStock_ThrowsBusinessException()
    {
        SetupRows(new Inventory(listingId: 1, warehouseId: 10, initialQuantity: 3));

        var act = () => _service.ReserveAsync(1, 5);

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.Which.Code.Should().Be("INSUFFICIENT_STOCK");
    }

    [Fact]
    public async Task ReserveAsync_NoInventoryRows_ThrowsBusinessException()
    {
        SetupRows();

        var act = () => _service.ReserveAsync(1, 1);

        await act.Should().ThrowAsync<BusinessException>();
    }

    [Fact]
    public async Task ReserveAsync_NonPositiveQuantity_ThrowsBusinessException()
    {
        SetupRows(new Inventory(listingId: 1, warehouseId: 10, initialQuantity: 10));

        var act = () => _service.ReserveAsync(1, 0);

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.Which.Code.Should().Be("INVALID_QUANTITY");
    }

    [Fact]
    public async Task ReserveAsync_GreedyFromLargestWarehouse_DrainsBiggestFirst()
    {
        var small = new Inventory(listingId: 1, warehouseId: 10, initialQuantity: 2);
        var large = new Inventory(listingId: 1, warehouseId: 11, initialQuantity: 10);
        SetupRows(small, large);

        await _service.ReserveAsync(1, 7);

        large.Quantity.Should().Be(3);
        small.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task ReserveAsync_SpansMultipleWarehouses_ConsumesBoth()
    {
        var a = new Inventory(listingId: 1, warehouseId: 10, initialQuantity: 4);
        var b = new Inventory(listingId: 1, warehouseId: 11, initialQuantity: 4);
        SetupRows(a, b);

        await _service.ReserveAsync(1, 6);

        (a.Quantity + b.Quantity).Should().Be(2);
        a.Quantity.Should().Be(0);
        b.Quantity.Should().Be(2);
    }

    [Fact]
    public async Task ReleaseAsync_AddsBackToLargestWarehouse()
    {
        var small = new Inventory(listingId: 1, warehouseId: 10, initialQuantity: 1);
        var large = new Inventory(listingId: 1, warehouseId: 11, initialQuantity: 5);
        SetupRows(small, large);

        await _service.ReleaseAsync(1, 3);

        large.Quantity.Should().Be(8);
        small.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task ReleaseAsync_NoRows_DoesNotThrow()
    {
        SetupRows();

        var act = () => _service.ReleaseAsync(1, 3);

        await act.Should().NotThrowAsync();
    }

    // ---------- Feature flag (تصمیم ناظر: گزینهٔ C) ----------

    private InventoryService CreateService(bool enforceStock)
        => new(
            _uowMock.Object,
            Options.Create(new InventorySettings { EnforceStock = enforceStock }),
            new Mock<ICurrentUserService>().Object,
            new Mock<ILogger<InventoryService>>().Object);

    [Fact]
    public async Task ReserveAsync_NoRows_EnforcementDisabled_TreatsProductAsUnlimited()
    {
        SetupRows();
        var service = CreateService(enforceStock: false);

        var act = () => service.ReserveAsync(1, 5);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReserveAsync_NoRows_EnforcementEnabled_StillThrows()
    {
        SetupRows();
        var service = CreateService(enforceStock: true);

        var act = () => service.ReserveAsync(1, 5);

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.Which.Code.Should().Be("INSUFFICIENT_STOCK");
    }

    [Fact]
    public async Task ReserveAsync_RowsExistButInsufficient_EnforcementDisabled_StillThrows()
    {
        SetupRows(new Inventory(listingId: 1, warehouseId: 10, initialQuantity: 1));
        var service = CreateService(enforceStock: false);

        var act = () => service.ReserveAsync(1, 5);

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.Which.Code.Should().Be("INSUFFICIENT_STOCK");
    }
}