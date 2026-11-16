using Application.Entities;
using Application.Exceptions;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Domain;

/// <summary>
/// Batch 7 (F-16) — نقض قواعد دامنه باید <see cref="BusinessRuleViolationException"/> (با کد) باشد
/// تا میان‌افزار آن را 409 کند؛ یک <see cref="InvalidOperationException"/> ساده 500 می‌شد.
/// همچنان زیرکلاس InvalidOperationException است، پس کدهای قدیمی که آن را می‌گیرند دست‌نخورده می‌مانند.
/// </summary>
public class BusinessRuleViolationTests
{
    private static Order OrderIn(OrderStatus status)
    {
        var order = new Order();
        switch (status)
        {
            case OrderStatus.Processing: order.Confirm(); break;
            case OrderStatus.Shipped: order.Confirm(); order.MarkShipped(); break;
            case OrderStatus.Delivered: order.Confirm(); order.MarkDelivered(); break;
            case OrderStatus.Returned: order.Confirm(); order.MarkDelivered(); order.MarkReturned(); break;
        }
        return order;
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    public void Order_Cancel_AfterShipment_ThrowsRuleViolation(OrderStatus status)
    {
        var order = OrderIn(status);

        var act = () => order.Cancel();

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("ORDER_INVALID_STATE");
        act.Should().Throw<InvalidOperationException>("existing catch blocks and tests keep working");
    }

    [Fact]
    public void Order_Confirm_WhenNotPending_ThrowsRuleViolation()
    {
        var order = OrderIn(OrderStatus.Processing);

        var act = () => order.Confirm();

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("ORDER_INVALID_STATE");
    }

    [Fact]
    public void Order_MarkShipped_WhenPending_ThrowsRuleViolation()
    {
        var act = () => new Order().MarkShipped();

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("ORDER_INVALID_STATE");
    }

    [Fact]
    public void Order_MarkReturned_WhenNotDelivered_ThrowsRuleViolation()
    {
        var act = () => OrderIn(OrderStatus.Processing).MarkReturned();

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("ORDER_INVALID_STATE");
    }

    [Fact]
    public void Group_ShipWhenPending_ThrowsRuleViolation()
    {
        var group = new OrderSellerGroup(1, 100m);

        var act = () => group.MarkShipped("Tipax", "TRK", DateTime.UtcNow);

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("GROUP_INVALID_STATE");
    }

    [Fact]
    public void Group_DeliverWhenPending_ThrowsRuleViolation()
    {
        var act = () => new OrderSellerGroup(1, 100m).MarkDelivered(DateTime.UtcNow);

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("GROUP_INVALID_STATE");
    }

    [Fact]
    public void Group_ApplyCommissionTwice_ThrowsRuleViolation()
    {
        var group = new OrderSellerGroup(1, 100m);
        group.ApplyCommission();

        var act = () => group.ApplyCommission();

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("GROUP_INVALID_STATE");
    }

    [Fact]
    public void Group_ReturnWhenNotDelivered_ThrowsRuleViolation()
    {
        var act = () => new OrderSellerGroup(1, 100m).MarkReturned();

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("GROUP_INVALID_STATE");
    }

    [Fact]
    public void Payment_InvalidTransition_ThrowsRuleViolation()
    {
        var payment = new Payment { State = PaymentState.Initiated };

        var act = () => payment.TransitionTo(PaymentState.Succeeded);

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("PAYMENT_INVALID_TRANSITION");
    }

    [Fact]
    public void Payout_MarkPaidTwice_ThrowsRuleViolation()
    {
        var payout = new SellerPayout(1, 1, 100m, DateTime.UtcNow, DateTime.UtcNow);
        payout.MarkPaid("ref", 0m, payout.AvailableAtUtc); // Batch 44: پرداخت با کسر بدهی و زمان صریح

        var act = () => payout.MarkPaid("ref", 0m, payout.AvailableAtUtc); // Batch 44: پرداخت با کسر بدهی و زمان صریح

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("PAYOUT_INVALID_STATE");
    }

    [Fact]
    public void Payout_CancelPaid_ThrowsRuleViolation()
    {
        var payout = new SellerPayout(1, 1, 100m, DateTime.UtcNow, DateTime.UtcNow);
        payout.MarkPaid("ref", 0m, payout.AvailableAtUtc); // Batch 44: پرداخت با کسر بدهی و زمان صریح

        var act = () => payout.Cancel();

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("PAYOUT_INVALID_STATE");
    }

    [Fact]
    public void Inventory_DecreaseBelowZero_ThrowsRuleViolation()
    {
        var inventory = new Inventory(1, 1, 2m);

        var act = () => inventory.Decrease(5m);

        act.Should().Throw<BusinessRuleViolationException>().Which.Code.Should().Be("INSUFFICIENT_STOCK");
    }
}
