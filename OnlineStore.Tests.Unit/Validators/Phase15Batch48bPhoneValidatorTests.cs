using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.User;
using BusinessLogic.Validators.Auth;
using BusinessLogic.Validators.Cart;
using BusinessLogic.Validators.Common;
using BusinessLogic.Validators.Users;
using FluentAssertions;
using Xunit;

namespace OnlineStore.Tests.Unit.Validators;

/// <summary>
/// Batch 48b: the shared Iranian mobile rule accepts every input that can be normalised to 09xxxxxxxxx
/// and rejects everything else. Login, register, checkout and admin create/update all use the same rule.
/// </summary>
public class Phase15Batch48bPhoneValidatorTests
{
    public static TheoryData<string> Accepted => new()
    {
        "09123456789",
        "+989123456789",
        "00989123456789",
        "989123456789",
        "9123456789",
        "0912 345 6789",
        "0912-345-6789",
        "+98 912 345 6789",
        "۰۹۱۲۳۴۵۶۷۸۹",
        "٠٩١٢٣٤٥٦٧٨٩",
        "+۹۸۹۱۲۳۴۵۶۷۸۹",
    };

    public static TheoryData<string> Rejected => new()
    {
        "",
        "02112345678",
        "0912345678",
        "091234567890",
        "09l23456789",
        "+1 912 345 6789",
        "++989123456789",
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void SharedRule_AcceptsNormalisableInput(string phone)
        => PhoneNumber.IsValid(phone).Should().BeTrue();

    [Theory]
    [MemberData(nameof(Rejected))]
    public void SharedRule_RejectsOtherInput(string phone)
        => PhoneNumber.IsValid(phone).Should().BeFalse();

    [Theory]
    [MemberData(nameof(Accepted))]
    public void AllPhoneValidators_AcceptNormalisableInput(string phone)
    {
        Errors(phone).Should().AllSatisfy(e => e.Should().BeEmpty());
    }

    [Theory]
    [MemberData(nameof(Rejected))]
    public void AllPhoneValidators_RejectOtherInput_WithTheSharedMessage(string phone)
    {
        Errors(phone).Should().AllSatisfy(e => e.Should().NotBeEmpty());

        if (phone.Length > 0)
            Errors(phone).Should().AllSatisfy(e => e.Should().Contain(PhoneNumber.ErrorMessage));
    }

    /// <summary>خطاهای فیلد موبایل در هر پنج اعتبارسنج.</summary>
    private static List<List<string>> Errors(string phone)
    {
        static List<string> Of(FluentValidation.Results.ValidationResult r, string property)
            => r.Errors.Where(e => e.PropertyName == property).Select(e => e.ErrorMessage).ToList();

        return new()
        {
            Of(new LoginDtoValidator().Validate(new LoginDto { PhoneNumber = phone, Password = "p", DeviceId = "d" }), nameof(LoginDto.PhoneNumber)),
            Of(new RegisterDtoValidator().Validate(new RegisterDto { FirstName = "a", LastName = "b", PhoneNumber = phone, Password = "123456" }), nameof(RegisterDto.PhoneNumber)),
            Of(new CheckoutDtoValidator().Validate(new CheckoutDto { ShippingFullName = "n", ShippingAddress = "a", ShippingPhoneNumber = phone }), nameof(CheckoutDto.ShippingPhoneNumber)),
            Of(new CreateUserDtoValidator().Validate(new CreateUserDto { FirstName = "a", LastName = "b", PhoneNumber = phone, Password = "123456", DateOfBirth = "1370/01/01" }), nameof(CreateUserDto.PhoneNumber)),
            Of(new UpdateUserDtoValidator().Validate(new UpdateUserDto { UserId = 1, FirstName = "a", LastName = "b", PhoneNumber = phone }), nameof(UpdateUserDto.PhoneNumber)),
        };
    }
}
