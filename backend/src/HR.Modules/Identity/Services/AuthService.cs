using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Permissions;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.Entities;
using HR.Modules.Tenancy.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Services;

public class AuthService
{
    private readonly ApplicationDbContext _context;
    private readonly JwtTokenService _jwtTokenService;
    private readonly IConfiguration _configuration;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IEnumerable<ITenantOnboardingHook> _onboardingHooks;

    public AuthService(ApplicationDbContext context, JwtTokenService jwtTokenService,
        IConfiguration configuration, IPermissionResolver permissionResolver,
        IEnumerable<ITenantOnboardingHook> onboardingHooks)
    {
        _context = context;
        _jwtTokenService = jwtTokenService;
        _configuration = configuration;
        _permissionResolver = permissionResolver;
        _onboardingHooks = onboardingHooks;
    }

    public async Task<(string AccessToken, string RefreshToken, User User)> RegisterAsync(
        string companyName, string fullName, string email, string password, CancellationToken ct = default)
    {
        // Check if email already exists (ignoring query filters)
        var exists = await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct);
        if (exists)
            throw new ConflictException("A user with this email already exists.");

        // Create tenant
        var tenant = new Tenant
        {
            CompanyName = companyName,
            CompanyNameAr = companyName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Tenants.Add(tenant);

        // Create admin user
        var user = new User
        {
            TenantId = tenant.Id,
            Email = email,
            PasswordHash = PasswordHasher.Hash(password),
            FullName = fullName,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Users.Add(user);

        // Create default admin role
        var adminRole = new Role
        {
            TenantId = tenant.Id,
            Name = "Admin",
            NameAr = "مدير",
            Description = "Full system access",
            IsSystemRole = true,
            CreatedAt = DateTime.UtcNow
        };
        _context.Roles.Add(adminRole);

        // Assign all permissions to admin role
        var permissions = await _context.Permissions.ToListAsync(ct);
        foreach (var permission in permissions)
        {
            _context.RolePermissions.Add(new RolePermission
            {
                RoleId = adminRole.Id,
                PermissionId = permission.Id
            });
        }

        // Assign admin role to user
        _context.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = adminRole.Id
        });

        await _context.SaveChangesAsync(ct);

        // Let each module provision what a new tenant needs from it — request types, forms,
        // workflows. Runs after the tenant, admin user and permissions are committed, so a hook can
        // safely reference them. Hooks are ordered and self-contained; a failing one logs and does
        // not abort registration, since the account already exists by this point.
        foreach (var hook in _onboardingHooks.OrderBy(h => h.Order))
            await hook.OnTenantCreatedAsync(tenant.Id, user.Id, ct);

        // Generate tokens — resolve effective permissions through the unified resolver.
        var permissionNames = await _permissionResolver.ResolveAsync(user.Id, ct);
        var accessToken = _jwtTokenService.GenerateAccessToken(user, permissionNames);
        var refreshToken = await CreateRefreshTokenAsync(user.Id, ct);

        return (accessToken, refreshToken, user);
    }

    public async Task<(string AccessToken, string RefreshToken, User User)> LoginAsync(
        string email, string password, CancellationToken ct = default)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role).ThenInclude(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .Include(u => u.UserPermissions).ThenInclude(up => up.Permission)
            .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);

        if (user == null || !PasswordHasher.Verify(password, user.PasswordHash))
            throw new NotFoundException("User", email);

        if (!user.IsActive)
            throw new ForbiddenException("Account is deactivated.");

        var permissions = await _permissionResolver.ResolveAsync(user.Id, ct);

        var accessToken = _jwtTokenService.GenerateAccessToken(user, permissions);
        var refreshToken = await CreateRefreshTokenAsync(user.Id, ct);

        user.LastLoginAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return (accessToken, refreshToken, user);
    }

    public async Task<(string AccessToken, string RefreshToken)> RefreshTokenAsync(
        string accessToken, string refreshToken, CancellationToken ct = default)
    {
        var principal = _jwtTokenService.GetPrincipalFromExpiredToken(accessToken);
        if (principal == null)
            throw new ForbiddenException("Invalid token.");

        var userId = Guid.Parse(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Token == refreshToken && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow, ct);

        if (storedToken == null)
            throw new ForbiddenException("Invalid refresh token.");

        storedToken.IsRevoked = true;

        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstAsync(u => u.Id == userId && !u.IsDeleted, ct);

        if (!user.IsActive)
            throw new ForbiddenException("Account is deactivated.");

        var permissions = await _permissionResolver.ResolveAsync(user.Id, ct);

        var newAccessToken = _jwtTokenService.GenerateAccessToken(user, permissions);
        var newRefreshToken = await CreateRefreshTokenAsync(user.Id, ct);

        await _context.SaveChangesAsync(ct);

        return (newAccessToken, newRefreshToken);
    }

    /// <summary>Issue a secure single-use token (reset or invite). Returns the RAW token to email; only
    /// its SHA-256 hash is persisted, with an expiry.</summary>
    public async Task<string> IssueResetTokenAsync(Guid userId, string purpose, TimeSpan ttl, CancellationToken ct = default)
    {
        var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct)
            ?? throw new NotFoundException("User", userId);
        var raw = GenerateUrlToken();
        user.ResetTokenHash = Sha256(raw);
        user.ResetTokenExpiresAt = DateTime.UtcNow.Add(ttl);
        user.ResetTokenPurpose = purpose;
        await _context.SaveChangesAsync(ct);
        return raw;
    }

    /// <summary>Consume a reset/invite token: set the new password, activate the account, clear the token
    /// and revoke any outstanding refresh tokens.</summary>
    public async Task AcceptResetAsync(string rawToken, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(newPassword))
            throw new ConflictException("Token and new password are required.");
        var hash = Sha256(rawToken);
        var user = await _context.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.ResetTokenHash == hash && !u.IsDeleted, ct)
            ?? throw new ForbiddenException("Invalid or expired token.");
        if (user.ResetTokenExpiresAt is null || user.ResetTokenExpiresAt < DateTime.UtcNow)
            throw new ForbiddenException("Invalid or expired token.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.ResetTokenHash = null;
        user.ResetTokenExpiresAt = null;
        user.ResetTokenPurpose = null;
        user.IsActive = true;
        user.Status = HR.Domain.Enums.UserStatus.Active;

        var tokens = await _context.RefreshTokens.Where(t => t.UserId == user.Id && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        await _context.SaveChangesAsync(ct);
    }

    public static string Sha256(string value)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateUrlToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private async Task<string> CreateRefreshTokenAsync(Guid userId, CancellationToken ct)
    {
        var days = int.Parse(_configuration["Jwt:RefreshTokenExpirationDays"] ?? "7");
        var token = new RefreshToken
        {
            UserId = userId,
            Token = _jwtTokenService.GenerateRefreshToken(),
            ExpiresAt = DateTime.UtcNow.AddDays(days)
        };
        _context.RefreshTokens.Add(token);
        await _context.SaveChangesAsync(ct);
        return token.Token;
    }
}
