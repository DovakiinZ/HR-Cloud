// Request-type administration — the real RequestType entity and engine_request_types, not
// MasterDataItem. The settings page previously read master data with ObjectType=RequestType, which
// nothing in the backend consumed: those rows were cosmetic while the Request Center ran off a
// different table entirely.
import { apiFetch } from "@/lib/api-client";

const BASE = "/api/platform/request-types";

// ── Enums (mirror HR.Domain.Enums) ───────────────────────────────────────────

export type EffectTrigger = "FinalApproval" | "Rejection" | "Cancellation";
export type EffectExecutionMode = "Transactional" | "Asynchronous";
export type EffectValueSource =
  | "FormField" | "RequestContext" | "Constant" | "CurrentUser" | "TenantContext";

// ── Read models ──────────────────────────────────────────────────────────────

export interface RequestTypeListItem {
  id: string;
  code: string;
  nameAr: string;
  nameEn: string;
  descriptionAr?: string | null;
  descriptionEn?: string | null;
  categoryId?: string | null;
  icon?: string | null;
  color?: string | null;
  isSystem: boolean;
  isActive: boolean;
  seedVersion: number;
  sortOrder: number;
  hasForm: boolean;
  hasWorkflow: boolean;
  effectCount: number;
  /** False for system requests. The API refuses the delete regardless — this drives the UI. */
  canDelete: boolean;
}

export interface RequestEffectDefinition {
  id: string;
  requestTypeId: string;
  effectType: string;
  trigger: EffectTrigger;
  effectVersion: number;
  sequence: number;
  isEnabled: boolean;
  isRequired: boolean;
  executionMode: EffectExecutionMode;
  configurationJson: string;
  canDelete: boolean;
  canDisable: boolean;
  /** Null when the action is no longer in the catalog, or the caller may not configure it. */
  labelAr?: string | null;
  labelEn?: string | null;
  executorAvailable: boolean;
}

export interface RequestTypeDetail extends RequestTypeListItem {
  formDefinitionId: string;
  workflowDefinitionId?: string | null;
  printTemplateId?: string | null;
  leaveTypeId?: string | null;
  effects: RequestEffectDefinition[];
}

export interface EffectInputDescriptor {
  key: string;
  labelAr: string;
  labelEn: string;
  isRequired: boolean;
  /** Closed per input — an employee id accepts RequestContext but never Constant. */
  allowedSources: EffectValueSource[];
}

export interface EffectActionDescriptor {
  effectType: string;
  labelAr: string;
  labelEn: string;
  descriptionAr: string;
  descriptionEn: string;
  module: string;
  supportedTriggers: EffectTrigger[];
  executionMode: EffectExecutionMode;
  inputs: EffectInputDescriptor[];
  requiredPermissions: string[];
  executorAvailable: boolean;
}

export interface ValidationError {
  effectType?: string | null;
  field: string;
  messageAr: string;
  messageEn: string;
}

export interface ValidationResult {
  isValid: boolean;
  errors: ValidationError[];
}

export interface AssignableAsset {
  id: string;
  code: string;
  nameAr: string;
  nameEn: string;
  categoryId?: string | null;
  categoryNameAr?: string | null;
  categoryNameEn?: string | null;
  status: "Available" | "Assigned";
}

// ── Write models ─────────────────────────────────────────────────────────────

export interface CreateRequestTypeInput {
  code: string;
  nameAr: string;
  nameEn: string;
  descriptionAr?: string | null;
  descriptionEn?: string | null;
  categoryId?: string | null;
  formDefinitionId: string;
  workflowDefinitionId?: string | null;
  icon?: string | null;
  color?: string | null;
  sortOrder?: number;
}

/** Code is absent on purpose — it is immutable after creation, and the API ignores it. */
export interface UpdateRequestTypeInput {
  nameAr: string;
  nameEn: string;
  descriptionAr?: string | null;
  descriptionEn?: string | null;
  categoryId?: string | null;
  workflowDefinitionId?: string | null;
  printTemplateId?: string | null;
  icon?: string | null;
  color?: string | null;
  sortOrder?: number;
}

export interface EffectValueMapping {
  source: EffectValueSource;
  key: string;
}

export interface UpsertEffectInput {
  effectType: string;
  trigger?: EffectTrigger;
  effectVersion?: number;
  sequence?: number;
  isEnabled?: boolean;
  configurationJson: string;
}

// ── Request types ────────────────────────────────────────────────────────────

export const listRequestTypes = (includeInactive = true) =>
  apiFetch<RequestTypeListItem[]>(`${BASE}?includeInactive=${includeInactive}`);

