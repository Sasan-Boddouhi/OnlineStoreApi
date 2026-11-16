using System.Net;
using System.Net.Http.Headers;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Application.Entities;
using Application.Options;
using BusinessLogic.DTOs.Auth;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

/// <summary>
/// Batch 11 — طول عمر نشست، استفادهٔ دوباره از refresh token چرخیده، و غیرفعال‌سازی کاربر (F-13، F-19، F-22).
///
/// زمان با <see cref="TestTimeProvider"/> جلو برده می‌شود، نه با انتظار واقعی. اعتبارسنجی امضای JWT
/// در JwtBearer با ساعت واقعی انجام می‌شود، پس جلو بردن ساعت تست فقط سیاست نشست را می‌سنجد.
/// </summary>
public class Phase15Batch11SessionTests : ControllerIntegrationTestBase
{
    private const string Password = "Customer@123";

    public Phase15Batch11SessionTests(IntegrationTestFactory<Program> factory) : base(factory)
    {
        // ساعت در کل کلاس مشترک است؛ هر تست از «اکنون» واقعی شروع می‌کند.
        factory.Clock.SetUtcNow(DateTimeOffset.UtcNow);
    }

    private TestTimeProvider Clock => Factory.Clock;

    // ==================== F-13: طول عمر نشست ====================

