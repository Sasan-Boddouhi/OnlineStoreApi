using Application.Interfaces;
using BusinessLogic.Services.Interfaces;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;

namespace OnlineStore.Tests.Integration.Infrastructure;

/// <summary>
/// رگرسیون ثبت وابستگی‌ها (Phase 5).
///
/// ثبت خودکار BusinessLogic بر پایهٔ قرارداد نام‌گذاری XService/IXService است؛
/// هر سرویسی که از این قرارداد پیروی نکند بی‌صدا از DI جا می‌افتد و فقط
/// هنگام اولین درخواست واقعی منفجر می‌شود. این تست همان جا افتادن را می‌گیرد.
/// </summary>
public class ServiceRegistrationTests : BaseIntegrationTest
{
    public ServiceRegistrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Theory]
    [InlineData(typeof(IStorageService))]
    [InlineData(typeof(IProductImageService))]
    [InlineData(typeof(IReviewService))]
    [InlineData(typeof(ICartService))]
    [InlineData(typeof(IOrderService))]
    [InlineData(typeof(IInventoryService))]
    public void Service_IsResolvableFromContainer(Type serviceType)
    {
        var resolved = Services.GetService(serviceType);

        resolved.Should().NotBeNull($"{serviceType.Name} باید در DI ثبت شده باشد");
    }
}