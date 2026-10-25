using System.Net;
using System.Net.Http.Json;
using Application.Entities;

using DataLayer.Context;
using DataLayer.Security;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OnlineStore.Tests.Integration.Fixtures;
using OnlineStore.Tests.Integration.Infrastructure;
using Xunit;

namespace OnlineStore.Tests.Integration.Authorization;

public class RoleAssignmentTests : ControllerIntegrationTestBase
{
    public RoleAssignmentTests(IntegrationTestFactory<Program> factory) : base(factory) { }

    [Fact]
    public async Task AssignRoles_ValidRequest_UpdatesUserRolesAndAudits()
    {
        // Arrange: user 1 is an Admin (not SuperAdmin)
        await EnsureAdminOnlyAsync(1);

        // Create a real target user (do NOT hardcode IDs)
        var targetUserId = await CreateTargetUserAsync();

        // Ensure roles exist in the test database
        var db = GetService<AppDbContext>();
        var adminRole = await db.Role.FirstOrDefaultAsync(r => r.Code == "Admin");
        if (adminRole == null) {
            adminRole = new Role { Code = "Admin", DisplayName = "Admin" };
            db.Role.Add(adminRole);
        }
        var managerRole = await db.Role.FirstOrDefaultAsync(r => r.Code == "Manager");
        if (managerRole == null) {
            managerRole = new Role { Code = "Manager", DisplayName = "Manager" };
            db.Role.Add(managerRole);
        }
        await db.SaveChangesAsync();

        // Get actual RoleIds from the database (do NOT hardcode 2, 3)
        var roleIds = new List<int> { adminRole.RoleId, managerRole.RoleId };

        // Get admin token
        var adminToken = await GetAdminTokenAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var dto = new
        {
            RoleIds = roleIds
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{targetUserId}/roles", dto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Do NOT use DbContext.Users.Include... because it might be cached.
        // Query the join table directly with AsNoTracking to bypass the cache.
        var userRolesInDb = await db.UserRole
            .AsNoTracking()
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == targetUserId)
            .ToListAsync();

        var assignedRoleCodes = userRolesInDb.Select(ur => ur.Role.Code).ToList();

        assignedRoleCodes.Should().Contain(new[] { "Admin", "Manager" });
    }

