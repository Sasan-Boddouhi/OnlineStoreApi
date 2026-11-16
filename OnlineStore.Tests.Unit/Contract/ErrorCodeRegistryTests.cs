using Application.Exceptions;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Contract;

/// <summary>Batch 53 (API-06) — یکتایی و سازگاری رجیستری کد خطا.</summary>
public class ErrorCodeRegistryTests
{
    [Fact]
    public void Registry_HasNoDuplicateCode_SoNoCodeHasTwoStatuses()
    {
        ErrorCodes.All.GroupBy(e => e.Code).Where(g => g.Count() > 1)
            .Select(g => g.Key).Should().BeEmpty();
    }

    [Fact]
    public void Registry_EveryEntryHasUpperSnakeCodeErrorStatusAndPersianMessage()
    {
        foreach (var e in ErrorCodes.All)
        {
            e.Code.Should().MatchRegex("^[A-Z][A-Z0-9_]*$");
            e.Status.Should().BeInRange(400, 599);
            e.Message.Should().MatchRegex("[؀-ۿ]", $"{e.Code} needs a Persian message");
        }
    }

    [Fact]
    public void Registry_EveryConstantIsListedInAll()
    {
        var declared = typeof(ErrorCodes).GetFields()
            .Where(f => f.FieldType == typeof(ErrorCodeInfo))
            .Select(f => (ErrorCodeInfo)f.GetValue(null)!)
            .ToList();

        ErrorCodes.All.Should().BeEquivalentTo(declared);
    }

    [Fact]
    public void TypeUri_IsStableUrnWithoutPlaceholderHost()
    {
        ErrorCodes.TypeUri("TOKEN_EXPIRED").Should().Be("urn:onlinestore:error:token-expired");
    }

    [Fact]
    public void ExceptionBuiltFromRegistryEntry_TakesCodeStatusAndMessage()
    {
        var ex = new RegistryProbeException(ErrorCodes.Forbidden);

        ex.Code.Should().Be("FORBIDDEN");
        ex.StatusCode.Should().Be(403);
        ex.Message.Should().Be(ErrorCodes.Forbidden.Message);
    }

    [Theory]
    [InlineData("PAYOUT_NET_CHANGED")]
    [InlineData("USER_IS_SYSTEM")]
    [InlineData("PLATFORM_WAREHOUSE_EXISTS")]
    [InlineData("PAYOUT_ON_HOLD")]
    public void ServiceConflictCodes_AreRegisteredAs409(string code)
    {
        ErrorCodes.Find(code).Should().NotBeNull();
        ErrorCodes.Find(code)!.Status.Should().Be(409);
    }

    private sealed class RegistryProbeException : AppException
    {
        public RegistryProbeException(ErrorCodeInfo info) : base(info) { }
    }
}
