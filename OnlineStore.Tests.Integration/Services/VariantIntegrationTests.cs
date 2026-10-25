using Application.Entities;
using Application.Exceptions;
using Application.Interfaces;
using BusinessLogic.DTOs.Cart;
using BusinessLogic.DTOs.Listing;
using BusinessLogic.DTOs.OrderItem;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Services.Interfaces;
using BusinessLogic.Specifications.Products;
using DataLayer.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

/// <summary>
/// Phase 6 — Variants & Attributes.
///
/// سلسله‌مراتب قفل‌شده: Product → ProductVariant → SellerProduct (Listing) → Inventory.
/// </summary>
public class VariantIntegrationTests : BaseIntegrationTest
{
    private IProductService ProductService => GetService<IProductService>();
    private ISellerService SellerService => GetService<ISellerService>();
    private ICartService CartService => GetService<ICartService>();
    private IInventoryService InventoryService => GetService<IInventoryService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public VariantIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private void SetCurrentUser(int userId)
        => ((FakeCurrentUserService)GetService<ICurrentUserService>()).CurrentUserId = userId;

    private async Task<Product> CreateProductAsync(string name = "Galaxy S23")
    {
        var product = new Product { Name = $"{name}-{Guid.NewGuid():N}", Price = 100, SubcategoryId = 1 };
        DbContext.Product.Add(product);
        await DbContext.SaveChangesAsync();
        return product;
    }

    // ==================== Variants ====================

    [Fact]
    public async Task Product_CanHaveMultipleVariants()
    {
        var product = await CreateProductAsync();

        var v1 = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "256GB Black",
            Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var v2 = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "512GB White",
            Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var all = await ProductService.GetVariantsAsync(product.ProductId);

        all.Should().HaveCount(2);
        all.Select(v => v.VariantId).Should().Contain(new[] { v1.VariantId, v2.VariantId });
        all.Should().OnlyContain(v => v.ProductId == product.ProductId);
    }

