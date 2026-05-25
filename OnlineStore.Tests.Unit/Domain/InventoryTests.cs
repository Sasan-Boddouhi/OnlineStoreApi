using Application.Entities;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Domain;

public class InventoryTests
{
    [Fact]
    public void Constructor_WithValidParameters_SetsProperties()
    {
        var inventory = new Inventory(10, 20, 100);
        inventory.ProductId.Should().Be(10);
        inventory.WarehouseId.Should().Be(20);
        inventory.Quantity.Should().Be(100);
        inventory.MinimumStock.Should().Be(0);
        inventory.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WhenInitialQuantityNegative_ThrowsArgumentException()
    {
        Action act = () => new Inventory(1, 2, -5);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Initial quantity cannot be negative.*");
    }

    [Fact]
    public void Increase_WithPositiveAmount_AddsToQuantity()
    {
        var inventory = new Inventory(1, 2, 50);
        inventory.Increase(30);
        inventory.Quantity.Should().Be(80);
    }

    [Fact]
    public void Increase_WithZero_ThrowsArgumentException()
    {
        var inventory = new Inventory(1, 2, 50);
        Action act = () => inventory.Increase(0);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Increase amount must be greater than zero.*");
    }

    [Fact]
    public void Increase_WithNegativeAmount_ThrowsArgumentException()
    {
        var inventory = new Inventory(1, 2, 50);
        Action act = () => inventory.Increase(-10);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Increase amount must be greater than zero.*");
    }

    [Fact]
    public void Decrease_WhenSufficientStock_ReducesQuantity()
    {
        var inventory = new Inventory(1, 2, 100);
        inventory.Decrease(40);
        inventory.Quantity.Should().Be(60);
    }

    [Fact]
    public void Decrease_WhenInsufficientStock_ThrowsInvalidOperationException()
    {
        var inventory = new Inventory(1, 2, 30);
        Action act = () => inventory.Decrease(50);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("موجودی کافی نیست.");
    }

    [Fact]
    public void Decrease_WithZeroAmount_ThrowsArgumentException()
    {
        var inventory = new Inventory(1, 2, 100);
        Action act = () => inventory.Decrease(0);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Decrease amount must be greater than zero.*");
    }

    [Fact]
    public void SetMinimumStock_WithValidValue_SetsMinimumStock()
    {
        var inventory = new Inventory(1, 2, 100);
        inventory.SetMinimumStock(20);
        inventory.MinimumStock.Should().Be(20);
    }

    [Fact]
    public void SetMinimumStock_WithNegativeValue_ThrowsArgumentException()
    {
        var inventory = new Inventory(1, 2, 100);
        Action act = () => inventory.SetMinimumStock(-5);
        act.Should().Throw<ArgumentException>()
            .WithMessage("Minimum stock cannot be negative.*");
    }

    [Fact]
    public void IsLowStock_WhenQuantityEqualsMinimum_ReturnsTrue()
    {
        var inventory = new Inventory(1, 2, 30);
        inventory.SetMinimumStock(30);
        inventory.IsLowStock().Should().BeTrue();
    }

    [Fact]
    public void IsLowStock_WhenQuantityLessThanMinimum_ReturnsTrue()
    {
        var inventory = new Inventory(1, 2, 25);
        inventory.SetMinimumStock(30);
        inventory.IsLowStock().Should().BeTrue();
    }

    [Fact]
    public void IsLowStock_WhenQuantityGreaterThanMinimum_ReturnsFalse()
    {
        var inventory = new Inventory(1, 2, 40);
        inventory.SetMinimumStock(30);
        inventory.IsLowStock().Should().BeFalse();
    }
}