using HR.Application.Common.Interfaces;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>
/// Regression for the tenant-less login bug: admin-created users (POST /api/users,
/// /api/users/from-employee) were persisted with TenantId = Guid.Empty because User is an
/// AuditableEntity, not a TenantEntity, so the SaveChanges tenant-stamper skipped it. The stamper
/// must also stamp User.TenantId from the current tenant when it was left empty — while never
/// overwriting a tenant that was set explicitly (e.g. the seeded admin in AuthService.RegisterAsync).
/// </summary>
public class UserTenantStampingTests
{
    private static readonly Guid CurrentTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => CurrentTenant;
        public string? Email => "admin@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options,
        new FakeUser());

    private static User NewUser() => new()
    {
        Email = $"{Guid.NewGuid():N}@t.local",
        FullName = "New User",
        PasswordHash = "x",
    };

    [Fact]
    public async Task SaveChanges_stamps_current_tenant_on_new_user_left_empty()
    {
        await using var db = Ctx(nameof(SaveChanges_stamps_current_tenant_on_new_user_left_empty));
        var user = NewUser(); // TenantId defaults to Guid.Empty
        db.Users.Add(user);

        await db.SaveChangesAsync();

        var saved = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(CurrentTenant, saved.TenantId);
    }

    [Fact]
    public async Task SaveChanges_does_not_overwrite_an_explicitly_set_user_tenant()
    {
        var explicitTenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
        await using var db = Ctx(nameof(SaveChanges_does_not_overwrite_an_explicitly_set_user_tenant));
        var user = NewUser();
        user.TenantId = explicitTenant; // e.g. AuthService.RegisterAsync sets the new tenant's id
        db.Users.Add(user);

        await db.SaveChangesAsync();

        var saved = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(explicitTenant, saved.TenantId);
    }
}