    [Fact]
    public async Task CreateVariant_DuplicateSku_IsRejected()
    {
        var product = await CreateProductAsync();
        var sku = $"SKU-{Guid.NewGuid():N}";

        await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "A", Sku = sku
        });

        var act = () => ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "B", Sku = sku
        });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("SKU_ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateVariant_WithAttributes_ReturnsNamedAttributeValues()
    {
        var product = await CreateProductAsync();

        var color = await ProductService.CreateAttributeAsync(new CreateProductAttributeDto
        {
            ProductId = product.ProductId, Name = "Color", Type = AttributeType.Color
        });

        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "256GB Black",
            Sku = $"SKU-{Guid.NewGuid():N}",
            Attributes = new List<VariantAttributeInputDto>
            {
                new() { AttributeId = color.AttributeId, Value = "Black" }
            }
        });

        variant.Attributes.Should().HaveCount(1);
        variant.Attributes[0].Name.Should().Be("Color");
        variant.Attributes[0].Type.Should().Be("Color");
        variant.Attributes[0].Value.Should().Be("Black");
    }

    [Fact]
    public async Task CreateVariant_WithAttributeFromAnotherProduct_IsRejected()
    {
        var productA = await CreateProductAsync("A");
        var productB = await CreateProductAsync("B");

        var attributeOfB = await ProductService.CreateAttributeAsync(new CreateProductAttributeDto
        {
            ProductId = productB.ProductId, Name = "Size", Type = AttributeType.Text
        });

        var act = () => ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = productA.ProductId,
            Name = "Cross",
            Attributes = new List<VariantAttributeInputDto>
            {
                new() { AttributeId = attributeOfB.AttributeId, Value = "XL" }
            }
        });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("ATTRIBUTE_NOT_IN_PRODUCT");
    }

    // ==================== Listing → Variant → Product ====================

    [Fact]
    public async Task Listing_BelongsToVariant_AndInheritsProductContext()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();

        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);

        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        listing.VariantId.Should().Be(variant.VariantId);
        // ProductId با عبور از واریانت استخراج می‌شود، نه از خود Listing.
        listing.ProductId.Should().Be(product.ProductId);
    }

    [Fact]
    public async Task CreateListing_ForUnknownVariant_IsRejected()
    {
        var (sellerUser, _) = await CreateCustomerAsync();
        var seller = await CreateSellerAsync(sellerUser.UserId);

        var act = () => SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId, VariantId = 999_999, Price = 10
        });

        var ex = await act.Should().ThrowAsync<BusinessException>();
        ex.And.Code.Should().Be("VARIANT_NOT_FOUND");
    }

    // ==================== Checkout با Listing مبتنی بر واریانت ====================

    [Fact]
    public async Task Checkout_SnapshotsVariantAndResolvesProductIdFromVariant()
    {
        var (buyerUser, buyer) = await CreateCustomerAsync();
        var (sellerUser, _) = await CreateCustomerAsync();

        var product = await CreateProductAsync("Variant Bought");
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 77,
            Status = SellerProductStatus.Active
        });

        await EnsureStockAsync(seller.SellerId, listing.ListingId, buyerUser.UserId);

        SetCurrentUser(buyerUser.UserId);

        await CartService.AddItemAsync(
            buyerUser.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 2 });

        var order = await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Variant Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000"
        });

        var item = await DbContext.OrderItem.AsNoTracking()
            .SingleAsync(oi => oi.OrderId == order.OrderId);

        item.ListingId.Should().Be(listing.ListingId);
        item.VariantId.Should().Be(variant.VariantId);
        // رگرسیون: اگر Variant در کوئری Listing include نشود، این مقدار صفر می‌شود.
        item.ProductId.Should().Be(product.ProductId);
        item.UnitPrice.Should().Be(77);
    }

    // ==================== Projection ====================

    [Fact]
    public void Projection_ExposesVariantsWithAttributes()
    {
        var product = new Product
        {
            ProductId = 1,
            Name = "P",
            Price = 10,
            SubcategoryId = 1,
            IsActive = true,
            Variants = new List<ProductVariant>
            {
                new()
                {
                    VariantId = 5,
                    ProductId = 1,
                    Name = "256GB",
                    IsActive = true,
                    AttributeValues = new List<ProductAttributeValue>
                    {
                        new()
                        {
                            ValueId = 1,
                            VariantId = 5,
                            AttributeId = 3,
                            Value = "Black",
                            Attribute = new ProductAttribute { AttributeId = 3, ProductId = 1, Name = "Color" }
                        }
                    }
                }
            }
        };

        var dto = ProductQueryConfig.Projection.Compile().Invoke(product);

        dto.Variants.Should().HaveCount(1);
        dto.Variants[0].VariantId.Should().Be(5);
        dto.Variants[0].Attributes.Should().HaveCount(1);
        dto.Variants[0].Attributes[0].Name.Should().Be("Color");
        dto.Variants[0].Attributes[0].Value.Should().Be("Black");
    }

    // ==================== Helpers ====================

    private async Task EnsureStockAsync(int sellerId, int listingId, int ownerUserId)
    {
        var province = new Province { ProvinceName = $"P-{Guid.NewGuid():N}" };
        DbContext.Province.Add(province);
        await DbContext.SaveChangesAsync();

        var city = new City { CityName = $"C-{Guid.NewGuid():N}", ProvinceId = province.ProvinceId };
        DbContext.City.Add(city);
        await DbContext.SaveChangesAsync();

        var address = new Address
        {
            CityId = city.CityId,
            Plaque = "1",
            Unit = "1",
            PostalCode = "1234567890",
            RecipientFirstName = "R",
            RecipientLastName = "C",
            UserId = ownerUserId
        };
        DbContext.Address.Add(address);
        await DbContext.SaveChangesAsync();

        var warehouse = new Warehouse
        {
            Name = $"W-{Guid.NewGuid():N}",
            AddressId = address.AddressId,
            SellerId = sellerId
        };
        DbContext.Warehouse.Add(warehouse);
        await DbContext.SaveChangesAsync();

        DbContext.Inventory.Add(new Inventory(listingId, warehouse.WarehouseId, 50));
        await DbContext.SaveChangesAsync();
    }

    private async Task<Seller> CreateSellerAsync(int ownerUserId)
    {
        var seller = new Seller
        {
            UserId = ownerUserId,
            StoreName = $"VShop-{Guid.NewGuid():N}",
            Status = SellerStatus.Approved,
            IsActive = true
        };
        DbContext.Seller.Add(seller);
        await DbContext.SaveChangesAsync();
        return seller;
    }

    private async Task<(User user, Customer customer)> CreateCustomerAsync()
    {
        var user = new User
        {
            FirstName = "Var",
            LastName = "User",
            PhoneNumber = $"0912{new Random().Next(1000000, 9999999)}",
            PasswordHash = "hash",
            UserType = UserType.Customer,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        DbContext.User.Add(user);
        await DbContext.SaveChangesAsync();

        var customer = new Customer { UserId = user.UserId };
        DbContext.Customer.Add(customer);
        await DbContext.SaveChangesAsync();

        return (user, customer);
    }

    // ==================== M7.1: Listing Delete & Price Update ====================

    [Fact]
    public async Task DeleteListingAsync_NoActiveOrders_DeletesListing()
    {
        // Arrange
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Act
        var deleted = await SellerService.DeleteListingAsync(seller.SellerId, listing.ListingId);

        // Assert
        deleted.Should().BeTrue();
        var fromDb = await DbContext.SellerProduct.FindAsync(listing.ListingId);
        fromDb.Should().BeNull();
    }

    [Fact]
    public async Task DeleteListingAsync_HasActiveOrders_MarksInactive()
    {
        // Arrange
        var (sellerUser, _) = await CreateCustomerAsync();
        var (buyerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Create an order with this listing
        await EnsureStockAsync(seller.SellerId, listing.ListingId, buyerUser.UserId);
        SetCurrentUser(buyerUser.UserId);
        await CartService.AddItemAsync(
            buyerUser.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });
        await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000"
        });

        // Act
        var deleted = await SellerService.DeleteListingAsync(seller.SellerId, listing.ListingId);

        // Assert
        deleted.Should().BeTrue();
        var fromDb = await DbContext.SellerProduct.FindAsync(listing.ListingId);
        fromDb.Should().NotBeNull();
        fromDb.Status.Should().Be(SellerProductStatus.Inactive);
    }

    [Fact]
    public async Task DeleteListingAsync_OtherSeller_ThrowsForbidden()
    {
        // Arrange
        var (sellerUser1, _) = await CreateCustomerAsync();
        var (sellerUser2, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller1 = await CreateSellerAsync(sellerUser1.UserId);
        var seller2 = await CreateSellerAsync(sellerUser2.UserId);

        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller1.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Act & Assert
        var act = async () => await SellerService.DeleteListingAsync(seller2.SellerId, listing.ListingId);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*مجاز*");
    }

    [Fact]
    public async Task UpdateListingPriceAsync_ValidPrice_UpdatesPrice()
    {
        // Arrange
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Act
        var updated = await SellerService.UpdateListingPriceAsync(seller.SellerId, listing.ListingId, 120);

        // Assert
        updated.Should().NotBeNull();
        updated.Price.Should().Be(120);

        var fromDb = await DbContext.SellerProduct.FindAsync(listing.ListingId);
        fromDb.Price.Should().Be(120);
    }

    [Fact]
    public async Task UpdateListingPriceAsync_InvalidPrice_ThrowsBusinessException()
    {
        // Arrange
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Act & Assert
        var act = async () => await SellerService.UpdateListingPriceAsync(seller.SellerId, listing.ListingId, 0);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*قیمت*");
    }

    [Fact]
    public async Task UpdateListingPriceAsync_OtherSeller_ThrowsForbidden()
    {
        // Arrange
        var (sellerUser1, _) = await CreateCustomerAsync();
        var (sellerUser2, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "256GB Black", Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller1 = await CreateSellerAsync(sellerUser1.UserId);
        var seller2 = await CreateSellerAsync(sellerUser2.UserId);

        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller1.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Act & Assert
        var act = async () => await SellerService.UpdateListingPriceAsync(seller2.SellerId, listing.ListingId, 120);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*مجاز*");
    }

    // ==================== M7.1: Variant Update & Delete ====================

    [Fact]
    public async Task UpdateVariantAsync_ValidUpdate_UpdatesVariant()
    {
        // Arrange
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "Original",
            Sku = $"SKU-{Guid.NewGuid():N}",
            IsActive = true
        });

        var updateDto = new UpdateProductVariantDto
        {
            VariantId = variant.VariantId,
            Name = "Updated Name",
            Sku = $"SKU-{Guid.NewGuid():N}",
            Barcode = "1234567890123",
            IsActive = false
        };

        // Act
        var updated = await ProductService.UpdateVariantAsync(variant.VariantId, updateDto);

        // Assert
        updated.Should().NotBeNull();
        updated.Name.Should().Be("Updated Name");
        updated.IsActive.Should().BeFalse();
        updated.Barcode.Should().Be("1234567890123");
    }

    [Fact]
    public async Task UpdateVariantAsync_DuplicateSku_ThrowsBusinessException()
    {
        // Arrange
        var product = await CreateProductAsync();
        var sku1 = $"SKU-{Guid.NewGuid():N}";
        var sku2 = $"SKU-{Guid.NewGuid():N}";

        var v1 = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "V1", Sku = sku1
        });

        await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId, Name = "V2", Sku = sku2
        });

        var updateDto = new UpdateProductVariantDto
        {
            VariantId = v1.VariantId,
            Name = "V1 Updated",
            Sku = sku2 // Duplicate SKU
        };

        // Act & Assert
        var act = async () => await ProductService.UpdateVariantAsync(v1.VariantId, updateDto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*SKU*");
    }

    [Fact]
    public async Task UpdateVariantAsync_WithAttributes_UpdatesAttributes()
    {
        // Arrange
        var product = await CreateProductAsync();
        var color = await ProductService.CreateAttributeAsync(new CreateProductAttributeDto
        {
            ProductId = product.ProductId, Name = "Color", Type = AttributeType.Color
        });

        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "256GB",
            Sku = $"SKU-{Guid.NewGuid():N}",
            Attributes = new List<VariantAttributeInputDto>
            {
                new() { AttributeId = color.AttributeId, Value = "Black" }
            }
        });

        var updateDto = new UpdateProductVariantDto
        {
            VariantId = variant.VariantId,
            Name = "256GB Updated",
            Attributes = new List<VariantAttributeInputDto>
            {
                new() { AttributeId = color.AttributeId, Value = "White" }
            }
        };

        // Act
        var updated = await ProductService.UpdateVariantAsync(variant.VariantId, updateDto);

        // Assert
        updated.Should().NotBeNull();
        updated.Attributes.Should().HaveCount(1);
        updated.Attributes[0].Value.Should().Be("White");
    }

    [Fact]
    public async Task DeleteVariantAsync_NoDependencies_DeletesVariant()
    {
        // Arrange
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "ToDelete",
            Sku = $"SKU-{Guid.NewGuid():N}"
        });

        // Act
        var deleted = await ProductService.DeleteVariantAsync(variant.VariantId);

        // Assert
        deleted.Should().BeTrue();
        var fromDb = await DbContext.ProductVariant.FindAsync(variant.VariantId);
        fromDb.Should().BeNull();
    }

    [Fact]
    public async Task DeleteVariantAsync_HasActiveListing_ThrowsBusinessException()
    {
        // Arrange
        var (sellerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "HasListing",
            Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);
        await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Act & Assert
        var act = async () => await ProductService.DeleteVariantAsync(variant.VariantId);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*فعال*");
    }

    [Fact]
    public async Task DeleteVariantAsync_HasOrders_ThrowsBusinessException()
    {
        // Arrange
        var (sellerUser, _) = await CreateCustomerAsync();
        var (buyerUser, _) = await CreateCustomerAsync();
        var product = await CreateProductAsync();
        var variant = await ProductService.CreateVariantAsync(new CreateProductVariantDto
        {
            ProductId = product.ProductId,
            Name = "HasOrders",
            Sku = $"SKU-{Guid.NewGuid():N}"
        });

        var seller = await CreateSellerAsync(sellerUser.UserId);
        var listing = await SellerService.CreateListingAsync(new CreateSellerProductDto
        {
            SellerId = seller.SellerId,
            VariantId = variant.VariantId,
            Price = 90,
            Status = SellerProductStatus.Active
        });

        // Create order with this listing
        await EnsureStockAsync(seller.SellerId, listing.ListingId, buyerUser.UserId);
        SetCurrentUser(buyerUser.UserId);
        await CartService.AddItemAsync(
            buyerUser.UserId, null, new AddCartItemDto { ListingId = listing.ListingId, Quantity = 1 });
        await CartService.CheckoutAsync(buyerUser.UserId, null, new CheckoutDto
        {
            ShippingFullName = "Buyer",
            ShippingAddress = "Tehran",
            ShippingPhoneNumber = "09120000000"
        });

        // Act & Assert
        var act = async () => await ProductService.DeleteVariantAsync(variant.VariantId);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*سفارشات*");
    }
}