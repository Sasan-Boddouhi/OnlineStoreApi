using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BusinessLogic.DTOs.Auth;
using BusinessLogic.DTOs.User;
using FluentAssertions;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;

namespace OnlineStore.Tests.Integration.Controllers;

public class UsersControllerIntegrationTests : ControllerIntegrationTestBase
{
    public UsersControllerIntegrationTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    private async Task<string> GetCustomerTokenAsync()
    {
        var phone = $"0912{Random.Shared.Next(0, 10_000_000):D7}";
        var registerData = new
        {
            PhoneNumber = phone,
            Password = "Customer@123",
            FirstName = "Customer",
            LastName = "Test",
            DeviceId = "m5-customer-device",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/auth/register", registerData);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<AuthResultDto>();
        return result!.AccessToken;
    }

    // ============================================================
    // GET /api/v1/users/me
    // ============================================================
    [Fact]
    public async Task GetMyProfile_Authenticated_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/users/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.PhoneNumber.Should().Be("09123456789"); // admin phone from seed
    }

    [Fact]
    public async Task GetMyProfile_Unauthenticated_ReturnsUnauthorized()
    {
        var response = await Client.GetAsync("/api/v1/users/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMyProfile_CustomerToken_ReturnsOk()
    {
        var token = await GetCustomerTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/users/me");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ============================================================
    // GET /api/v1/users
    // ============================================================
    [Fact]
    public async Task GetUsers_CustomerToken_ReturnsForbidden()
    {
        var token = await GetCustomerTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/users");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_ReturnsOk_WithPagination()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/users?pageNumber=1&pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUsers_WithInvalidFilter_ReturnsBadRequest()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/users?filter=invalidField eq 'x'");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ============================================================
    // GET /api/v1/users/{id}
    // ============================================================
    [Fact]
    public async Task GetUserById_Existing_ReturnsOk()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // admin has userId=1 (based on seed)
        var response = await Client.GetAsync("/api/v1/users/1");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var user = await response.Content.ReadFromJsonAsync<UserDto>();
        user.Should().NotBeNull();
        user!.UserId.Should().Be(1);
    }

    [Fact]
    public async Task GetUserById_NonExisting_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/v1/users/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ============================================================
    // POST /api/v1/users
    // ============================================================
    [Fact]
    public async Task CreateUser_ValidData_ReturnsCreated()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateUserDto
        {
            PhoneNumber = "09120001111",
            Password = "Strong@123",
            FirstName = "New",
            LastName = "User",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/users", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<UserDto>();
        created.Should().NotBeNull();
        created!.UserId.Should().BeGreaterThan(0);

        // Verify Location header
        response.Headers.Location!.AbsolutePath.Should().Contain(created.UserId.ToString());
    }

    [Fact]
    public async Task CreateUser_DuplicatePhone_ReturnsBadRequest()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var dto = new CreateUserDto
        {
            PhoneNumber = "09123456789", // already exists (admin)
            Password = "Strong@123",
            FirstName = "Dup",
            LastName = "User",
            DateOfBirth = "1370/01/01"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/users", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ============================================================
    // PUT /api/v1/users/{id}
    // ============================================================
    [Fact]
    public async Task UpdateUser_ValidData_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create a new user first
        var createDto = new CreateUserDto
        {
            PhoneNumber = "09120002222",
            Password = "Strong@123",
            FirstName = "Before",
            LastName = "Update",
            DateOfBirth = "1370/01/01"
        };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/users", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<UserDto>();

        // Update that user
        var updateDto = new UpdateUserDto
        {
            UserId = created!.UserId,
            PhoneNumber = "09120002222",
            FirstName = "After",
            LastName = "Update"
        };

        var response = await Client.PutAsJsonAsync($"/api/v1/users/{created.UserId}", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateUser_IdMismatch_ReturnsUnprocessableEntity()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var updateDto = new UpdateUserDto { UserId = 5 };
        var response = await Client.PutAsJsonAsync("/api/v1/users/10", updateDto);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // ============================================================
    // PATCH /api/v1/users/{id}
    // ============================================================
    [Fact]
    public async Task PatchUserStatus_ValidData_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var patchDto = new { userId = 1, isActive = false };
        var response = await Client.PatchAsJsonAsync("/api/v1/users/1", patchDto);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Restore active status for other tests
        var restoreDto = new { userId = 1, isActive = true };
        await Client.PatchAsJsonAsync("/api/v1/users/1", restoreDto);
    }

    // ============================================================
    // DELETE /api/v1/users/{id}
    // ============================================================
    [Fact]
    public async Task DeleteUser_Existing_ReturnsNoContent()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Create a user to delete
        var createDto = new CreateUserDto
        {
            PhoneNumber = "09120003333",
            Password = "Strong@123",
            FirstName = "To",
            LastName = "Delete",
            DateOfBirth = "1370/01/01"
        };
        var createResponse = await Client.PostAsJsonAsync("/api/v1/users", createDto);
        var created = await createResponse.Content.ReadFromJsonAsync<UserDto>();

        var response = await Client.DeleteAsync($"/api/v1/users/{created!.UserId}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteUser_NonExisting_ReturnsNotFound()
    {
        var token = await GetAdminTokenAsync();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.DeleteAsync("/api/v1/users/99999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}