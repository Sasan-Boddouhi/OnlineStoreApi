using Application.Entities;
using BusinessLogic.DTOs.Address;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class AddressServiceIntegrationTests : BaseIntegrationTest
{
    private IAddressService AddressService => GetService<IAddressService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public AddressServiceIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<User> CreateUserAsync()
    {
        var user = new User
        {
            FirstName = "Addr",
            LastName = "Test",
            PhoneNumber = $"0912{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task CreateAsync_ValidAddress_CreatesAndReturnsDto()
    {
        // Arrange
        var user = await CreateUserAsync();

        // ایجاد Province و City (اگر برای کلید خارجی لازم باشد)
        var province = new Province { ProvinceName = "TestProvince" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = "TestCity", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        // مقداردهی کامل DTO با تمام فیلدهای الزامی
        var dto = new CreateAddressDto
        {
            Plaque = "12",
            Unit = "1",
            PostalCode = "1234567890",
            CityId = city.CityId,
            RecipientFirstName = "Ali",
            RecipientLastName = "Alavi",
            IsDefault = true
        };

        // Act
        var result = await AddressService.CreateAsync(user.UserId, dto);

        // Assert
        result.Should().NotBeNull();
        result.Plaque.Should().Be("12");
        result.IsDefault.Should().BeTrue();

        var fromDb = await DbContext.Address.FirstOrDefaultAsync(a => a.UserId == user.UserId);
        fromDb.Should().NotBeNull();
    }
}