export const getRequestType = (id: string) => apiFetch<RequestTypeDetail>(`${BASE}/${id}`);

export const createRequestType = (input: CreateRequestTypeInput) =>
  apiFetch<RequestTypeDetail>(BASE, { method: "POST", body: input });

export const updateRequestType = (id: string, input: UpdateRequestTypeInput) =>
  apiFetch<RequestTypeDetail>(`${BASE}/${id}`, { method: "PUT", body: input });

export const deleteRequestType = (id: string) =>
  apiFetch<void>(`${BASE}/${id}`, { method: "DELETE" });

export const duplicateRequestType = (id: string, body: { code?: string; nameAr: string; nameEn: string }) =>
  apiFetch<RequestTypeDetail>(`${BASE}/${id}/duplicate`, { method: "POST", body });

export const validateRequestType = (id: string) =>
  apiFetch<ValidationResult>(`${BASE}/${id}/validate`);

export const setRequestTypeActive = (id: string, isActive: boolean) =>
  apiFetch<RequestTypeDetail>(`${BASE}/${id}/active`, { method: "PUT", body: { isActive } });

// ── Effects ──────────────────────────────────────────────────────────────────

export const listEffects = (requestTypeId: string) =>
  apiFetch<RequestEffectDefinition[]>(`${BASE}/${requestTypeId}/effects`);

export const addEffect = (requestTypeId: string, input: UpsertEffectInput) =>
  apiFetch<RequestEffectDefinition>(`${BASE}/${requestTypeId}/effects`, { method: "POST", body: input });

export const validateEffect = (requestTypeId: string, input: UpsertEffectInput) =>
  apiFetch<ValidationResult>(`${BASE}/${requestTypeId}/effects/validate`, { method: "POST", body: input });

export const reorderEffects = (requestTypeId: string, effectIds: string[]) =>
  apiFetch<RequestEffectDefinition[]>(`${BASE}/${requestTypeId}/effects/reorder`, {
    method: "PUT", body: { effectIds },
  });

export const updateEffect = (effectId: string, input: UpsertEffectInput) =>
  apiFetch<RequestEffectDefinition>(`${BASE}/effects/${effectId}`, { method: "PUT", body: input });

export const setEffectEnabled = (effectId: string, isEnabled: boolean) =>
  apiFetch<RequestEffectDefinition>(`${BASE}/effects/${effectId}/enabled`, {
    method: "PUT", body: { isEnabled },
  });

export const deleteEffect = (effectId: string) =>
  apiFetch<void>(`${BASE}/effects/${effectId}`, { method: "DELETE" });

// ── Catalog + assets ─────────────────────────────────────────────────────────

/** Already filtered server-side to the actions this caller may configure. */
export const getEffectCatalog = () =>
  apiFetch<EffectActionDescriptor[]>("/api/platform/effect-catalog");

export const getAssignableAssets = (search?: string) =>
  apiFetch<AssignableAsset[]>(
    `/api/platform/assets/assignable${search ? `?search=${encodeURIComponent(search)}` : ""}`);

// ── Configuration helpers ────────────────────────────────────────────────────

export type EffectConfiguration = Record<string, EffectValueMapping>;

/**
 * Parses a stored ConfigurationJson. Returns {} rather than throwing on malformed input: the
 * builder must still render an effect whose configuration is broken, since that is precisely the
 * one a user needs to open and repair.
 */
export function parseConfiguration(json?: string | null): EffectConfiguration {
  if (!json) return {};
  try {
    const parsed = JSON.parse(json);
    return parsed && typeof parsed === "object" ? (parsed as EffectConfiguration) : {};
  } catch {
    return {};
  }
}

export const serializeConfiguration = (config: EffectConfiguration) => JSON.stringify(config);

/** Keys addressable via RequestContext — mirrors RequestContextKeys on the server. */
export const REQUEST_CONTEXT_KEYS = [
  "employeeId", "requestId", "requestNumber", "requestTypeCode",
  "leaveTypeId", "startDate", "endDate", "daysCount",
] as const;

export const CURRENT_USER_KEYS = ["userId"] as const;
export const TENANT_CONTEXT_KEYS = ["tenantId"] as const;

export const SOURCE_LABELS_AR: Record<EffectValueSource, string> = {
  FormField: "حقل من النموذج",
  RequestContext: "بيانات الطلب",
  Constant: "قيمة ثابتة",
  CurrentUser: "المستخدم الحالي",
  TenantContext: "المنشأة",
};
