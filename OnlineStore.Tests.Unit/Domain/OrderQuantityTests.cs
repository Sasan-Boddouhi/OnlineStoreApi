using System.Globalization;
using Application.Common.Helpers;
using Application.Entities;
using Application.Exceptions;
using FluentAssertions;

namespace OnlineStore.Tests.Unit.Domain;

/// <summary>
/// Batch 45c: قاعدهٔ یگانهٔ مقدار. کالای عددی فقط عدد صحیح ۱ یا بیشتر؛ کالای وزنی/حجمی/طولی بزرگ‌تر از صفر
/// با حداکثر دو رقم اعشار. مقدار نامعتبر 422 <c>QUANTITY_INVALID_FOR_UNIT</c> می‌گیرد و هرگز گرد نمی‌شود.
/// </summary>
public class OrderQuantityTests
{
    [Theory]
    [InlineData(UnitOfMeasurement.Piece, "1", true)]
    [InlineData(UnitOfMeasurement.Piece, "3", true)]
    [InlineData(UnitOfMeasurement.Piece, "2.00", true)]
    [InlineData(UnitOfMeasurement.Piece, "0.5", false)]
    [InlineData(UnitOfMeasurement.Piece, "1.5", false)]
    [InlineData(UnitOfMeasurement.Piece, "0", false)]
    [InlineData(UnitOfMeasurement.Piece, "-1", false)]
    [InlineData(UnitOfMeasurement.Kilogram, "1.25", true)]
    [InlineData(UnitOfMeasurement.Kilogram, "0.01", true)]
    [InlineData(UnitOfMeasurement.Kilogram, "1.250", true)]
    [InlineData(UnitOfMeasurement.Kilogram, "2", true)]
    [InlineData(UnitOfMeasurement.Kilogram, "1.255", false)]
    [InlineData(UnitOfMeasurement.Kilogram, "0.001", false)]
    [InlineData(UnitOfMeasurement.Kilogram, "0", false)]
    [InlineData(UnitOfMeasurement.Kilogram, "-0.5", false)]
    [InlineData(UnitOfMeasurement.Liter, "0.75", true)]
    [InlineData(UnitOfMeasurement.Liter, "0.755", false)]
    [InlineData(UnitOfMeasurement.Meter, "2.5", true)]
    [InlineData(UnitOfMeasurement.gram, "12.34", true)]
    public void IsValid_FollowsTheUnit(UnitOfMeasurement unit, string quantity, bool expected)
        => OrderQuantity.IsValid(unit, decimal.Parse(quantity, CultureInfo.InvariantCulture)).Should().Be(expected);

    [Fact]
    public void EnsureValid_HalfAPhone_Throws422_WithTheRegisteredCode()
    {
        var act = () => OrderQuantity.EnsureValid(UnitOfMeasurement.Piece, 0.5m);

        var ex = act.Should().Throw<UnprocessableEntityException>().Which;
        ex.Code.Should().Be(ErrorCodes.QuantityInvalidForUnit.Code);
        ex.StatusCode.Should().Be(ErrorCodes.QuantityInvalidForUnit.Status).And.Be(422);
        ex.Message.Should().Be(OrderQuantity.CountUnitMessage);
    }

    [Fact]
    public void EnsureValid_ThreeDecimalKilos_Throws_InsteadOfRounding()
    {
        var act = () => OrderQuantity.EnsureValid(UnitOfMeasurement.Kilogram, 1.255m);

        act.Should().Throw<UnprocessableEntityException>().Which.Message.Should().Be(OrderQuantity.TooManyDecimalsMessage);
    }

    [Fact]
    public void OneAndAQuarterKilo_IsValid_AndItsLineTotalIsAWholeRial()
    {
        OrderQuantity.EnsureValid(UnitOfMeasurement.Kilogram, 1.25m);

        RialMoney.LineTotal(1.25m, 12_345m).Should().Be(15_431m);
    }

    [Theory]
    [InlineData("1", null)]
    [InlineData("0.5", null)]
    [InlineData("0", OrderQuantity.NotPositiveMessage)]
    [InlineData("1.255", OrderQuantity.TooManyDecimalsMessage)]
    public void UnitIndependentViolation_NeedsNoUnit(string quantity, string? expected)
        => OrderQuantity.UnitIndependentViolation(decimal.Parse(quantity, CultureInfo.InvariantCulture)).Should().Be(expected);
}
