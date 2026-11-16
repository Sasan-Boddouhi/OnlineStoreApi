// OnlineStore.Tests.Unit/Domain/OrderSellerGroupTransitionTests.cs
using Application.Entities;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Domain;

/// <summary>
/// Batch 4 (F-02) — جدول کامل گذارهای <see cref="OrderSellerGroup"/>.
/// هر ترکیب «وضعیت فعلی × عملیات» یک بار سنجیده می‌شود: یا به وضعیت مقصد
/// می‌رسد یا <see cref="InvalidOperationException"/> می‌دهد و وضعیت دست نمی‌خورد.
/// </summary>
public class OrderSellerGroupTransitionTests
{
    public enum GroupAction
    {
        ApplyCommission,
        MarkShipped,
        MarkDelivered,
        Cancel,
        MarkReturned
    }

    private const SellerGroupStatus Rejected = (SellerGroupStatus)(-1);

    /// <summary>جدول مرجع؛ همان جدولی که در گزارش Batch 4 آمده است.</summary>
    private static readonly Dictionary<(SellerGroupStatus From, GroupAction Action), SellerGroupStatus> Table = new()
    {
        [(SellerGroupStatus.Pending, GroupAction.ApplyCommission)] = SellerGroupStatus.Processing,
        [(SellerGroupStatus.Pending, GroupAction.MarkShipped)] = Rejected,
        [(SellerGroupStatus.Pending, GroupAction.MarkDelivered)] = Rejected,
        [(SellerGroupStatus.Pending, GroupAction.Cancel)] = SellerGroupStatus.Cancelled,
        [(SellerGroupStatus.Pending, GroupAction.MarkReturned)] = Rejected,

        [(SellerGroupStatus.Processing, GroupAction.ApplyCommission)] = Rejected,
        [(SellerGroupStatus.Processing, GroupAction.MarkShipped)] = SellerGroupStatus.Shipped,
        [(SellerGroupStatus.Processing, GroupAction.MarkDelivered)] = Rejected,
        [(SellerGroupStatus.Processing, GroupAction.Cancel)] = SellerGroupStatus.Cancelled,
        [(SellerGroupStatus.Processing, GroupAction.MarkReturned)] = Rejected,

        [(SellerGroupStatus.Shipped, GroupAction.ApplyCommission)] = Rejected,
        [(SellerGroupStatus.Shipped, GroupAction.MarkShipped)] = Rejected,
        [(SellerGroupStatus.Shipped, GroupAction.MarkDelivered)] = SellerGroupStatus.Delivered,
        [(SellerGroupStatus.Shipped, GroupAction.Cancel)] = SellerGroupStatus.Cancelled,
        [(SellerGroupStatus.Shipped, GroupAction.MarkReturned)] = Rejected,

        [(SellerGroupStatus.Delivered, GroupAction.ApplyCommission)] = Rejected,
        [(SellerGroupStatus.Delivered, GroupAction.MarkShipped)] = Rejected,
        [(SellerGroupStatus.Delivered, GroupAction.MarkDelivered)] = Rejected,
        [(SellerGroupStatus.Delivered, GroupAction.Cancel)] = Rejected,
        [(SellerGroupStatus.Delivered, GroupAction.MarkReturned)] = SellerGroupStatus.Returned,

        // Cancelled پایانی است؛ لغو دوباره بی‌اثر (idempotent) است، نه خطا.
        [(SellerGroupStatus.Cancelled, GroupAction.ApplyCommission)] = Rejected,
        [(SellerGroupStatus.Cancelled, GroupAction.MarkShipped)] = Rejected,
        [(SellerGroupStatus.Cancelled, GroupAction.MarkDelivered)] = Rejected,
        [(SellerGroupStatus.Cancelled, GroupAction.Cancel)] = SellerGroupStatus.Cancelled,
        [(SellerGroupStatus.Cancelled, GroupAction.MarkReturned)] = Rejected,

        [(SellerGroupStatus.Returned, GroupAction.ApplyCommission)] = Rejected,
        [(SellerGroupStatus.Returned, GroupAction.MarkShipped)] = Rejected,
        [(SellerGroupStatus.Returned, GroupAction.MarkDelivered)] = Rejected,
        [(SellerGroupStatus.Returned, GroupAction.Cancel)] = Rejected,
        [(SellerGroupStatus.Returned, GroupAction.MarkReturned)] = Rejected,
    };

