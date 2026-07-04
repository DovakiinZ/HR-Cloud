import { apiFetch } from "../api-client";

// ===== Types (mirror backend DTOs) =====

export interface UserListItem {
  id: string;
  email: string;
  fullName: string;
  status: "Active" | "Suspended" | "Invited";
  isActive: boolean;
  lastLoginAt?: string | null;
  employeeId?: string | null;
  employeeName?: string | null;
  employeeNumber?: string | null;
  roles: string[];
}

export interface TemplateRef { id: string; name: string }
export interface OverrideDto { id: string; permissionCode: string; isGranted: boolean }

export interface UserDetail extends UserListItem {
  phone?: string | null;
  roleIds: string[];
  templates: TemplateRef[];
  overrides: OverrideDto[];
  effectivePermissions: string[];
}

export interface CreateUserRequest {
  email: string;
  fullName: string;
  phone?: string;
  password?: string;
  roleIds?: string[];
  employeeId?: string;
  sendInvite?: boolean;
}

export interface RoleDto {
  id: string;
  name: string;
  nameAr?: string | null;
  description?: string | null;
  isSystemRole: boolean;
  userCount: number;
  permissionCodes: string[];
}

export interface CreateRoleRequest {
  name: string;
  nameAr?: string;
  description?: string;
  permissionCodes?: string[];
}

export interface PermissionCatalogItem { code: string; name: string }
export interface PermissionCatalogModule { module: string; permissions: PermissionCatalogItem[] }

export interface TemplateDto {
  id: string;
  nameEn: string;
  nameAr: string;
  description?: string | null;
  isSystem: boolean;
  permissionCodes: string[];
}

export interface CreateTemplateRequest {
  nameEn: string;
  nameAr: string;
  description?: string;
  permissionCodes?: string[];
}

export interface TokenLinkResult { resetLink: string; purpose: string }

export interface AccessAuditDto {
  id: string;
  action: string;
  entityType: string;
  entityId: string;
  userEmail?: string | null;
  timestamp: string;
  oldValues?: string | null;
  newValues?: string | null;
}

// ===== Users =====

export const listUsers = () => apiFetch<UserListItem[]>("/api/users");
export const getUser = (id: string) => apiFetch<UserDetail>(`/api/users/${id}`);
export const createUser = (body: CreateUserRequest) => apiFetch<UserDetail>("/api/users", { method: "POST", body });
export const updateUser = (id: string, body: { fullName?: string; phone?: string }) =>
  apiFetch<UserDetail>(`/api/users/${id}`, { method: "PUT", body });
export const disableUser = (id: string) => apiFetch<unknown>(`/api/users/${id}/disable`, { method: "POST" });
export const enableUser = (id: string) => apiFetch<unknown>(`/api/users/${id}/enable`, { method: "POST" });
export const forceLogout = (id: string) => apiFetch<unknown>(`/api/users/${id}/force-logout`, { method: "POST" });
export const resetPassword = (id: string) => apiFetch<TokenLinkResult>(`/api/users/${id}/reset-password`, { method: "POST" });
/** Admin sets a user's password directly (audited). */
export const setPassword = (id: string, password: string) =>
  apiFetch<unknown>(`/api/users/${id}/set-password`, { method: "POST", body: { password } });
export const changeEmail = (id: string, email: string) =>
  apiFetch<unknown>(`/api/users/${id}/change-email`, { method: "POST", body: { email } });
export const setUserRoles = (id: string, roleIds: string[]) =>
  apiFetch<UserDetail>(`/api/users/${id}/roles`, { method: "PUT", body: { roleIds } });
export const linkEmployee = (id: string, employeeId: string) =>
  apiFetch<unknown>(`/api/users/${id}/link-employee`, { method: "POST", body: { employeeId } });
export const unlinkEmployee = (id: string) => apiFetch<unknown>(`/api/users/${id}/unlink-employee`, { method: "POST" });
export const createUserFromEmployee = (body: { employeeId: string; email?: string; fullName?: string; roleIds?: string[] }) =>
  apiFetch<UserDetail>("/api/users/from-employee", { method: "POST", body });

// ===== Roles =====

