namespace HR.Modules.Identity.DTOs;

// ---- Users ----

public class UserListItem
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public bool IsActive { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeNumber { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class UserDetail : UserListItem
{
    public string? Phone { get; set; }
    public List<Guid> RoleIds { get; set; } = new();
    public List<TemplateRef> Templates { get; set; } = new();
    public List<OverrideDto> Overrides { get; set; } = new();
    public List<string> EffectivePermissions { get; set; } = new();
}

public class TemplateRef { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; }

public class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Password { get; set; }
    public List<Guid>? RoleIds { get; set; }
    public Guid? EmployeeId { get; set; }
    /// <summary>When true (or no password supplied), the account is created as Invited and an invite link is issued.</summary>
    public bool SendInvite { get; set; }
}

public class UpdateUserRequest { public string? FullName { get; set; } public string? Phone { get; set; } }
public class ChangeEmailRequest { public string Email { get; set; } = string.Empty; }
public class SetPasswordRequest { public string Password { get; set; } = string.Empty; }
public class SetRolesRequest { public List<Guid> RoleIds { get; set; } = new(); }
public class LinkEmployeeRequest { public Guid EmployeeId { get; set; } }
public class CreateFromEmployeeRequest
{
    public Guid EmployeeId { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public List<Guid>? RoleIds { get; set; }
}
public class TokenLinkResult { public string ResetLink { get; set; } = string.Empty; public string Purpose { get; set; } = "Reset"; }
public class AcceptResetRequest { public string Token { get; set; } = string.Empty; public string NewPassword { get; set; } = string.Empty; }

// ---- Roles ----

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public int UserCount { get; set; }
    public List<string> PermissionCodes { get; set; } = new();
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public List<string>? PermissionCodes { get; set; }
}
public class SetPermissionsRequest { public List<string> PermissionCodes { get; set; } = new(); }

// ---- Permission catalog / templates / overrides ----

public class PermissionCatalogModule
{
    public string Module { get; set; } = string.Empty;
    public List<PermissionCatalogItem> Permissions { get; set; } = new();
}
public class PermissionCatalogItem { public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; }

public class TemplateDto
{
    public Guid Id { get; set; }
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public List<string> PermissionCodes { get; set; } = new();
}
public class CreateTemplateRequest
{
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<string>? PermissionCodes { get; set; }
}
public class AssignTemplateRequest { public Guid TemplateId { get; set; } }

public class OverrideDto { public Guid Id { get; set; } public string PermissionCode { get; set; } = string.Empty; public bool IsGranted { get; set; } }
public class SetOverridesRequest { public List<OverrideItem> Overrides { get; set; } = new(); }
public class OverrideItem { public string PermissionCode { get; set; } = string.Empty; public bool IsGranted { get; set; } }

// ---- Audit ----

public class AccessAuditDto
{
    public Guid Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? UserEmail { get; set; }
    public DateTime Timestamp { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
}