    public static IEnumerable<object[]> AllTransitions()
        => Table.Select(kv => new object[] { kv.Key.From, kv.Key.Action, kv.Value });

    [Fact]
    public void Table_CoversEveryStatusAndAction()
    {
        var expected = Enum.GetValues<SellerGroupStatus>().Length * Enum.GetValues<GroupAction>().Length;
        Table.Should().HaveCount(expected);
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void Transition_FollowsTable(SellerGroupStatus from, GroupAction action, SellerGroupStatus expected)
    {
        var group = GroupIn(from);

        var act = () => Apply(group, action);

        if (expected == Rejected)
        {
            act.Should().Throw<InvalidOperationException>();
            group.Status.Should().Be(from, "a rejected transition must not change the state");
        }
        else
        {
            act.Should().NotThrow();
            group.Status.Should().Be(expected);
        }
    }

    [Fact]
    public void Status_HasNoPublicSetter()
    {
        // بدون این، هر کدی می‌توانست ماشین حالت را دور بزند.
        var setter = typeof(OrderSellerGroup).GetProperty(nameof(OrderSellerGroup.Status))!.SetMethod;

        (setter is null || !setter.IsPublic).Should().BeTrue();
    }

    [Fact]
    public void MarkDelivered_FromShipped_SetsDeliveredAt()
    {
        var group = GroupIn(SellerGroupStatus.Shipped);
        var at = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

        group.MarkDelivered(at);

        group.DeliveredAt.Should().Be(at);
    }

    [Fact]
    public void RejectedShip_DoesNotWriteTrackingData()
    {
        var group = GroupIn(SellerGroupStatus.Pending);

        var act = () => group.MarkShipped("Post", "TRK-1", DateTime.UtcNow);

        act.Should().Throw<InvalidOperationException>();
        group.TrackingNumber.Should().BeNull();
        group.CarrierName.Should().BeNull();
        group.ShippedAtUtc.Should().BeNull();
    }

    // ---------------- ابزار ----------------

    private static void Apply(OrderSellerGroup group, GroupAction action)
    {
        switch (action)
        {
            case GroupAction.ApplyCommission: group.ApplyCommission(); break;
            case GroupAction.MarkShipped: group.MarkShipped("Post", "TRK", DateTime.UtcNow); break;
            case GroupAction.MarkDelivered: group.MarkDelivered(DateTime.UtcNow); break;
            case GroupAction.Cancel: group.Cancel(); break;
            case GroupAction.MarkReturned: group.MarkReturned(); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    /// <summary>گروهی می‌سازد که فقط با گذارهای مجاز به وضعیت خواسته‌شده رسیده است.</summary>
    private static OrderSellerGroup GroupIn(SellerGroupStatus status)
    {
        var group = new OrderSellerGroup(sellerId: 1, commissionRate: 10m);

        switch (status)
        {
            case SellerGroupStatus.Pending:
                break;
            case SellerGroupStatus.Processing:
                group.ApplyCommission();
                break;
            case SellerGroupStatus.Shipped:
                group.ApplyCommission();
                group.MarkShipped("Post", "TRK", DateTime.UtcNow);
                break;
            case SellerGroupStatus.Delivered:
                group.ApplyCommission();
                group.MarkShipped("Post", "TRK", DateTime.UtcNow);
                group.MarkDelivered(DateTime.UtcNow);
                break;
            case SellerGroupStatus.Cancelled:
                group.Cancel();
                break;
            case SellerGroupStatus.Returned:
                group.ApplyCommission();
                group.MarkShipped("Post", "TRK", DateTime.UtcNow);
                group.MarkDelivered(DateTime.UtcNow);
                group.MarkReturned();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        group.Status.Should().Be(status, "the fixture must reach the requested state");
        return group;
    }
}