export const listRoles = () => apiFetch<RoleDto[]>("/api/roles");
export const createRole = (body: CreateRoleRequest) => apiFetch<RoleDto>("/api/roles", { method: "POST", body });
export const updateRole = (id: string, body: CreateRoleRequest) => apiFetch<RoleDto>(`/api/roles/${id}`, { method: "PUT", body });
export const setRolePermissions = (id: string, permissionCodes: string[]) =>
  apiFetch<RoleDto>(`/api/roles/${id}/permissions`, { method: "PUT", body: { permissionCodes } });
export const deleteRole = (id: string) => apiFetch<unknown>(`/api/roles/${id}`, { method: "DELETE" });

// ===== Access / templates / overrides / catalog / audit =====

export const getPermissionCatalog = () => apiFetch<PermissionCatalogModule[]>("/api/access/permissions");
export const listTemplates = () => apiFetch<TemplateDto[]>("/api/access/templates");
export const seedDefaultTemplates = () => apiFetch<unknown>("/api/access/templates/seed-defaults", { method: "POST" });
export const createTemplate = (body: CreateTemplateRequest) => apiFetch<TemplateDto>("/api/access/templates", { method: "POST", body });
export const updateTemplate = (id: string, body: CreateTemplateRequest) =>
  apiFetch<TemplateDto>(`/api/access/templates/${id}`, { method: "PUT", body });
export const setTemplatePermissions = (id: string, permissionCodes: string[]) =>
  apiFetch<TemplateDto>(`/api/access/templates/${id}/permissions`, { method: "PUT", body: { permissionCodes } });
export const duplicateTemplate = (id: string) => apiFetch<TemplateDto>(`/api/access/templates/${id}/duplicate`, { method: "POST" });
export const deleteTemplate = (id: string) => apiFetch<unknown>(`/api/access/templates/${id}`, { method: "DELETE" });
export const assignTemplate = (userId: string, templateId: string) =>
  apiFetch<unknown>(`/api/access/users/${userId}/assign-template`, { method: "POST", body: { templateId } });
export const revokeTemplate = (userId: string, templateId: string) =>
  apiFetch<unknown>(`/api/access/users/${userId}/templates/${templateId}`, { method: "DELETE" });
export const setOverrides = (userId: string, overrides: { permissionCode: string; isGranted: boolean }[]) =>
  apiFetch<unknown>(`/api/access/users/${userId}/overrides`, { method: "PUT", body: { overrides } });
export const getEffectivePermissions = (userId: string) => apiFetch<string[]>(`/api/access/users/${userId}/effective`);
export const getAccessAudit = (take = 200) => apiFetch<AccessAuditDto[]>(`/api/access/audit?take=${take}`);

// ===== Arabic labels =====

export const MODULE_AR: Record<string, string> = {
  Identity: "المستخدمون والأدوار",
  Employees: "الموظفون",
  Tasks: "المهام",
  Departments: "الأقسام",
  Branches: "الفروع",
  Settings: "الإعدادات",
  Attendance: "الحضور",
  Leaves: "الإجازات",
  Payroll: "الرواتب",
  Expenses: "المصروفات",
  Loans: "السلف",
  Documents: "المستندات",
  Reports: "التقارير",
  Dashboards: "لوحات المعلومات",
  Workflows: "مسارات العمل",
  ESS: "الخدمة الذاتية",
  Notifications: "التنبيهات",
  Requests: "الطلبات",
};

export const ACTION_AR: Record<string, string> = {
  View: "عرض", Create: "إضافة", Edit: "تعديل", Delete: "حذف", Export: "تصدير",
  Approve: "اعتماد", Reject: "رفض", Run: "تشغيل", Lock: "إقفال", Assign: "إسناد",
  Cancel: "إلغاء", Terminate: "إنهاء خدمة", ViewSettlement: "عرض المخالصة", Generate: "توليد",
  ManageUsers: "إدارة المستخدمين", ManageRoles: "إدارة الأدوار", ManageTemplates: "إدارة القوالب", ViewAudit: "سجل التدقيق",
  ViewUsers: "عرض المستخدمين", CreateUsers: "إضافة مستخدمين", EditUsers: "تعديل المستخدمين", DeleteUsers: "حذف المستخدمين",
  ViewRoles: "عرض الأدوار", CreateRoles: "إضافة أدوار", EditRoles: "تعديل الأدوار", DeleteRoles: "حذف الأدوار",
};

export const STATUS_AR: Record<string, string> = {
  Active: "نشط", Suspended: "معطّل", Invited: "مدعو",
};
