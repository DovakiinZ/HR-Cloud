import { apiFetch } from "../api-client";

// ── Attendance permission types (استئذان) — configurable per-tenant ──
// Types are MasterDataItem rows (ObjectType="AttendancePermissionType"); their MetadataJson is the
// PermissionTypeRules shape below. Managed via the generic master-data endpoints. The two endpoints
// here are the self-service (employee) reads used by the submit form.

export const PERMISSION_TYPE_OBJECT = "AttendancePermissionType";
export const PERMISSION_TYPE_LOOKUP = "attendance-permission-types";

/** Block=0, Warn=1, RequireApprovalOverride=2 — stored numerically in MetadataJson. */
export type PermissionExceedBehavior = 0 | 1 | 2;

export const EXCEED_BEHAVIORS: { value: PermissionExceedBehavior; labelAr: string }[] = [
  { value: 0, labelAr: "منع الطلب" },
  { value: 1, labelAr: "تنبيه فقط" },
  { value: 2, labelAr: "يتطلب موافقة تجاوز" },
];

export interface ScopeCriterion {
  dimension: string; // "Department" | "Branch" | "JobTitle"
  valueIds: string[];
}

/** Mirrors the backend SelectionScope. null / mode "All" = entire company. */
export interface SelectionScope {
  mode: "All" | "Criteria";
  include: ScopeCriterion[];
  exclude: ScopeCriterion[];
  includeEmployeeIds: string[];
  excludeEmployeeIds: string[];
}

/** Mirrors backend PermissionTypeRules (stored in MetadataJson). All limits null = unlimited. */
export interface PermissionTypeRules {
  paid: boolean;
  maxMinutesPerRequest: number | null;
  maxMinutesPerDay: number | null;
  maxMinutesPerMonth: number | null;
  maxRequestsPerDay: number | null;
  maxRequestsPerMonth: number | null;
  exceedBehavior: PermissionExceedBehavior;
  eligibility: SelectionScope | null;
}

export const DEFAULT_PERMISSION_RULES: PermissionTypeRules = {
  paid: true,
  maxMinutesPerRequest: null,
  maxMinutesPerDay: null,
  maxMinutesPerMonth: null,
  maxRequestsPerDay: null,
  maxRequestsPerMonth: null,
  exceedBehavior: 0,
  eligibility: null,
};

export interface PermissionLimits {
  maxMinutesPerRequest: number | null;
  maxMinutesPerDay: number | null;
  maxMinutesPerMonth: number | null;
  maxRequestsPerDay: number | null;
  maxRequestsPerMonth: number | null;
}

export interface PermissionUsage {
  usedMinutesDay: number;
  remainingMinutesDay: number | null;
  usedMinutesMonth: number;
  remainingMinutesMonth: number | null;
  usedRequestsDay: number;
  remainingRequestsDay: number | null;
  usedRequestsMonth: number;
  remainingRequestsMonth: number | null;
}

export interface EligiblePermissionType {
  id: string;
  code: string;
  nameAr: string;
  nameEn: string;
  paid: boolean;
  // The MVC serializer may emit the enum as a number (0/1/2) or a name — normalize on read.
  exceedBehavior: number | string;
  usage: PermissionUsage;
  limits: PermissionLimits;
}

export interface PermissionDecision {
  outcome: string; // "Allowed" | "Warn" | "Block" | "RequireOverride"
  reasonAr?: string | null;
  reasonEn?: string | null;
}

export interface ValidatePermissionResult {
  durationMinutes: number;
  excusedMinutes: number;
  usage: PermissionUsage | null;
  decision: PermissionDecision;
  overrideRequired: boolean;
}

/** The permission types the calling employee is eligible for, with their limits + current usage. */
export async function getEligiblePermissionTypes(): Promise<EligiblePermissionType[]> {
  return (await apiFetch<EligiblePermissionType[]>("/api/attendance/permissions/eligible-types")) ?? [];
}

/** Dry-run a proposed window: duration, usage, and the cap decision — commits nothing. */
export async function validatePermission(input: {
  permissionTypeId: string;
  date: string; // ISO date
  fromTime: string; // "HH:mm"
  toTime: string; // "HH:mm"
}): Promise<ValidatePermissionResult> {
  return apiFetch<ValidatePermissionResult>("/api/attendance/permissions/validate", {
    method: "POST",
    body: input,
  });
}

/** Normalize an exceed-behavior value (number or enum name) to the numeric union. */
export function normalizeExceedBehavior(v: number | string | null | undefined): PermissionExceedBehavior {
  if (v === 1 || v === "Warn") return 1;
  if (v === 2 || v === "RequireApprovalOverride") return 2;
  return 0;
}
