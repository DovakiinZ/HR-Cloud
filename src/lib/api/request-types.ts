import { apiFetch } from "../api-client";

// ════════════════════════════════════════════════════════════════════════════════════
//  Client for the RequestType + effect-definition admin engine (RequestTypesController).
//  Distinct from the master-data request-type list in requests.ts: these are the RequestType
//  ENTITIES that the completion engine attaches configurable "effects" to. Matched to the
//  master-data list by Code.
// ════════════════════════════════════════════════════════════════════════════════════

/** Where an effect input's value comes from. Closed set — mirrors EffectValueSource. */
export type EffectValueSource = "FormField" | "RequestContext" | "Constant" | "CurrentUser" | "TenantContext";

export type EffectTriggerName = "FinalApproval" | "Rejection" | "Cancellation";
export type EffectExecutionModeName = "Transactional" | "Asynchronous";

// ── Read models ────────────────────────────────────────────────────────────────────

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
  canDelete: boolean;
}

export interface RequestTypeDetail extends RequestTypeListItem {
  formDefinitionId: string;
  workflowDefinitionId?: string | null;
  printTemplateId?: string | null;
  leaveTypeId?: string | null;
  effects: RequestEffectDefinition[];
}

export interface RequestEffectDefinition {
  id: string;
  requestTypeId: string;
  effectType: string;
  trigger: EffectTriggerName;
  effectVersion: number;
  sequence: number;
  isEnabled: boolean;
  isRequired: boolean;
  executionMode: EffectExecutionModeName;
  configurationJson: string;
  canDelete: boolean;
  canDisable: boolean;
  /** Catalog label; null when the action was retired (needs attention). */
  labelAr?: string | null;
  labelEn?: string | null;
  executorAvailable: boolean;
}

// ── Catalog ──────────────────────────────────────────────────────────────────────

export interface EffectInput {
  key: string;
  labelAr: string;
  labelEn: string;
  isRequired: boolean;
  allowedSources: EffectValueSource[];
}

export interface EffectAction {
  effectType: string;
  labelAr: string;
  labelEn: string;
  descriptionAr: string;
  descriptionEn: string;
  module: string;
  supportedTriggers: EffectTriggerName[];
  executionMode: EffectExecutionModeName;
  inputs: EffectInput[];
  requiredPermissions: string[];
  /** Greyed out in the picker when false — it would fail at approval time. */
  executorAvailable: boolean;
}

// ── Validation ─────────────────────────────────────────────────────────────────────

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

// ── Config shape (inputKey → {source, key}) ─────────────────────────────────────────

export interface EffectValueMapping {
  source: EffectValueSource;
  key: string;
}
export type EffectConfig = Record<string, EffectValueMapping>;

export const parseEffectConfig = (json: string | null | undefined): EffectConfig => {
  if (!json) return {};
  try {
    const parsed = JSON.parse(json) as Record<string, { source?: string; key?: string }>;
    const out: EffectConfig = {};
    for (const [k, v] of Object.entries(parsed ?? {})) {
      if (v && typeof v === "object") out[k] = { source: (v.source as EffectValueSource) ?? "Constant", key: v.key ?? "" };
    }
    return out;
  } catch { return {}; }
};

export const serializeEffectConfig = (config: EffectConfig): string => JSON.stringify(config);

// ── Write models ───────────────────────────────────────────────────────────────────

const TRIGGER_VALUE: Record<EffectTriggerName, number> = { FinalApproval: 1, Rejection: 2, Cancellation: 3 };

export interface UpsertEffectInput {
  effectType: string;
  trigger: EffectTriggerName;
  sequence: number;
  isEnabled: boolean;
  config: EffectConfig;
}

/** Build the on-the-wire body. Trigger is sent numerically (accepted with or without the
 *  server's string-enum converter); config is serialized to ConfigurationJson. */
const toUpsertBody = (input: UpsertEffectInput) => ({
  effectType: input.effectType,
  trigger: TRIGGER_VALUE[input.trigger],
  effectVersion: 1,
  sequence: input.sequence,
  isEnabled: input.isEnabled,
  configurationJson: serializeEffectConfig(input.config),
});

export interface AssignableAsset {
  id: string;
  code: string;
  nameAr: string;
  nameEn: string;
  categoryId?: string | null;
  categoryNameAr?: string | null;
  categoryNameEn?: string | null;
  status: string;
}

const BASE = "/api/platform/request-types";

// ── Request type defs (matched to the master-data list by code) ─────────────────────

export const listRequestTypeDefs = (includeInactive = true) =>
  apiFetch<RequestTypeListItem[]>(`${BASE}?includeInactive=${includeInactive}`);

export const getRequestTypeDef = (id: string) => apiFetch<RequestTypeDetail>(`${BASE}/${id}`);

export const duplicateRequestTypeDef = (id: string, input: { code?: string; nameAr: string; nameEn: string }) =>
  apiFetch<RequestTypeDetail>(`${BASE}/${id}/duplicate`, { method: "POST", body: input });

export const validateRequestTypeDef = (id: string) => apiFetch<ValidationResult>(`${BASE}/${id}/validate`);

export const setRequestTypeDefActive = (id: string, isActive: boolean) =>
  apiFetch<RequestTypeDetail>(`${BASE}/${id}/active`, { method: "PUT", body: { isActive } });

// ── Effects ─────────────────────────────────────────────────────────────────────────

export const listEffects = (typeId: string) =>
  apiFetch<RequestEffectDefinition[]>(`${BASE}/${typeId}/effects`);

export const addEffect = (typeId: string, input: UpsertEffectInput) =>
  apiFetch<RequestEffectDefinition>(`${BASE}/${typeId}/effects`, { method: "POST", body: toUpsertBody(input) });

export const validateEffect = (typeId: string, input: UpsertEffectInput) =>
  apiFetch<ValidationResult>(`${BASE}/${typeId}/effects/validate`, { method: "POST", body: toUpsertBody(input) });

export const reorderEffects = (typeId: string, effectIds: string[]) =>
  apiFetch<RequestEffectDefinition[]>(`${BASE}/${typeId}/effects/reorder`, { method: "PUT", body: { effectIds } });

export const updateEffect = (effectId: string, input: UpsertEffectInput) =>
  apiFetch<RequestEffectDefinition>(`${BASE}/effects/${effectId}`, { method: "PUT", body: toUpsertBody(input) });

export const setEffectEnabled = (effectId: string, isEnabled: boolean) =>
  apiFetch<RequestEffectDefinition>(`${BASE}/effects/${effectId}/enabled`, { method: "PUT", body: { isEnabled } });

export const deleteEffect = (effectId: string) =>
  apiFetch<void>(`${BASE}/effects/${effectId}`, { method: "DELETE" });

// ── Catalog + asset lookup ──────────────────────────────────────────────────────────

export const getEffectCatalog = () => apiFetch<EffectAction[]>("/api/platform/effect-catalog");

export const getAssignableAssets = (search?: string) =>
  apiFetch<AssignableAsset[]>(`/api/platform/assets/assignable${search ? `?search=${encodeURIComponent(search)}` : ""}`);
