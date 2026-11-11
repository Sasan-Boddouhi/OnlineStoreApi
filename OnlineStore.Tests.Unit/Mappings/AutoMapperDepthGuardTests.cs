using AutoMapper;
using AutoMapper.Internal;
using BusinessLogic.Extensions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OnlineStore.Tests.Unit.Mappings;

/// <summary>
/// Batch 40 (ARC-05، GHSA-rvv3-g6hj-g44x): AutoMapper 14 برای نوع‌های خودارجاع سقف عمق ندارد و گراف
/// خیلی عمیق با StackOverflow کل فرایند را می‌کشد. ثبت AutoMapper در برنامه باید برای هر نگاشتی که
/// مقصدش (مستقیم یا از راه مجموعه‌ها) به خودش می‌رسد، سقف عمق بگذارد — همان کاری که نسخه‌های اصلاح‌شده می‌کنند.
/// </summary>
public class AutoMapperDepthGuardTests
{
    public sealed class Node
    {
        public int Value { get; set; }
        public Node? Child { get; set; }
        public List<Node> Children { get; set; } = new();
    }

    public sealed class NodeDto
    {
        public int Value { get; set; }
        public NodeDto? Child { get; set; }
        public List<NodeDto> Children { get; set; } = new();
    }

    public sealed class FlatSource
    {
        public int Value { get; set; }
    }

    public sealed class FlatDto
    {
        public int Value { get; set; }
    }

    /// <summary>مقصد تخت برای مبدأ خودارجاع (مثل entity → DTO تخت)؛ عمقش محدود است.</summary>
    public sealed class NodeSummaryDto
    {
        public int Value { get; set; }
    }

    private static IMapper BuildApplicationMapper()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // همان ثبتی که برنامه انجام می‌دهد، به‌علاوهٔ یک Profile آزمایشی (نگاشت‌های برنامه هم در Profile هستند).
        services.AddBusinessLogicServices();
        services.AddAutoMapper(cfg => cfg.AddProfile<TestProfile>());

        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }

    private sealed class TestProfile : Profile
    {
        public TestProfile()
        {
            CreateMap<Node, NodeDto>();
            CreateMap<FlatSource, FlatDto>();
            CreateMap<Node, NodeSummaryDto>();
        }
    }

    private static Node Chain(int depth)
    {
        var root = new Node { Value = 1 };
        var current = root;
        for (var i = 2; i <= depth; i++)
        {
            current.Child = new Node { Value = i };
            current = current.Child;
        }

        return root;
    }

    private static int Depth(NodeDto? node)
    {
        var depth = 0;
        for (; node != null; node = node.Child) depth++;
        return depth;
    }

    [Fact]
    public void SelfReferentialMap_IsCappedAtTheDepthGuard()
    {
        var mapper = BuildApplicationMapper();

        var result = mapper.Map<NodeDto>(Chain(500));

        Depth(result).Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(AutoMapperDepthGuard.MaxDepth);
    }

    [Fact]
    public void ShallowSelfReferentialGraph_IsMappedCompletely()
    {
        var mapper = BuildApplicationMapper();

        var result = mapper.Map<NodeDto>(Chain(10));

        Depth(result).Should().Be(10);
    }

    [Fact]
    public void OnlyRecursiveMapsGetADepthLimit()
    {
        var mapper = BuildApplicationMapper();

        var maps = mapper.ConfigurationProvider.Internal().GetAllTypeMaps();

        maps.Single(m => m.DestinationType == typeof(NodeDto)).MaxDepth.Should().Be(AutoMapperDepthGuard.MaxDepth);
        maps.Single(m => m.DestinationType == typeof(FlatDto)).MaxDepth.Should().Be(0);
        maps.Single(m => m.DestinationType == typeof(NodeSummaryDto)).MaxDepth.Should().Be(0);
    }

    [Fact]
    public void ApplicationProfiles_AreUnchanged_BecauseNoneMapsARecursiveGraphOntoARecursiveDto()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBusinessLogicServices();
        services.AddAutoMapper(_ => { }, typeof(Application.Features.Users.CreateUser.CreateUserMappingProfile).Assembly);

        var maps = services.BuildServiceProvider().GetRequiredService<IMapper>()
            .ConfigurationProvider.Internal().GetAllTypeMaps();

        maps.Should().NotBeEmpty();
        maps.Where(m => m.MaxDepth > 0).Select(m => $"{m.SourceType.Name} -> {m.DestinationType.Name}")
            .Should().BeEmpty("نگاشت‌های فعلی برنامه بازگشتی نیستند؛ سقف عمق فقط شبکهٔ ایمنی برای نگاشت‌های آینده است");
    }
}
