using HR.Api.Controllers;
using HR.Api.Filters;
using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Application.Engines.Permissions;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Identity.DTOs;
using HR.Modules.Identity.Entities;
using HR.Modules.Identity.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Identity.Controllers;

[Authorize]
[Route("api/users")]
public class UsersController : BaseApiController
{
    private const string ManageUsers = "Settings.ManageUsers";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogService _audit;
    private readonly IPermissionResolver _resolver;
    private readonly AuthService _auth;
    private readonly IConfiguration _config;

    public UsersController(ApplicationDbContext db, ICurrentUserService currentUser, IAuditLogService audit,
        IPermissionResolver resolver, AuthService auth, IConfiguration config)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _resolver = resolver;
        _auth = auth;
        _config = config;
    }

    [HttpGet]
    [RequirePermission("Identity.ViewUsers")]
    public async Task<ActionResult<ApiResponse<List<UserListItem>>>> GetAll(CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        var userIds = users.Select(u => u.Id).ToList();
        var links = await _db.Employees.AsNoTracking()
            .Where(e => e.UserId != null && userIds.Contains(e.UserId!.Value))
            .Select(e => new { e.UserId, e.Id, Name = (e.FirstNameAr ?? e.FirstName) + " " + (e.LastNameAr ?? e.LastName), e.EmployeeNumber })
            .ToListAsync(ct);
        var linkByUser = links.GroupBy(l => l.UserId!.Value).ToDictionary(g => g.Key, g => g.First());

        var list = users.Select(u =>
        {
            linkByUser.TryGetValue(u.Id, out var emp);
            return new UserListItem
            {
                Id = u.Id, Email = u.Email, FullName = u.FullName,
                Status = u.Status.ToString(), IsActive = u.IsActive, LastLoginAt = u.LastLoginAt,
                EmployeeId = emp?.Id, EmployeeName = emp?.Name?.Trim(), EmployeeNumber = emp?.EmployeeNumber,
                Roles = u.UserRoles.Select(r => r.Role.NameAr ?? r.Role.Name).ToList(),
            };
        }).ToList();

        return OkResponse(list);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission("Identity.ViewUsers")]
    public async Task<ActionResult<ApiResponse<UserDetail>>> GetById(Guid id, CancellationToken ct)
        => OkResponse(await BuildDetail(id, ct));

    [HttpPost]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse<UserDetail>>> Create([FromBody] CreateUserRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email)) throw new ConflictException("Email is required.");
        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email && !u.IsDeleted, ct))
            throw new ConflictException("A user with this email already exists.");

        var invite = req.SendInvite || string.IsNullOrWhiteSpace(req.Password);
        var user = new User
        {
            Email = email,
            FullName = req.FullName.Trim(),
            Phone = req.Phone,
            PasswordHash = PasswordHasher.Hash(invite ? Guid.NewGuid().ToString("N") : req.Password!),
            IsActive = !invite,
            Status = invite ? UserStatus.Invited : UserStatus.Active,
        };
        _db.Users.Add(user);

        if (req.RoleIds is { Count: > 0 })
            foreach (var rid in req.RoleIds.Distinct())
                _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = rid });

        await _db.SaveChangesAsync(ct);

        if (req.EmployeeId is { } empId) await LinkEmployeeInternalAsync(user.Id, empId, ct);

        await _audit.LogAsync("UserCreated", "Access.User", user.Id, null,
            new { user.Email, user.FullName, Status = user.Status.ToString() }, ct);

        if (invite)
        {
            var link = await IssueAndQueueAsync(user, "Invite", TimeSpan.FromDays(7), ct);
            return CreatedResponse(await BuildDetail(user.Id, ct), link.ResetLink);
        }

        return CreatedResponse(await BuildDetail(user.Id, ct));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse<UserDetail>>> Update(Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        if (req.FullName is not null) user.FullName = req.FullName.Trim();
        if (req.Phone is not null) user.Phone = req.Phone;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UserUpdated", "Access.User", user.Id, null, new { user.FullName, user.Phone }, ct);
        return OkResponse(await BuildDetail(id, ct));
    }

    [HttpPost("{id:guid}/disable")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse>> Disable(Guid id, CancellationToken ct)
    {
        if (id == _currentUser.UserId) throw new ConflictException("You cannot disable your own account.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        user.IsActive = false; user.Status = UserStatus.Suspended;
        await RevokeRefreshTokensAsync(id, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UserDisabled", "Access.User", id, null, null, ct);
        return OkResponse("User disabled.");
    }

    [HttpPost("{id:guid}/enable")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse>> Enable(Guid id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        user.IsActive = true; user.Status = UserStatus.Active;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UserEnabled", "Access.User", id, null, null, ct);
        return OkResponse("User enabled.");
    }

    [HttpPost("{id:guid}/force-logout")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse>> ForceLogout(Guid id, CancellationToken ct)
    {
        await RevokeRefreshTokensAsync(id, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("UserForceLogout", "Access.User", id, null, null, ct);
        return OkResponse("Sessions revoked. The user must sign in again once their current access token expires.");
    }

    [HttpPost("{id:guid}/reset-password")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse<TokenLinkResult>>> ResetPassword(Guid id, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        var link = await IssueAndQueueAsync(user, "Reset", TimeSpan.FromHours(2), ct);
        await _audit.LogAsync("PasswordResetSent", "Access.User", id, null, new { user.Email }, ct);
        return OkResponse(link);
    }

    [HttpPost("{id:guid}/change-email")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse>> ChangeEmail(Guid id, [FromBody] ChangeEmailRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email)) throw new ConflictException("Email is required.");
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email && u.Id != id && !u.IsDeleted, ct))
            throw new ConflictException("Another user already uses this email.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        var old = user.Email;
        user.Email = email;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("EmailChanged", "Access.User", id, new { Email = old }, new { Email = email }, ct);
        return OkResponse("Email updated.");
    }

    /// <summary>Admin sets a user's password directly (enter + confirm on the client). Gated to user-management
    /// (System Administrator); audited with who changed it and when.</summary>
    [HttpPost("{id:guid}/set-password")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse>> SetPassword(Guid id, [FromBody] SetPasswordRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 6)
            throw new ConflictException("كلمة المرور يجب ألا تقل عن 6 أحرف.");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        user.PasswordHash = HR.Modules.Identity.Services.PasswordHasher.Hash(req.Password);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PasswordChangedByAdmin", "Access.User", id,
            null, new { changedByUserId = _currentUser.UserId, user.Email }, ct);
        return OkResponse("تم تغيير كلمة المرور بنجاح.");
    }

    [HttpPut("{id:guid}/roles")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse<UserDetail>>> SetRoles(Guid id, [FromBody] SetRolesRequest req, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("User", id);
        var existing = await _db.UserRoles.Where(ur => ur.UserId == id).ToListAsync(ct);
        _db.UserRoles.RemoveRange(existing);
        foreach (var rid in req.RoleIds.Distinct())
            _db.UserRoles.Add(new UserRole { UserId = id, RoleId = rid });
        await _db.SaveChangesAsync(ct);

        // Self-protection: an admin must not strip their own user-management access.
        if (id == _currentUser.UserId)
        {
            var effective = await _resolver.ResolveAsync(id, ct);
            if (!effective.Contains(ManageUsers))
                throw new ConflictException("You cannot remove your own user-management access.");
        }

        await _audit.LogAsync("RolesChanged", "Access.User", id, null, new { req.RoleIds }, ct);
        return OkResponse(await BuildDetail(id, ct));
    }

    [HttpPost("{id:guid}/link-employee")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse>> LinkEmployee(Guid id, [FromBody] LinkEmployeeRequest req, CancellationToken ct)
    {
        await LinkEmployeeInternalAsync(id, req.EmployeeId, ct);
        await _audit.LogAsync("EmployeeLinked", "Access.User", id, null, new { req.EmployeeId }, ct);
        return OkResponse("Employee linked.");
    }

    [HttpPost("{id:guid}/unlink-employee")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse>> UnlinkEmployee(Guid id, CancellationToken ct)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == id, ct);
        if (emp is not null) { emp.UserId = null; await _db.SaveChangesAsync(ct); }
        await _audit.LogAsync("EmployeeUnlinked", "Access.User", id, null, null, ct);
        return OkResponse("Employee unlinked.");
    }

    [HttpPost("from-employee")]
    [RequirePermission(ManageUsers)]
    public async Task<ActionResult<ApiResponse<UserDetail>>> CreateFromEmployee([FromBody] CreateFromEmployeeRequest req, CancellationToken ct)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == req.EmployeeId, ct)
            ?? throw new NotFoundException("Employee", req.EmployeeId);
        if (emp.UserId is not null) throw new ConflictException("This employee already has a linked user account.");

        var email = (req.Email ?? emp.Email).Trim().ToLowerInvariant();
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email && !u.IsDeleted, ct))
            throw new ConflictException("A user with this email already exists.");

        var user = new User
        {
            Email = email,
            FullName = (req.FullName ?? $"{emp.FirstNameAr ?? emp.FirstName} {emp.LastNameAr ?? emp.LastName}").Trim(),
            PasswordHash = PasswordHasher.Hash(Guid.NewGuid().ToString("N")),
            IsActive = false,
            Status = UserStatus.Invited,
        };
        _db.Users.Add(user);
        if (req.RoleIds is { Count: > 0 })
            foreach (var rid in req.RoleIds.Distinct())
                _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = rid });
        await _db.SaveChangesAsync(ct);

        emp.UserId = user.Id;
        await _db.SaveChangesAsync(ct);

        var link = await IssueAndQueueAsync(user, "Invite", TimeSpan.FromDays(7), ct);
        await _audit.LogAsync("UserCreatedFromEmployee", "Access.User", user.Id, null,
            new { user.Email, req.EmployeeId }, ct);
        return CreatedResponse(await BuildDetail(user.Id, ct), link.ResetLink);
    }

    // ---- helpers ----

    private async Task LinkEmployeeInternalAsync(Guid userId, Guid employeeId, CancellationToken ct)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new NotFoundException("Employee", employeeId);
        if (emp.UserId is not null && emp.UserId != userId)
            throw new ConflictException("This employee is already linked to another user.");
        var other = await _db.Employees.FirstOrDefaultAsync(e => e.UserId == userId && e.Id != employeeId, ct);
        if (other is not null) other.UserId = null; // a user links to at most one employee
        emp.UserId = userId;
        await _db.SaveChangesAsync(ct);
    }

    private async Task RevokeRefreshTokensAsync(Guid userId, CancellationToken ct)
    {
        var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;
    }

    private async Task<TokenLinkResult> IssueAndQueueAsync(User user, string purpose, TimeSpan ttl, CancellationToken ct)
    {
        var raw = await _auth.IssueResetTokenAsync(user.Id, purpose, ttl, ct);
        var baseUrl = (_config["App:WebUrl"] ?? _config.GetSection("Cors:Origins").Get<string[]>()?.FirstOrDefault() ?? "").TrimEnd('/');
        var path = purpose == "Invite" ? "accept-invite" : "reset-password";
        var link = $"{baseUrl}/{path}?token={raw}";

        _db.EmailQueue.Add(new EmailNotificationQueue
        {
            UserId = user.Id,
            ToEmail = user.Email,
            Subject = purpose == "Invite" ? "دعوة لإنشاء حسابك في سند" : "إعادة تعيين كلمة المرور",
            Body = purpose == "Invite"
                ? $"تمت دعوتك لإنشاء حسابك. الرابط صالح لمدة 7 أيام:\n{link}"
                : $"لإعادة تعيين كلمة المرور (صالح لمدة ساعتين):\n{link}",
            Category = purpose == "Invite" ? "AccessInvite" : "AccessReset",
            Link = link,
        });
        await _db.SaveChangesAsync(ct);
        return new TokenLinkResult { ResetLink = link, Purpose = purpose };
    }

    private async Task<UserDetail> BuildDetail(Guid id, CancellationToken ct)
    {
        var u = await _db.Users.AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("User", id);

        var emp = await _db.Employees.AsNoTracking().Where(e => e.UserId == id)
            .Select(e => new { e.Id, Name = (e.FirstNameAr ?? e.FirstName) + " " + (e.LastNameAr ?? e.LastName), e.EmployeeNumber })
            .FirstOrDefaultAsync(ct);

        var templates = await _db.UserPermissionTemplates.AsNoTracking()
            .Where(t => t.UserId == id)
            .Select(t => new TemplateRef { Id = t.PermissionTemplateId, Name = t.PermissionTemplate!.NameAr })
            .ToListAsync(ct);

        var overrides = await _db.UserPermissionOverrides.AsNoTracking()
            .Where(o => o.UserId == id)
            .Select(o => new OverrideDto { Id = o.Id, PermissionCode = o.PermissionCode, IsGranted = o.IsGranted })
            .ToListAsync(ct);

        return new UserDetail
        {
            Id = u.Id, Email = u.Email, FullName = u.FullName, Phone = u.Phone,
            Status = u.Status.ToString(), IsActive = u.IsActive, LastLoginAt = u.LastLoginAt,
            EmployeeId = emp?.Id, EmployeeName = emp?.Name?.Trim(), EmployeeNumber = emp?.EmployeeNumber,
            Roles = u.UserRoles.Select(r => r.Role.NameAr ?? r.Role.Name).ToList(),
            RoleIds = u.UserRoles.Select(r => r.RoleId).ToList(),
            Templates = templates,
            Overrides = overrides,
            EffectivePermissions = (await _resolver.ResolveAsync(id, ct)).ToList(),
        };
    }
}
