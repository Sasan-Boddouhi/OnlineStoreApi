using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessLogic.DTOs.Product;
using BusinessLogic.DTOs.Shared;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class ProductsControllerIntegrationTests : ControllerIntegrationTestBase
{
    public ProductsControllerIntegrationTests(IntegrationTestFactory<Program> factory)
        : base(factory) { }

    // ============================================================
    // POST /api/products
    // ============================================================
    [Fact]
    public async Task CreateProduct_Valid_ReturnsCreated()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateProductDto
        {
            Name = "محصول تست",
            Price = 150000,
            SubcategoryId = 1 // Seed این ID را ایجاد می‌کند
        };

        // Act
        var response = await Client.PostAsJsonAsync("/api/products", dto);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product.Should().NotBeNull();
        product!.Name.Should().Be(dto.Name);

        // Check Location header points to created resource
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.AbsolutePath.Should().Contain(product.ProductId.ToString());
    }

    [Fact]
    public async Task CreateProduct_Unauthorized_Returns401()
    {
        var dto = new CreateProductDto
        {
            Name = "محصول بدون احراز",
            Price = 100,
            SubcategoryId = 1
        };

        var response = await Client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateProduct_InvalidSubcategory_ReturnsBadRequest()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateProductDto
        {
            Name = "Invalid Sub",
            Price = 10,
            SubcategoryId = 9999
        };

        var response = await Client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_EmptyName_ReturnsValidationError()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateProductDto
        {
            Name = "",
            Price = 10,
            SubcategoryId = 1
        };

        var response = await Client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity); // ۴۲۲
    }

    // ============================================================
    // GET /api/products
    // ============================================================
    [Fact]
    public async Task GetProducts_ReturnsOk_WithPagination()
    {
        // ابتدا چند محصول اضافه کن
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        for (int i = 1; i <= 3; i++)
        {
            await Client.PostAsJsonAsync("/api/products", new CreateProductDto
            {
                Name = $"List Product {i}",
                Price = i * 10,
                SubcategoryId = 1
            });
        }

        Client.DefaultRequestHeaders.Authorization = null; // GET anonymous

        var response = await Client.GetAsync("/api/products?pageNumber=1&pageSize=2");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Count().Should().BeLessThanOrEqualTo(2);
        result.TotalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetProducts_WithInvalidFilter_ReturnsBadRequest()
    {
        var response = await Client.GetAsync("/api/products?filter=invalidField eq 'x'");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ============================================================
    // GET /api/products/{id}
    // ============================================================
    [Fact]
    public async Task GetProduct_ExistingId_ReturnsOk()
    {
        // ابتدا یک محصول اضافه کن
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createdResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductDto
        {
            Name = "Single Product",
            Price = 20,
            SubcategoryId = 1
        });
        var created = await createdResponse.Content.ReadFromJsonAsync<ProductDto>();

        Client.DefaultRequestHeaders.Authorization = null;

        var response = await Client.GetAsync($"/api/products/{created!.ProductId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product!.Name.Should().Be("Single Product");
    }

    [Fact]
    public async Task GetProduct_NonExistingId_ReturnsNotFound()
    {
        var response = await Client.GetAsync("/api/products/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================
    // PUT /api/products/{id}
    // ============================================================
    [Fact]
    public async Task UpdateProduct_Valid_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createdResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductDto
        {
            Name = "Before Update",
            Price = 50,
            SubcategoryId = 1
        });
        var created = await createdResponse.Content.ReadFromJsonAsync<ProductDto>();

        var updateDto = new UpdateProductDto
        {
            ProductId = created!.ProductId,
            Name = "After Update",
            Price = 75,
            SubcategoryId = 1
        };

        var response = await Client.PutAsJsonAsync($"/api/products/{created.ProductId}", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ProductDto>();
        updated!.Name.Should().Be("After Update");
        updated.Price.Should().Be(75);
    }

    [Fact]
    public async Task UpdateProduct_IdMismatch_ReturnsBadRequest()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var updateDto = new UpdateProductDto
        {
            ProductId = 5,
            Name = "x",
            Price = 1,
            SubcategoryId = 1
        };

        var response = await Client.PutAsJsonAsync("/api/products/10", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProduct_Unauthorized_Returns401()
    {
        var updateDto = new UpdateProductDto
        {
            ProductId = 1,
            Name = "x",
            Price = 1,
            SubcategoryId = 1
        };
        var response = await Client.PutAsJsonAsync("/api/products/1", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ============================================================
    // DELETE /api/products/{id}
    // ============================================================
    [Fact]
    public async Task DeleteProduct_Existing_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var createdResponse = await Client.PostAsJsonAsync("/api/products", new CreateProductDto
        {
            Name = "To Delete",
            Price = 1,
            SubcategoryId = 1
        });
        var created = await createdResponse.Content.ReadFromJsonAsync<ProductDto>();

        var response = await Client.DeleteAsync($"/api/products/{created!.ProductId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm soft‑deleted (با GET)
        Client.DefaultRequestHeaders.Authorization = null;
        var getResponse = await Client.GetAsync($"/api/products/{created.ProductId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProduct_NonExisting_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync("/api/products/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProduct_Unauthorized_Returns401()
    {
        var response = await Client.DeleteAsync("/api/products/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}