    [Fact]
    public async Task Refresh_AfterAccessTokenExpired_Succeeds()
    {
        var auth = await RegisterAndLoginAsync();

        // سناریوی دقیق F-13: کلاینت وقتی access token منقضی شد refresh می‌کند (۶۰ دقیقهٔ قدیم + ۱).
        var accessLifetime = TimeSpan.FromMinutes(Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.ExpireMinutes);
        var wait = TimeSpan.FromMinutes(61);
        wait.Should().BeGreaterThan(accessLifetime);
        Clock.Advance(wait);

        var response = await RefreshAsync(auth.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var refreshed = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        refreshed!.AccessToken.Should().NotBeNullOrEmpty();
        refreshed.RefreshToken.Should().NotBe(auth.RefreshToken);
    }

    [Fact]
    public async Task Refresh_IdleTimeoutIsMeasuredFromLastRefresh_AndRejectsAfterIt()
    {
        var auth = await RegisterAndLoginAsync();

        // ۶ روز بی‌فعالیت: هنوز در بازهٔ ۷ روزه است.
        Clock.Advance(TimeSpan.FromDays(6));
        var first = await RefreshAsync(auth.RefreshToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK, "6 days idle is inside the 7-day idle timeout");
        var latest = (await first.Content.ReadFromJsonAsync<AuthResultDto>())!;

        // ۷ روز و یک دقیقه پس از آخرین refresh: نشست بی‌کار منقضی شده است.
        Clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));
        var second = await RefreshAsync(latest.RefreshToken);

        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SessionStatusAsync(latest.AccessToken)).Should().NotBe(UserSession.SessionStatus.Active);
    }

    [Fact]
    public async Task Refresh_AfterAbsoluteLifetime_IsRejected_EvenWhenRecentlyActive()
    {
        var auth = await RegisterAndLoginAsync();
        var refreshToken = auth.RefreshToken;

        // هر ۶ روز یک refresh: هیچ‌وقت بی‌کار نمی‌ماند.
        for (var day = 6; day <= 24; day += 6)
        {
            Clock.Advance(TimeSpan.FromDays(6));
            var ok = await RefreshAsync(refreshToken);
            ok.StatusCode.Should().Be(HttpStatusCode.OK, $"day {day} is inside both the idle and absolute limits");
            refreshToken = (await ok.Content.ReadFromJsonAsync<AuthResultDto>())!.RefreshToken;
        }

        // روز ۳۰ و یک دقیقه: آخرین فعالیت ۶ روز پیش بود، ولی سقف مطلق ۳۰ روز گذشته است.
        Clock.Advance(TimeSpan.FromDays(6) + TimeSpan.FromMinutes(1));
        var rejected = await RefreshAsync(refreshToken);

        rejected.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ==================== F-19: استفادهٔ دوباره از refresh token چرخیده ====================

    [Fact]
    public async Task Refresh_ReusingRotatedToken_RevokesTheWholeSession()
    {
        var auth = await RegisterAndLoginAsync();

        var rotated = await RefreshAsync(auth.RefreshToken);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await rotated.Content.ReadFromJsonAsync<AuthResultDto>())!;

        // بیرون از بازهٔ تحمل هم‌زمانی: این دیگر رقابت دو تب نیست، نشانهٔ سرقت است.
        Clock.Advance(TimeSpan.FromMinutes(1));
        var replay = await RefreshAsync(auth.RefreshToken);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // کل خانواده باطل شده است: توکن جدیدتر و access token آن هم دیگر کار نمی‌کنند.
        var newer = await RefreshAsync(current.RefreshToken);
        newer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var me = await SendAsync(HttpMethod.Get, "/api/v1/auth/me", current.AccessToken);
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await SessionStatusAsync(current.AccessToken)).Should().Be(UserSession.SessionStatus.Revoked);
    }

    [Fact]
    public async Task Refresh_ReusingRotatedToken_InsideGraceWindow_IsRejected_ButKeepsTheSession()
    {
        // رقابت دو تب هم‌زمان: توکن قدیمی رد می‌شود ولی کاربر بیرون انداخته نمی‌شود.
        var auth = await RegisterAndLoginAsync();

        var rotated = await RefreshAsync(auth.RefreshToken);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await rotated.Content.ReadFromJsonAsync<AuthResultDto>())!;

        var raced = await RefreshAsync(auth.RefreshToken);
        raced.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newer = await RefreshAsync(current.RefreshToken);
        newer.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ==================== F-22: غیرفعال‌سازی کاربر ====================

    [Fact]
    public async Task Deactivate_RevokesAllSessions_AndRejectsExistingAccessToken()
    {
        var phone = RandomPhone();
        var first = await RegisterAsync(phone);
        var second = await LoginAsync(phone, "second-device");
        (await SendAsync(HttpMethod.Get, "/api/v1/auth/me", first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        var adminToken = await GetAdminTokenAsync();
        var patch = await SendAsync(HttpMethod.Patch, $"/api/v1/users/{first.User.UserId}", adminToken,
            new { userId = first.User.UserId, isActive = false });
        patch.StatusCode.Should().Be(HttpStatusCode.NoContent, await patch.Content.ReadAsStringAsync());

        (await SendAsync(HttpMethod.Get, "/api/v1/auth/me", first.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, "/api/v1/auth/me", second.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(second.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await SessionStatusesAsync(first.User.UserId)).Should().HaveCount(2)
            .And.OnlyContain(s => s == UserSession.SessionStatus.Revoked);
        (await ActiveRefreshTokenCountAsync(first.User.UserId)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_DeactivatesInsteadOfDeleting_AndRevokesSessions()
    {
        var auth = await RegisterAndLoginAsync();

        var adminToken = await GetAdminTokenAsync();
        var delete = await SendAsync(HttpMethod.Delete, $"/api/v1/users/{auth.User.UserId}", adminToken);
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent, await delete.Content.ReadAsStringAsync());

        (await SendAsync(HttpMethod.Get, "/api/v1/auth/me", auth.AccessToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var db = GetService<AppDbContext>();
        var user = await db.User.AsNoTracking().SingleOrDefaultAsync(u => u.UserId == auth.User.UserId);
        user.Should().NotBeNull("DELETE users/{id} is a soft delete now; orders and history keep their user");
        user!.IsActive.Should().BeFalse();
        (await SessionStatusesAsync(auth.User.UserId)).Should().OnlyContain(s => s == UserSession.SessionStatus.Revoked);
    }

    // ==================== helpers ====================

    private async Task<AuthResultDto> RegisterAndLoginAsync()
    {
        var phone = RandomPhone();
        await RegisterAsync(phone);
        return await LoginAsync(phone, "b11-device");
    }

    private async Task<AuthResultDto> RegisterAsync(string phone)
    {
        var response = await Client.PostRegisterWithVerifiedPhoneAsync(Factory, new
        {
            PhoneNumber = phone,
            Password,
            FirstName = "Batch",
            LastName = "Eleven",
            DeviceId = "b11-register-device"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    private async Task<AuthResultDto> LoginAsync(string phone, string deviceId)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            PhoneNumber = phone,
            Password,
            DeviceId = deviceId
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken)
        => Client.PostAsJsonAsync("/api/v1/auth/refresh", new { RefreshToken = refreshToken });

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string accessToken, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return Client.SendAsync(request);
    }

    private async Task<List<UserSession.SessionStatus>> SessionStatusesAsync(int userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserSession.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => s.Status)
            .ToListAsync();
    }

    /// <summary>وضعیت همان نشستی که access token به آن تعلق دارد (ثبت‌نام نشست جداگانهٔ خودش را می‌سازد).</summary>
    private async Task<UserSession.SessionStatus> SessionStatusAsync(string accessToken)
    {
        var sessionId = Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.First(c => c.Type == "SessionId").Value);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.UserSession.AsNoTracking().FirstAsync(s => s.Id == sessionId)).Status;
    }

    private async Task<int> ActiveRefreshTokenCountAsync(int userId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.RefreshToken.AsNoTracking().CountAsync(t => t.UserId == userId && !t.IsRevoked);
    }

    private static string RandomPhone() => $"0913{Random.Shared.Next(1000000, 9999999):D7}";
}
