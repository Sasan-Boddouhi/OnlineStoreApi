using Application.Entities;
using Application.Exceptions;
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

    private async Task<int> CreateCityAsync()
    {
        var province = new Province { ProvinceName = "TestProvince" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = "TestCity", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        return city.CityId;
    }

    private CreateAddressDto CreateValidDto(int cityId) => new()
    {
        Plaque = "12",
        Unit = "1",
        PostalCode = "1234567890",
        CityId = cityId,
        RecipientFirstName = "Ali",
        RecipientLastName = "Alavi",
        IsDefault = true
    };

    [Fact]
    public async Task CreateAsync_ValidAddress_CreatesAndReturnsDto()
    {
        // Arrange
        var user = await CreateUserAsync();
        var cityId = await CreateCityAsync();
        var dto = CreateValidDto(cityId);

        // Act
        var result = await AddressService.CreateAsync(user.UserId, dto);

        // Assert
        result.Should().NotBeNull();
        result.Plaque.Should().Be("12");
        result.IsDefault.Should().BeTrue();

        var fromDb = await DbContext.Address.FirstOrDefaultAsync(a => a.UserId == user.UserId);
        fromDb.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsUserAddresses()
    {
        // Arrange
        var user = await CreateUserAsync();
        var cityId = await CreateCityAsync();

        var dto1 = CreateValidDto(cityId);
        dto1.Plaque = "10";
        dto1.IsDefault = true;
        await AddressService.CreateAsync(user.UserId, dto1);

        var dto2 = CreateValidDto(cityId);
        dto2.Plaque = "20";
        dto2.IsDefault = false;
        await AddressService.CreateAsync(user.UserId, dto2);

        // Act
        var addresses = await AddressService.GetAllAsync(user.UserId);

        // Assert
        addresses.Should().HaveCount(2);
        addresses.Should().Contain(a => a.Plaque == "10");
        addresses.Should().Contain(a => a.Plaque == "20");
    }

    [Fact]
    public async Task GetAllAsync_OtherUserAddresses_NotReturned()
    {
        // Arrange
        var user1 = await CreateUserAsync();
        var user2 = await CreateUserAsync();
        var cityId = await CreateCityAsync();

        var dto = CreateValidDto(cityId);
        await AddressService.CreateAsync(user1.UserId, dto);

        // Act
        var addresses = await AddressService.GetAllAsync(user2.UserId);

        // Assert
        addresses.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_ValidUpdate_UpdatesAddress()
    {
        // Arrange
        var user = await CreateUserAsync();
        var cityId = await CreateCityAsync();
        var dto = CreateValidDto(cityId);
        var created = await AddressService.CreateAsync(user.UserId, dto);

        var updateDto = new UpdateAddressDto
        {
            CityId = cityId,
            Plaque = "99",
            Unit = "2",
            PostalCode = "0987654321",
            RecipientFirstName = "Reza",
            RecipientLastName = "Rezai",
            ExtraDescription = "Updated",
            IsDefault = true
        };

        // Act
        var updated = await AddressService.UpdateAsync(user.UserId, created.AddressId, updateDto);

        // Assert
        updated.Should().NotBeNull();
        updated.Plaque.Should().Be("99");
        updated.RecipientFirstName.Should().Be("Reza");
        updated.IsDefault.Should().BeTrue();

        var fromDb = await DbContext.Address.FindAsync(created.AddressId);
        fromDb.Plaque.Should().Be("99");
    }

    [Fact]
    public async Task UpdateAsync_OtherUserAddress_ThrowsForbidden()
    {
        // Arrange
        var user1 = await CreateUserAsync();
        var user2 = await CreateUserAsync();
        var cityId = await CreateCityAsync();

        var dto = CreateValidDto(cityId);
        var created = await AddressService.CreateAsync(user1.UserId, dto);

        var updateDto = new UpdateAddressDto
        {
            CityId = cityId,
            Plaque = "99",
            Unit = "2",
            PostalCode = "0987654321",
            RecipientFirstName = "Reza",
            RecipientLastName = "Rezai",
            IsDefault = true
        };

        // Act & Assert
        var act = async () => await AddressService.UpdateAsync(user2.UserId, created.AddressId, updateDto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*مجاز*");
    }

    [Fact]
    public async Task UpdateAsync_NonExistentAddress_ReturnsNull()
    {
        // Arrange
        var user = await CreateUserAsync();
        var cityId = await CreateCityAsync();

        var updateDto = new UpdateAddressDto
        {
            CityId = cityId,
            Plaque = "99",
            Unit = "2",
            PostalCode = "0987654321",
            RecipientFirstName = "Reza",
            RecipientLastName = "Rezai",
            IsDefault = true
        };

        // Act
        var result = await AddressService.UpdateAsync(user.UserId, 99999, updateDto);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_ValidDelete_DeletesAddress()
    {
        // Arrange
        var user = await CreateUserAsync();
        var cityId = await CreateCityAsync();
        var dto = CreateValidDto(cityId);
        var created = await AddressService.CreateAsync(user.UserId, dto);

        // Act
        var deleted = await AddressService.DeleteAsync(user.UserId, created.AddressId);

        // Assert
        deleted.Should().BeTrue();

        var fromDb = await DbContext.Address.FindAsync(created.AddressId);
        fromDb.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_OtherUserAddress_ThrowsForbidden()
    {
        // Arrange
        var user1 = await CreateUserAsync();
        var user2 = await CreateUserAsync();
        var cityId = await CreateCityAsync();

        var dto = CreateValidDto(cityId);
        var created = await AddressService.CreateAsync(user1.UserId, dto);

        // Act & Assert
        var act = async () => await AddressService.DeleteAsync(user2.UserId, created.AddressId);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*مجاز*");
    }

    [Fact]
    public async Task DeleteAsync_NonExistentAddress_ReturnsFalse()
    {
        // Arrange
        var user = await CreateUserAsync();

        // Act
        var deleted = await AddressService.DeleteAsync(user.UserId, 99999);

        // Assert
        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_MultipleAddresses_OnlyOneDefault()
    {
        // Arrange
        var user = await CreateUserAsync();
        var cityId = await CreateCityAsync();

        var dto1 = CreateValidDto(cityId);
        dto1.Plaque = "10";
        dto1.IsDefault = true;
        await AddressService.CreateAsync(user.UserId, dto1);

        var dto2 = CreateValidDto(cityId);
        dto2.Plaque = "20";
        dto2.IsDefault = true; // This should cause the first to become non-default
        await AddressService.CreateAsync(user.UserId, dto2);

        // Act
        var addresses = await AddressService.GetAllAsync(user.UserId);

        // Assert
        addresses.Should().HaveCount(2);
        addresses.Count(a => a.IsDefault).Should().Be(1);
        addresses.First(a => a.Plaque == "20").IsDefault.Should().BeTrue();
        addresses.First(a => a.Plaque == "10").IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ChangeDefault_SetsCorrectDefault()
    {
        // Arrange
        var user = await CreateUserAsync();
        var cityId = await CreateCityAsync();

        var dto1 = CreateValidDto(cityId);
        dto1.Plaque = "10";
        dto1.IsDefault = true;
        var addr1 = await AddressService.CreateAsync(user.UserId, dto1);

        var dto2 = CreateValidDto(cityId);
        dto2.Plaque = "20";
        dto2.IsDefault = false;
        var addr2 = await AddressService.CreateAsync(user.UserId, dto2);

        // Act - change default to addr2
        var updateDto = new UpdateAddressDto
        {
            CityId = cityId,
            Plaque = "20",
            Unit = "2",
            PostalCode = "0987654321",
            RecipientFirstName = "Reza",
            RecipientLastName = "Rezai",
            IsDefault = true
        };
        await AddressService.UpdateAsync(user.UserId, addr2.AddressId, updateDto);

        // Assert
        var addresses = await AddressService.GetAllAsync(user.UserId);
        addresses.Count(a => a.IsDefault).Should().Be(1);
        addresses.First(a => a.AddressId == addr2.AddressId).IsDefault.Should().BeTrue();
        addresses.First(a => a.AddressId == addr1.AddressId).IsDefault.Should().BeFalse();
    }
}