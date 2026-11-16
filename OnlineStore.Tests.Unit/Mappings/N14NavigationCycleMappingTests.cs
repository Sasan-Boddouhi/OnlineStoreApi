using AutoMapper;
using BusinessLogic.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace OnlineStore.Tests.Unit.Mappings;

/// <summary>
/// N14 — گراف موجودیتی با چرخهٔ navigation (Order &lt;-&gt; OrderItem) بدون StackOverflow نگاشت می‌شود
/// و AutoMapperDepthGuard عمق را به MaxDepth محدود می‌کند.
/// </summary>
public class N14NavigationCycleMappingTests
{
    private class Order { public int Id { get; set; } public List<OrderItem> Items { get; set; } = new(); }
    private class OrderItem { public int Id { get; set; } public Order? Order { get; set; } }
    private class OrderDto { public int Id { get; set; } public List<OrderItemDto> Items { get; set; } = new(); }
    private class OrderItemDto { public int Id { get; set; } public OrderDto? Order { get; set; } }

    private class CycleProfile : Profile
    {
        public CycleProfile()
        {
            CreateMap<Order, OrderDto>();
            CreateMap<OrderItem, OrderItemDto>();
        }
    }

    private static IMapper Build(bool guard = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAutoMapper(cfg =>
        {
            cfg.AddProfile<CycleProfile>();
            if (guard) AutoMapperDepthGuard.Apply(cfg);
        });
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    [Fact]
    public void OrderOrderItemCycle_MapsWithoutStackOverflow()
    {
        var order = new Order { Id = 1 };
        var item = new OrderItem { Id = 10, Order = order };
        order.Items.Add(item);

        var dto = Build().Map<OrderDto>(order);

        // AutoMapper preserves references for circular maps: a real instance cycle yields a finite DTO graph.
        dto.Id.Should().Be(1);
        dto.Items.Should().ContainSingle().Which.Order.Should().BeSameAs(dto);
    }

    [Fact]
    public void DeepOrderOrderItemChain_IsCappedAtMaxDepth()
    {
        // distinct instances (no repeated reference) alternating Order -> OrderItem -> Order ...
        var root = new Order { Id = 1 };
        var current = root;
        for (var i = 0; i < 500; i++)
        {
            var next = new Order { Id = i + 2 };
            current.Items.Add(new OrderItem { Id = i, Order = next });
            current = next;
        }

        var dto = Build().Map<OrderDto>(root);

        var levels = 0;
        for (var o = dto; o != null; o = o.Items.FirstOrDefault()?.Order) levels++;
        levels.Should().BeGreaterThan(1).And.BeLessThanOrEqualTo(AutoMapperDepthGuard.MaxDepth);
    }

    [Fact]
    public void BothCycleMaps_GetTheGuardDepth()
    {
        var provider = Build().ConfigurationProvider;
        var maps = ((AutoMapper.Internal.IGlobalConfiguration)provider).GetAllTypeMaps();
        maps.Where(m => m.SourceType == typeof(Order) || m.SourceType == typeof(OrderItem))
            .Should().HaveCount(2).And.OnlyContain(m => m.MaxDepth == AutoMapperDepthGuard.MaxDepth);
    }
}