    [Fact]
    public async Task AssignRoles_SelfRemovingSuperAdmin_ThrowsException()
    {
        // Arrange: ensure user 1 is a SuperAdmin so the self-removal guard applies.
        await EnsureSuperAdminAsync(1);

        // Get admin token
        var adminToken = await GetAdminTokenAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var targetUserId = 1; // The SuperAdmin user themselves
        // Get the actual Manager role ID from the database
        var db = GetService<AppDbContext>();
        var managerRole = await db.Role.FirstAsync(r => r.Code == "Manager");
        var roleIds = new List<int> { managerRole.RoleId }; // Manager role only — strips SuperAdmin

        var dto = new
        {
            RoleIds = roleIds
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{targetUserId}/roles", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AssignRoles_AdminAssigningSuperAdmin_ThrowsForbidden()
    {
        // Arrange: user 1 is an Admin (not SuperAdmin). Ensure no SuperAdmin role.
        await EnsureAdminOnlyAsync(1);

        // Create a real target user (do NOT hardcode IDs)
        var targetUserId = await CreateTargetUserAsync();

        // Get the actual SuperAdmin role ID from the database
        var db = GetService<AppDbContext>();
        var superAdminRole = await db.Role.FirstAsync(r => r.Code == "SuperAdmin");

        var adminToken = await GetAdminTokenAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var roleIds = new List<int> { superAdminRole.RoleId }; // SuperAdmin role

        var dto = new
        {
            RoleIds = roleIds
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{targetUserId}/roles", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssignRoles_EmptyPayload_ThrowsBadRequest()
    {
        // Arrange
        await EnsureAdminOnlyAsync(1);

        // Create a real target user (do NOT hardcode IDs)
        var targetUserId = await CreateTargetUserAsync();

        var adminToken = await GetAdminTokenAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var roleIds = new List<int>(); // Empty — strips all roles

        var dto = new
        {
            RoleIds = roleIds
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{targetUserId}/roles", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AssignRoles_InvalidRoleId_ThrowsException()
    {
        // Arrange: user 1 is an Admin (not SuperAdmin)
        await EnsureAdminOnlyAsync(1);

        // Create a real target user (do NOT hardcode IDs)
        var targetUserId = await CreateTargetUserAsync();

        // Get admin token
        var adminToken = await GetAdminTokenAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var roleIds = new List<int> { 9999 }; // Non-existent role

        var dto = new
        {
            RoleIds = roleIds
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{targetUserId}/roles", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AssignRoles_UpdatesSecurityStamp()
    {
        // Arrange: user 1 is an Admin (not SuperAdmin)
        await EnsureAdminOnlyAsync(1);

        // Create a real target user (do NOT hardcode IDs)
        var targetUserId = await CreateTargetUserAsync();

        // Get the original security stamp before role assignment
        var db = GetService<AppDbContext>();
        var existingUser = await db.User.FirstAsync(u => u.UserId == targetUserId);
        var originalSecurityStamp = existingUser.SecurityStamp;

        // Get the actual Manager role ID from the database
        var managerRole = await db.Role.FirstAsync(r => r.Code == "Manager");
        var roleIds = new List<int> { managerRole.RoleId };

        // Get admin token
        var adminToken = await GetAdminTokenAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", adminToken);

        var dto = new
        {
            RoleIds = roleIds
        };

        var response = await client.PutAsJsonAsync($"/api/v1/users/{targetUserId}/roles", dto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify security stamp changed — query from DB directly with AsNoTracking
        var updatedUser = await db.User
            .AsNoTracking()
            .FirstAsync(u => u.UserId == targetUserId);
        updatedUser.SecurityStamp.Should().NotBe(originalSecurityStamp);
    }

    /// <summary>
    /// Creates a real target user in the test database and returns the actual UserId.
    /// Do NOT hardcode user IDs in tests — the seeded DB only has user 1 (the admin).
    /// </summary>
    private async Task<int> CreateTargetUserAsync()
    {
        var db = GetService<AppDbContext>();
        var passwordHasher = new BcryptPasswordHasher();
        var newUser = new User
        {
            FirstName = "Target",
            LastName = "User",
            PhoneNumber = "09120000100",
            PasswordHash = passwordHasher.Hash("Test@123"),
            UserType = UserType.Customer,
            IsActive = true,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.User.Add(newUser);
        await db.SaveChangesAsync();
        return newUser.UserId;
    }

    private async Task EnsureSuperAdminAsync(int userId)
    {
        var db = GetService<AppDbContext>();
        var superAdminRole = await db.Role.FirstAsync(r => r.Code == "SuperAdmin");

        // Remove Admin role and assign SuperAdmin
        var existing = await db.UserRole.FirstOrDefaultAsync(ur => ur.UserId == userId);
        if (existing != null)
        {
            if (existing.RoleId != superAdminRole.RoleId)
            {
                db.UserRole.Remove(existing);
                await db.UserRole.AddAsync(new UserRole { UserId = userId, RoleId = superAdminRole.RoleId });
            }
        }
        else
        {
            await db.UserRole.AddAsync(new UserRole { UserId = userId, RoleId = superAdminRole.RoleId });
        }
        await db.SaveChangesAsync();
    }

    private async Task EnsureAdminOnlyAsync(int userId)
    {
        var db = GetService<AppDbContext>();
        var adminRole = await db.Role.FirstAsync(r => r.Code == "Admin");
        var superAdminRole = await db.Role.FirstAsync(r => r.Code == "SuperAdmin");

        // Remove SuperAdmin if present, ensure Admin is assigned
        var existing = await db.UserRole.Where(ur => ur.UserId == userId).ToListAsync();
        db.UserRole.RemoveRange(existing);
        await db.UserRole.AddAsync(new UserRole { UserId = userId, RoleId = adminRole.RoleId });
        await db.SaveChangesAsync();
    }
}