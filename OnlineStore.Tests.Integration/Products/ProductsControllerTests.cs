using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BusinessLogic.DTOs.Product;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using OnlineStore.Tests.Integration.Fixtures;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Application.Entities;
using Microsoft.EntityFrameworkCore;
using BusinessLogic.DTOs.Shared;
using DataLayer.Context;
using OnlineStore.Tests.Integration.Builders;

namespace OnlineStore.Tests.Integration.Products;

[Collection("DatabaseCollection")]
public class ProductsControllerTests : IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private string _adminToken = null!;
    private int _testSubcategoryId;

    public ProductsControllerTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminUser = await db.User.FirstOrDefaultAsync(u => u.PhoneNumber == "09120000000");
        if (adminUser == null)
            throw new Exception("Admin user not seeded. Check TestSeedData.");

        _testSubcategoryId = _fixture.TestSubcategoryId;
        _adminToken = GenerateJwtToken(adminUser.UserId, "Admin", adminUser.SecurityStamp);

        // ایجاد داده‌های اولیه برای تست فیلتر (اگر نیاز باشد)
        if (!await db.Product.AnyAsync(p => p.Price > 500))
        {
            var cheapProduct = new ProductBuilder()
                .WithName("Cheap Product")
                .WithPrice(300)
                .WithSubcategoryId(_testSubcategoryId)
                .Build();
            var expensiveProduct = new ProductBuilder()
                .WithName("Expensive Product")
                .WithPrice(800)
                .WithSubcategoryId(_testSubcategoryId)
                .Build();
            db.Product.AddRange(cheapProduct, expensiveProduct);
            await db.SaveChangesAsync();
        }
    }

    private string GenerateJwtToken(int userId, string role, string securityStamp)
    {
        var key = Encoding.UTF8.GetBytes("TEST_KEY_FOR_INTEGRATION_TESTS_1234567890");
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim("SecurityStamp", securityStamp)
        };
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = "TestIssuer",
            Audience = "TestAudience",
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    public async Task DisposeAsync()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var testProducts = await db.Product
            .Where(p => p.Name.StartsWith("To Delete") ||
                        p.Name == "Test Product" ||
                        p.Name.StartsWith("Unique") ||
                        p.Name == "Cheap Product" ||
                        p.Name == "Expensive Product")
            .ToListAsync();

        db.Product.RemoveRange(testProducts);
        await db.SaveChangesAsync();
    }

    #region Create Product

    [Fact]
    public async Task CreateProduct_WithoutToken_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var dto = new CreateProductDto { Name = "Test", Price = 100, SubcategoryId = 1 };
        var response = await _client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateProduct_WithValidToken_ReturnsCreated()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);
        var dto = new CreateProductDto
        {
            Name = "Test Product",
            Price = 100,
            SubcategoryId = _testSubcategoryId
        };
        var response = await _client.PostAsJsonAsync("/api/products", dto);

        // دیباگ: چاپ وضعیت و محتوا در صورت خطا
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Create failed: {response.StatusCode} - {errorContent}");
        }

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<ProductDto>();
        created.Should().NotBeNull();
        created!.Name.Should().Be(dto.Name);
        created.Price.Should().Be(dto.Price);
    }

    [Fact]
    public async Task CreateProduct_DuplicateName_ReturnsBadRequest()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);
        var uniqueName = $"Duplicate {Guid.NewGuid()}";
        var dto = new CreateProductDto
        {
            Name = uniqueName,
            Price = 100,
            SubcategoryId = _testSubcategoryId
        };
        await _client.PostAsJsonAsync("/api/products", dto); // اولین درخواست موفق
        var response = await _client.PostAsJsonAsync("/api/products", dto); // دومین درخواست تکراری
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadAsStringAsync();
        error.Should().Contain("نام");
    }

    [Fact]
    public async Task CreateProduct_InvalidSubcategoryId_ReturnsBadRequest()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);
        var dto = new CreateProductDto
        {
            Name = "Invalid Subcat",
            Price = 100,
            SubcategoryId = 99999
        };
        var response = await _client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateProduct_PriceZeroOrNegative_ReturnsBadRequest()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);
        var dto = new CreateProductDto
        {
            Name = "Zero Price",
            Price = 0,
            SubcategoryId = _testSubcategoryId
        };
        var response = await _client.PostAsJsonAsync("/api/products", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Get Product By Id

    [Fact]
    public async Task GetProductById_ExistingProduct_ReturnsOk()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

        // استفاده از نام یکتا برای جلوگیری از تداخل
        var uniqueName = $"GetById Test {Guid.NewGuid()}";
        var createDto = new CreateProductDto { Name = uniqueName, Price = 50, SubcategoryId = _testSubcategoryId };
        var createResponse = await _client.PostAsJsonAsync("/api/products", createDto);

        // در صورت خطا، پیام واقعی را بخوانید
        if (!createResponse.IsSuccessStatusCode)
        {
            var errorContent = await createResponse.Content.ReadAsStringAsync();
            throw new Exception($"Create product failed: {createResponse.StatusCode} - {errorContent}");
        }

        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        var productId = created!.ProductId;

        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync($"/api/products/{productId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var product = await response.Content.ReadFromJsonAsync<ProductDto>();
        product.Should().NotBeNull();
        product!.Name.Should().Be(createDto.Name);
    }

    [Fact]
    public async Task GetProductById_NotFound_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/products/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Update Product

    [Fact]
    public async Task UpdateProduct_WithValidData_ReturnsOk()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

        var originalName = $"To Update {Guid.NewGuid()}";
        var createDto = new CreateProductDto { Name = originalName, Price = 100, SubcategoryId = _testSubcategoryId };
        var createResponse = await _client.PostAsJsonAsync("/api/products", createDto);

        // اطمینان از موفقیت ایجاد محصول
        if (!createResponse.IsSuccessStatusCode)
        {
            var errorContent = await createResponse.Content.ReadAsStringAsync();
            throw new Exception($"Create product failed: {createResponse.StatusCode} - {errorContent}");
        }

        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        var productId = created!.ProductId;

        var updatedName = $"Updated Name {Guid.NewGuid()}"; // ✅ نام جدید یکتا
        var updateDto = new UpdateProductDto
        {
            ProductId = productId,
            Name = updatedName,
            Price = 200,
            SubcategoryId = _testSubcategoryId
        };
        var response = await _client.PutAsJsonAsync($"/api/products/{productId}", updateDto);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"Update failed: {response.StatusCode} - {errorBody}");
        }

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<ProductDto>();
        updated.Should().NotBeNull();
        updated!.Name.Should().Be(updatedName);
        updated.Price.Should().Be(200);
    }

    [Fact]
    public async Task UpdateProduct_WithoutToken_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var updateDto = new UpdateProductDto { ProductId = 1, Name = "X", Price = 10, SubcategoryId = 1 };
        var response = await _client.PutAsJsonAsync("/api/products/1", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProduct_DuplicateName_ReturnsBadRequest()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

        var uniqueName1 = $"Unique1_{Guid.NewGuid()}";
        var uniqueName2 = $"Unique2_{Guid.NewGuid()}";

        var createDto1 = new CreateProductDto { Name = uniqueName1, Price = 100, SubcategoryId = _testSubcategoryId };
        var createDto2 = new CreateProductDto { Name = uniqueName2, Price = 100, SubcategoryId = _testSubcategoryId };

        var res1 = await _client.PostAsJsonAsync("/api/products", createDto1);
        var created1 = await res1.Content.ReadFromJsonAsync<ProductDto>();
        var res2 = await _client.PostAsJsonAsync("/api/products", createDto2);
        var created2 = await res2.Content.ReadFromJsonAsync<ProductDto>();

        var updateDto = new UpdateProductDto
        {
            ProductId = created2!.ProductId,
            Name = uniqueName1, // نام تکراری
            Price = 200,
            SubcategoryId = _testSubcategoryId
        };
        var response = await _client.PutAsJsonAsync($"/api/products/{created2.ProductId}", updateDto);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadAsStringAsync();
        error.Should().Contain("نام");
    }

    #endregion

    #region Delete Product

    [Fact]
    public async Task DeleteProduct_WithValidToken_ReturnsNoContent()
    {
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _adminToken);

        // استفاده از نام یکتا برای جلوگیری از تداخل
        var uniqueName = $"To Delete {Guid.NewGuid()}";
        var createDto = new CreateProductDto { Name = uniqueName, Price = 100, SubcategoryId = _testSubcategoryId };
        var createResponse = await _client.PostAsJsonAsync("/api/products", createDto);

        // اطمینان از موفقیت ایجاد محصول
        if (!createResponse.IsSuccessStatusCode)
        {
            var errorContent = await createResponse.Content.ReadAsStringAsync();
            throw new Exception($"Create failed: {createResponse.StatusCode} - {errorContent}");
        }

        var created = await createResponse.Content.ReadFromJsonAsync<ProductDto>();
        var productId = created!.ProductId;

        var deleteResponse = await _client.DeleteAsync($"/api/products/{productId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // بررسی اینکه محصول Soft Delete شده باشد
        var getResponse = await _client.GetAsync($"/api/products/{productId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteProduct_WithoutToken_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.DeleteAsync("/api/products/1");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Query (Filter, Sort, Paging)

    [Fact]
    public async Task GetProducts_FilterByPriceGreaterThan_ReturnsFiltered()
    {
        // داده‌های تست
        _client.DefaultRequestHeaders.Authorization = null;
        // فرض می‌کنیم حداقل دو محصول با قیمت‌های متفاوت وجود دارد
        var response = await _client.GetAsync("/api/products?filter=price gt 500&page=1&size=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().OnlyContain(p => p.Price > 500);
    }

    [Fact]
    public async Task GetProducts_SortByPriceDescending_ReturnsSorted()
    {
        var response = await _client.GetAsync("/api/products?sort=-price&page=1&size=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        var prices = result!.Items.Select(p => p.Price).ToList();
        prices.Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task GetProducts_InvalidFilterSyntax_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/products?filter=price >> 100");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errorContent = await response.Content.ReadAsStringAsync();
        errorContent.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetProducts_Paging_ReturnsCorrectPage()
    {
        var response = await _client.GetAsync("/api/products?pageNumber=2&pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(5);
    }

    #endregion
}