// OnlineStore.Tests.Unit/Domain/InventoryTests.cs
using Application.Entities;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Domain;

public class InventoryTests
{
    [Fact]
    public void Increase_ValidAmount_AddsToQuantity()
    {
        var inventory = new Inventory(1, 1, 10);
        inventory.Increase(5);
        inventory.Quantity.Should().Be(15);
    }

    [Fact]
    public void Increase_NegativeOrZero_ThrowsException()
    {
        var inventory = new Inventory(1, 1, 10);
        Action act = () => inventory.Increase(-1);
        act.Should().Throw<ArgumentException>();

        act = () => inventory.Increase(0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decrease_ValidAmount_SubtractsFromQuantity()
    {
        var inventory = new Inventory(1, 1, 10);
        inventory.Decrease(3);
        inventory.Quantity.Should().Be(7);
    }

    [Fact]
    public void Decrease_MoreThanStock_ThrowsInvalidOperation()
    {
        var inventory = new Inventory(1, 1, 10);
        Action act = () => inventory.Decrease(20);
        act.Should().Throw<InvalidOperationException>().WithMessage("موجودی کافی نیست.");
    }

    [Fact]
    public void Increase_WhenAmountTooLarge_ThrowsException()
    {
    }
}