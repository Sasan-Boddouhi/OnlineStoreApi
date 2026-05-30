using Application.Common.Queries;
using Application.Entities;
using Application.Exceptions;
using BusinessLogic.DTOs.Product;
using BusinessLogic.Services.Interfaces;
using DataLayer.Context;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Services;

public class ProductServiceIntegrationTests : BaseIntegrationTest
{
    private IProductService ProductService => GetService<IProductService>();
    private AppDbContext DbContext => GetService<AppDbContext>();

    public ProductServiceIntegrationTests(IntegrationTestFactory<Program> factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task CreateProduct_Should_Create_Product_Successfully()
    {
        var db = DbContext;
        var subcategory = db.ProductSubcategory.FirstOrDefault();
        subcategory.Should().NotBeNull();

        var dto = new CreateProductDto
        {
            Name = "Test Product",
            Price = 100,
            SubcategoryId = subcategory!.SubcategoryId
        };

        var result = await ProductService.CreateAsync(dto);

        result.Should().NotBeNull();
        result.Name.Should().Be(dto.Name);
        result.ProductId.Should().BeGreaterThan(0);

        var fromDb = await db.Product.FindAsync(result.ProductId);
        fromDb.Should().NotBeNull();
        fromDb!.Name.Should().Be(dto.Name);
    }

    [Fact]
    public async Task CreateProduct_EmptyName_ShouldThrowException()
    {
        var subcategory = DbContext.ProductSubcategory.First();

        var dto = new CreateProductDto
        {
            Name = "   ",
            Price = 100,
            SubcategoryId = subcategory.SubcategoryId
        };

        Func<Task> act = () => ProductService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*نام محصول*");
    }

    [Fact]
    public async Task CreateProduct_ZeroPrice_ShouldThrowException()
    {
        var subcategory = DbContext.ProductSubcategory.First();

        var dto = new CreateProductDto
        {
            Name = "Zero Price",
            Price = 0,
            SubcategoryId = subcategory.SubcategoryId
        };

        Func<Task> act = () => ProductService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*قیمت*");
    }

    [Fact]
    public async Task CreateProduct_NegativePrice_ShouldThrowException()
    {
        var subcategory = DbContext.ProductSubcategory.First();

        var dto = new CreateProductDto
        {
            Name = "Negative Price",
            Price = -10,
            SubcategoryId = subcategory.SubcategoryId
        };

        Func<Task> act = () => ProductService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*قیمت*");
    }

    [Fact]
    public async Task CreateProduct_InvalidSubcategory_ShouldThrowException()
    {
        var dto = new CreateProductDto
        {
            Name = "No Subcategory",
            Price = 50,
            SubcategoryId = 9999
        };

        Func<Task> act = () => ProductService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*زیردسته*");
    }

    [Fact]
    public async Task CreateProduct_DuplicateName_ShouldThrowException()
    {
        var subcategory = DbContext.ProductSubcategory.First();

        var dto = new CreateProductDto
        {
            Name = "Unique Product",
            Price = 10,
            SubcategoryId = subcategory.SubcategoryId
        };

        await ProductService.CreateAsync(dto);

        Func<Task> act = () => ProductService.CreateAsync(dto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*نام*ثبت شده*");
    }

    [Fact]
    public async Task UpdateProduct_Should_Update_Successfully()
    {
        var subcategory = DbContext.ProductSubcategory.First();
        var created = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = "To Update",
            Price = 50,
            SubcategoryId = subcategory.SubcategoryId
        });

        var updateDto = new UpdateProductDto
        {
            ProductId = created.ProductId,
            Name = "Updated Name",
            Price = 75,
            SubcategoryId = subcategory.SubcategoryId
        };

        var result = await ProductService.UpdateAsync(updateDto);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Name");
        result.Price.Should().Be(75);

        var fromDb = await DbContext.Product.FindAsync(created.ProductId);
        fromDb!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateProduct_NotFound_ShouldReturnNull()
    {
        var updateDto = new UpdateProductDto
        {
            ProductId = 9999,
            Name = "Ghost",
            Price = 1,
            SubcategoryId = 1
        };

        var result = await ProductService.UpdateAsync(updateDto);
        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateProduct_DuplicateName_ShouldThrowException()
    {
        var subcategory = DbContext.ProductSubcategory.First();
        var p1 = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = "P1",
            Price = 10,
            SubcategoryId = subcategory.SubcategoryId
        });
        var p2 = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = "P2",
            Price = 20,
            SubcategoryId = subcategory.SubcategoryId
        });

        var updateDto = new UpdateProductDto
        {
            ProductId = p2.ProductId,
            Name = "P1",
            Price = 30,
            SubcategoryId = subcategory.SubcategoryId
        };

        Func<Task> act = () => ProductService.UpdateAsync(updateDto);
        await act.Should().ThrowAsync<BusinessException>()
            .WithMessage("*نام*ثبت شده*");
    }

    [Fact]
    public async Task DeleteProduct_Should_SetIsActiveFalse()
    {
        var subcategory = DbContext.ProductSubcategory.First();
        var created = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = "To Delete",
            Price = 10,
            SubcategoryId = subcategory.SubcategoryId
        });

        var result = await ProductService.DeleteAsync(created.ProductId);
        result.Should().BeTrue();

        var fromDb = await DbContext.Product.FindAsync(created.ProductId);
        fromDb!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProduct_AlreadyInactive_ShouldReturnFalse()
    {
        var subcategory = DbContext.ProductSubcategory.First();
        var created = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = "Already Gone",
            Price = 5,
            SubcategoryId = subcategory.SubcategoryId
        });

        await ProductService.DeleteAsync(created.ProductId);
        var secondAttempt = await ProductService.DeleteAsync(created.ProductId);
        secondAttempt.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProduct_NonExistent_ShouldReturnFalse()
    {
        var result = await ProductService.DeleteAsync(99999);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetById_ExistingProduct_ShouldReturnProduct()
    {
        var subcategory = DbContext.ProductSubcategory.First();
        var created = await ProductService.CreateAsync(new CreateProductDto
        {
            Name = "Queryable",
            Price = 42,
            SubcategoryId = subcategory.SubcategoryId
        });

        var product = await ProductService.GetByIdAsync(created.ProductId);
        product.Should().NotBeNull();
        product!.Name.Should().Be("Queryable");
    }

    [Fact]
    public async Task GetById_NonExistingProduct_ShouldReturnNull()
    {
        var product = await ProductService.GetByIdAsync(98765);
        product.Should().BeNull();
    }

    [Fact]
    public async Task GetByQuery_ReturnsPagedResult()
    {
        var subcategory = DbContext.ProductSubcategory.First();
        for (int i = 1; i <= 5; i++)
        {
            await ProductService.CreateAsync(new CreateProductDto
            {
                Name = $"Query Product {i}",
                Price = i * 10,
                SubcategoryId = subcategory.SubcategoryId
            });
        }

        var query = new QueryContract<Product>
        {
            Page = 1,
            Size = 3
        };

        var result = await ProductService.GetByQueryAsync(query);
        result.Should().NotBeNull();
        result.Items.Should().NotBeEmpty();
        result.TotalCount.Should().BeGreaterThanOrEqualTo(5);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(3);
        result.Items.Count().Should().BeLessThanOrEqualTo(3);
    }
}