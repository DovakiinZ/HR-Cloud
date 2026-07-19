import { apiFetch, API_BASE_URL } from "../api-client";
import { getAccessToken } from "../auth-storage";
import { toast } from "sonner";

// ── Enums (sent as strings; the backend binds enum names case-insensitively) ────────

export type ReportType = "Tabular" | "Summary" | "Matrix" | "Chart";
export type ReportScope = "Personal" | "Department" | "Company" | "Shared";
export type ReportFieldType = "ObjectField" | "CalculatedField" | "AggregateField" | "RelationshipField";
export type AggregationType = "Count" | "Sum" | "Average" | "Min" | "Max" | "Percentage";
export type SortDirection = "Ascending" | "Descending";
export type JoinType = "Inner" | "Left" | "Right";

export type ReportFilterOperator =
  | "Equals" | "NotEquals" | "Contains" | "StartsWith" | "EndsWith"
  | "GreaterThan" | "LessThan" | "GreaterThanOrEqual" | "LessThanOrEqual"
  | "Between" | "In" | "NotIn" | "IsNull" | "IsNotNull";

/** FieldKind names as they arrive on ReportColumn.type. Computed fields are always "Text". */
export type FieldKind =
  | "Text" | "Number" | "Decimal" | "Currency" | "Percentage"
  | "Date" | "DateTime" | "Boolean" | "Reference" | "Enum" | "Guid";

// ── Definition DTOs ────────────────────────────────────────────────────────────────

export interface ReportField {
  id: string;
  fieldType: ReportFieldType;
  objectDefinitionId?: string | null;
  fieldCode: string;
  displayNameEn: string;
  displayNameAr: string;
  aggregation?: AggregationType | null;
  /** AST JSON. Not editable — author via calculationText instead. */
  calculationExpression?: string | null;
  calculationText?: string | null;
  formatPattern?: string | null;
  width: number;
  sortOrder: number;
  isVisible: boolean;
}

export interface ReportFilter {
  id: string;
  fieldCode: string;
  operator: ReportFilterOperator;
  value?: string | null;
  valueTo?: string | null;
  /** Stored but NOT honored: the SQL builder ANDs every filter. Do not offer an OR. */
  logicalOperator?: string | null;
  isParameter: boolean;
}

export interface ReportGrouping {
  id: string;
  fieldCode: string;
  sortOrder: number;
}

export interface ReportSorting {
  id: string;
  fieldCode: string;
  direction: SortDirection;
  sortOrder: number;
}

export interface ReportRelationship {
  id: string;
  reportDefinitionId: string;
  sourceObjectId: string;
  targetObjectId: string;
  /** A field on the SOURCE object. The join is always FK→PK. */
  joinField: string;
  joinType: JoinType;
  sortOrder: number;
}

export interface ReportTag {
  id: string;
  name: string;
  color?: string | null;
}

export interface ReportFolder {
  id: string;
  nameEn: string;
  nameAr: string;
  parentFolderId?: string | null;
}

export interface ReportShare {
  id: string;
  reportDefinitionId: string;
  sharedWithUserId?: string | null;
  sharedWithDepartmentId?: string | null;
  sharedWithRoleId?: string | null;
  canEdit: boolean;
  sharedAt: string;
}

export interface ReportDefinition {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  description?: string | null;
  reportType: ReportType;
  scope: ReportScope;
  ownerId?: string | null;
  primaryObjectId: string;
  isPublished: boolean;
  version: number;
  folderId?: string | null;
  fields: ReportField[];
  filters: ReportFilter[];
  groupings: ReportGrouping[];
  sortings: ReportSorting[];
  relationships: ReportRelationship[];
  tags: ReportTag[];
  /** Per-caller, not per-report. */
  isFavorite: boolean;
  isPinned: boolean;
}

/**
 * The reports pagination envelope. Payroll uses a different one (`page`/`total`) — never import
 * `Paged<T>` from payroll.ts here, it silently yields undefined totals.
 */
export interface PaginatedReports {
  items: ReportDefinition[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

// ── Run result ─────────────────────────────────────────────────────────────────────

export interface ReportColumn {
  code: string;
  /** Populated from DisplayNameAr only — DisplayNameEn never reaches the result. */
  label: string;
  type: FieldKind;
  isMeasure: boolean;
  aggregation?: AggregationType | null;
  formatPattern?: string | null;
}

/** A row is keyed by field code. */
export type ReportRow = Record<string, unknown>;

export interface ReportGroup {
  fieldCode: string;
  key: unknown;
  label: string;
  /** A group has either subGroups (non-leaf) or rows (leaf), never both. */
  subGroups: ReportGroup[];
  rows: ReportRow[];
  /** Keyed by the measure column's `code`. */
  aggregates: Record<string, number>;
  count: number;
}

export interface ReportResult {
  reportCode: string;
  columns: ReportColumn[];
  /** Populated only when the report has groupings — mutually exclusive with `rows`. */
  groups: ReportGroup[];
  /** The flat page, populated only when there are no groupings. */
  rows: ReportRow[];
  grandTotals: Record<string, number>;
  totalCount: number;
  page: number;
  pageSize: number;
  /** True when the report exceeded the 5000-row cap. Surface this — do not page silently over it. */
  truncated: boolean;
}

/** Values for filters with `isParameter`, keyed by field code. A Between filter's upper bound is `<fieldCode>:to`. */
export type ReportParameters = Record<string, string | null>;

// ── Catalog ────────────────────────────────────────────────────────────────────────

export interface EnumOption {
  value: number;
  label: string;
}

export interface CatalogField {
  code: string;
  nameEn: string;
  nameAr: string;
  fieldType: FieldKind;
  isMeasure: boolean;
  isGroupable: boolean;
  isFilterable: boolean;
  isDate: boolean;
  isReference: boolean;
  referenceObjectCode?: string | null;
  options?: EnumOption[] | null;
}

export interface CatalogObject {
  code: string;
  nameEn: string;
  nameAr: string;
  module: string;
  icon?: string | null;
  hasTenantScope: boolean;
  hasSoftDelete: boolean;
  hasDateCreated: boolean;
  fieldCount: number;
  fields: CatalogField[];
}

/** A row in the ObjectDefinition registry. `id` is what the report entities reference. */
export interface ObjectDefinition {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  module: string;
  tableName: string;
  isSystem: boolean;
  isActive: boolean;
}

export type ExportFormat = "excel" | "csv" | "pdf" | "sif";

// ── Definition CRUD ────────────────────────────────────────────────────────────────

export interface GetReportsParams {
  page?: number;
  pageSize?: number;
  search?: string;
  scope?: ReportScope;
  view?: "favorites" | "recent" | "pinned";
  folderId?: string;
  tagId?: string;
}

/** Fetch the visible (access-filtered) report definitions. */
export async function getReports(params: GetReportsParams = {}): Promise<PaginatedReports> {
  const q = new URLSearchParams();
  if (params.page) q.set("pageNumber", String(params.page));
  if (params.pageSize) q.set("pageSize", String(params.pageSize));
  if (params.search) q.set("search", params.search);
  if (params.scope) q.set("scope", params.scope);
  if (params.view) q.set("view", params.view);
  if (params.folderId) q.set("folderId", params.folderId);
  if (params.tagId) q.set("tagId", params.tagId);
  const qs = q.toString();
  return apiFetch<PaginatedReports>(`/api/platform/reports${qs ? `?${qs}` : ""}`);
}

export const getReport = (id: string) => apiFetch<ReportDefinition>(`/api/platform/reports/${id}`);

export interface CreateReportInput {
  code: string;
  nameEn: string;
  nameAr: string;
  description?: string | null;
  reportType: ReportType;
  scope: ReportScope;
  primaryObjectId: string;
  templateId?: string | null;
}

/** `code` and `primaryObjectId` are immutable after create — UpdateReportCommand excludes them. */
export const createReport = (input: CreateReportInput) =>
  apiFetch<ReportDefinition>("/api/platform/reports", { method: "POST", body: input });

export interface UpdateReportInput {
  nameEn: string;
  nameAr: string;
  description?: string | null;
  reportType: ReportType;
  scope: ReportScope;
}

export const updateReport = (id: string, input: UpdateReportInput) =>
  apiFetch<ReportDefinition>(`/api/platform/reports/${id}`, { method: "PUT", body: { id, ...input } });

export const deleteReport = (id: string) =>
  apiFetch<void>(`/api/platform/reports/${id}`, { method: "DELETE" });

export const publishReport = (id: string) =>
  apiFetch<ReportDefinition>(`/api/platform/reports/${id}/publish`, { method: "POST" });

export const cloneReport = (id: string, input: { newCode: string; nameEn: string; nameAr: string }) =>
  apiFetch<ReportDefinition>(`/api/platform/reports/${id}/clone`, {
    method: "POST",
    body: { sourceReportId: id, ...input },
  });

// ── Children: add + delete only. There is no update endpoint — editing is delete-then-re-add. ──

export interface AddFieldInput {
  fieldType: ReportFieldType;
  objectDefinitionId?: string | null;
  fieldCode: string;
  displayNameEn: string;
  displayNameAr: string;
  aggregation?: AggregationType | null;
  /** Formula as authored. Compiled to the AST on write; wins over calculationExpression. */
  calculationText?: string | null;
  calculationExpression?: string | null;
  formatPattern?: string | null;
  width?: number;
  sortOrder?: number;
  isVisible?: boolean;
}

export const addReportField = (reportId: string, input: AddFieldInput) =>
  apiFetch<ReportField>(`/api/platform/reports/${reportId}/fields`, {
    method: "POST",
    body: { reportDefinitionId: reportId, ...input },
  });

export const deleteReportField = (fieldId: string) =>
  apiFetch<void>(`/api/platform/reports/fields/${fieldId}`, { method: "DELETE" });

export interface AddFilterInput {
  fieldCode: string;
  operator: ReportFilterOperator;
  value?: string | null;
  valueTo?: string | null;
  isParameter?: boolean;
}

export const addReportFilter = (reportId: string, input: AddFilterInput) =>
  apiFetch<ReportFilter>(`/api/platform/reports/${reportId}/filters`, {
    method: "POST",
    body: { reportDefinitionId: reportId, ...input },
  });

export const deleteReportFilter = (filterId: string) =>
  apiFetch<void>(`/api/platform/reports/filters/${filterId}`, { method: "DELETE" });

export const addReportGrouping = (reportId: string, input: { fieldCode: string; sortOrder: number }) =>
  apiFetch<ReportGrouping>(`/api/platform/reports/${reportId}/groupings`, {
    method: "POST",
    body: { reportDefinitionId: reportId, ...input },
  });

export const deleteReportGrouping = (groupingId: string) =>
  apiFetch<void>(`/api/platform/reports/groupings/${groupingId}`, { method: "DELETE" });

export const addReportSorting = (
  reportId: string,
  input: { fieldCode: string; direction: SortDirection; sortOrder: number },
) =>
  apiFetch<ReportSorting>(`/api/platform/reports/${reportId}/sortings`, {
    method: "POST",
    body: { reportDefinitionId: reportId, ...input },
  });

export const deleteReportSorting = (sortingId: string) =>
  apiFetch<void>(`/api/platform/reports/sortings/${sortingId}`, { method: "DELETE" });

// ── Relationships (joins) ──────────────────────────────────────────────────────────

export const getReportRelationships = (reportId: string) =>
  apiFetch<ReportRelationship[]>(`/api/platform/reports/${reportId}/relationships`);

export interface AddRelationshipInput {
  sourceObjectId: string;
  targetObjectId: string;
  /** Must be a field on the SOURCE object. */
  joinField: string;
  joinType: JoinType;
  /** Alias assignment walks in sortOrder: a source must be the primary object or an
   *  already-introduced target at a strictly lower sortOrder. */
  sortOrder: number;
}

export const addReportRelationship = (reportId: string, input: AddRelationshipInput) =>
  apiFetch<ReportRelationship>(`/api/platform/reports/${reportId}/relationships`, {
    method: "POST",
    body: { reportDefinitionId: reportId, ...input },
  });

export const deleteReportRelationship = (relationshipId: string) =>
  apiFetch<void>(`/api/platform/reports/relationships/${relationshipId}`, { method: "DELETE" });

// ── Run ────────────────────────────────────────────────────────────────────────────

export const runReport = (
  id: string,
  opts: { page?: number; pageSize?: number; parameters?: ReportParameters } = {},
) => {
  const q = new URLSearchParams();
  if (opts.page) q.set("page", String(opts.page));
  if (opts.pageSize) q.set("pageSize", String(opts.pageSize));
  const qs = q.toString();
  return apiFetch<ReportResult>(`/api/platform/reports/${id}/run${qs ? `?${qs}` : ""}`, {
    method: "POST",
    body: { parameters: opts.parameters ?? null },
  });
};

// ── Folders ────────────────────────────────────────────────────────────────────────

export const getReportFolders = () => apiFetch<ReportFolder[]>("/api/platform/reports/folders");

export const createReportFolder = (input: { nameEn: string; nameAr: string; parentFolderId?: string | null }) =>
  apiFetch<ReportFolder>("/api/platform/reports/folders", { method: "POST", body: input });

export const updateReportFolder = (
  folderId: string,
  input: { nameEn: string; nameAr: string; parentFolderId?: string | null },
) =>
  apiFetch<ReportFolder>(`/api/platform/reports/folders/${folderId}`, {
    method: "PUT",
    body: { id: folderId, ...input },
  });

export const deleteReportFolder = (folderId: string) =>
  apiFetch<void>(`/api/platform/reports/folders/${folderId}`, { method: "DELETE" });

export const setReportFolder = (reportId: string, folderId: string | null) =>
  apiFetch<void>(`/api/platform/reports/${reportId}/folder`, {
    method: "PUT",
    body: { reportDefinitionId: reportId, folderId },
  });

// ── Tags ───────────────────────────────────────────────────────────────────────────

export const getReportTags = () => apiFetch<ReportTag[]>("/api/platform/reports/tags");

export const createReportTag = (input: { name: string; color?: string | null }) =>
  apiFetch<ReportTag>("/api/platform/reports/tags", { method: "POST", body: input });

export const deleteReportTag = (tagId: string) =>
  apiFetch<void>(`/api/platform/reports/tags/${tagId}`, { method: "DELETE" });

export const assignReportTag = (reportId: string, tagId: string) =>
  apiFetch<void>(`/api/platform/reports/${reportId}/tags/${tagId}`, { method: "POST" });

export const unassignReportTag = (reportId: string, tagId: string) =>
  apiFetch<void>(`/api/platform/reports/${reportId}/tags/${tagId}`, { method: "DELETE" });

// ── Per-user state. Both return the new state. ─────────────────────────────────────

export const toggleReportFavorite = (reportId: string) =>
  apiFetch<boolean>(`/api/platform/reports/${reportId}/favorite`, { method: "POST" });

export const toggleReportPin = (reportId: string) =>
  apiFetch<boolean>(`/api/platform/reports/${reportId}/pin`, { method: "POST" });

// ── Shares ─────────────────────────────────────────────────────────────────────────

export const getReportShares = (reportId: string) =>
  apiFetch<ReportShare[]>(`/api/platform/reports/${reportId}/shares`);

export interface AddShareInput {
  sharedWithUserId?: string | null;
  sharedWithRoleId?: string | null;
  sharedWithDepartmentId?: string | null;
  canEdit: boolean;
}

export const addReportShare = (reportId: string, input: AddShareInput) =>
  apiFetch<ReportShare>(`/api/platform/reports/${reportId}/shares`, {
    method: "POST",
    body: { reportDefinitionId: reportId, ...input },
  });

export const removeReportShare = (reportId: string, shareId: string) =>
  apiFetch<void>(`/api/platform/reports/${reportId}/shares/${shareId}`, { method: "DELETE" });

// ── Formula validation (pure on the server — safe to call as the user types) ────────

export interface FormulaValidation {
  isValid: boolean;
  error?: string | null;
}

export const validateFormula = (formula: string) =>
  apiFetch<FormulaValidation>("/api/platform/reports/validate-formula", {
    method: "POST",
    body: { formula },
  });

// ── Catalog + registry ─────────────────────────────────────────────────────────────

/** Live catalog: rich field metadata (isMeasure/isGroupable/options), keyed by Code. */
export const getCatalogObjects = () => apiFetch<CatalogObject[]>("/api/platform/registry/objects");

export const getCatalogObject = (code: string) =>
  apiFetch<CatalogObject>(`/api/platform/registry/objects/${code}`);

export const getCatalogFields = (code: string) =>
  apiFetch<CatalogField[]>(`/api/platform/registry/objects/${code}/fields`);

/** Registry: the Guids the report entities reference. Join to the catalog on `code`. */
export const getObjectDefinitions = () => apiFetch<ObjectDefinition[]>("/api/platform/objects");

/**
 * The two-catalog join. Report entities reference ObjectDefinition Guids, but the rich field
 * metadata only exists on the live catalog, keyed by Code. An ObjectDefinition whose Code is
 * absent from the catalog is selectable but fails at run time, so it is dropped here rather
 * than offered as a way to build a broken report.
 */
export interface SelectableObject {
  id: string;
  code: string;
  nameAr: string;
  nameEn: string;
  module: string;
  catalog: CatalogObject;
}

export async function getSelectableObjects(): Promise<SelectableObject[]> {
  const [definitions, catalog] = await Promise.all([getObjectDefinitions(), getCatalogObjects()]);
  const byCode = new Map(catalog.map((c) => [c.code, c]));
  return definitions
    .filter((d) => d.isActive && byCode.has(d.code))
    .map((d) => ({
      id: d.id,
      code: d.code,
      nameAr: d.nameAr,
      nameEn: d.nameEn,
      module: d.module,
      catalog: byCode.get(d.code)!,
    }));
}

// ── Export (raw bytes, so it bypasses apiFetch's JSON envelope) ────────────────────

const EXT: Record<ExportFormat, string> = { excel: "xlsx", csv: "csv", pdf: "pdf", sif: "csv" };

/**
 * Download a report export as a file. The export endpoint streams raw bytes (not the JSON
 * envelope), so we fetch the blob directly with the Bearer token and trigger a browser download.
 * The server sets Content-Disposition, but we also derive a sensible fallback filename.
 *
 * Pass the viewer's current `parameters` — omitting them silently exports the stored defaults,
 * which then disagree with the table the user is looking at.
 */
export async function exportReport(
  reportId: string,
  format: ExportFormat,
  fallbackName = "report",
  parameters?: ReportParameters,
): Promise<void> {
  const token = getAccessToken();
  const q = new URLSearchParams({ format });
  for (const [key, value] of Object.entries(parameters ?? {})) {
    if (value !== null && value !== undefined) q.set(`p.${key}`, value);
  }

  const res = await fetch(`${API_BASE_URL}/api/platform/reports/${reportId}/export?${q.toString()}`, {
    method: "GET",
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  });

  if (res.status === 401) { toast.error("انتهت الجلسة. يرجى تسجيل الدخول من جديد"); throw new Error("Unauthorized"); }
  if (res.status === 403) { toast.error("ليس لديك صلاحية لتصدير التقارير"); throw new Error("Forbidden"); }
  if (res.status === 400) {
    let msg = "طلب التصدير غير صالح";
    try {
      const body = await res.clone().json();
      const raw = body?.errors?.columns?.[0] ?? body?.message ?? body?.title ?? null;
      if (raw) msg = raw;
    } catch { /* ignore parse errors, use generic message */ }
    toast.error(msg); throw new Error(`Export bad request (${msg})`);
  }
  if (!res.ok) { toast.error("تعذر تصدير التقرير"); throw new Error(`Export failed (${res.status})`); }

  // Prefer the server's filename from Content-Disposition when present.
  let filename = `${fallbackName}-${new Date().toISOString().slice(0, 10)}.${EXT[format]}`;
  const cd = res.headers.get("Content-Disposition");
  const match = cd?.match(/filename\*?=(?:UTF-8''|")?([^";]+)/i);
  if (match?.[1]) filename = decodeURIComponent(match[1].replace(/"/g, ""));

  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = filename;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// ── Schedules (mirror ReportScheduleDto) — appended by the scheduling sub-project ──

export interface ReportSchedule {
  id: string; frequency: string; cronExpression?: string | null;
  exportFormat: string; recipients: string; isActive: boolean;
  timeOfDayMinutes?: number | null; dayOfWeek?: number | null; dayOfMonth?: number | null;
  lastRunAt?: string | null; nextRunAt?: string | null;
}

export const getSchedules = (id: string) => apiFetch<ReportSchedule[]>(`/api/platform/reports/${id}/schedules`);
export const addSchedule = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportSchedule>(`/api/platform/reports/${id}/schedules`, { method: "POST", body });
export const deleteSchedule = (scheduleId: string) =>
  apiFetch<unknown>(`/api/platform/reports/schedules/${scheduleId}`, { method: "DELETE" });

// ════════════════════════════════════════════════════════════════════════════════════
//  Report Field Registry — the simple, business-friendly layer (Reports simplification).
//  Subjects = business areas; fields = friendly, permission-scoped columns keyed by an
//  opaque business key. The simple builder sends ONLY these keys — never entity names,
//  columns, joins or SQL. The server discovers the primary object + join chain.
// ════════════════════════════════════════════════════════════════════════════════════

export interface ReportSubject {
  key: string;
  labelAr: string;
  labelEn: string;
  icon: string;
  sortOrder: number;
}

export interface ReportJoinStep {
  sourceObjectCode: string;
  targetObjectCode: string;
  joinField: string;
}

/** A friendly, resolvable report field. `key` is opaque — display the label, pass back the key. */
export interface ReportFieldDescriptor {
  key: string;
  labelAr: string;
  labelEn: string;
  subject: string;
  group: string;
  dataType: FieldKind;
  objectDefinitionId: string;
  objectCode: string;
  /** The physical column. Two fields sharing it can't coexist in one R1 report — used to warn client-side. */
  propertyPath: string;
  joinPath: ReportJoinStep[];
  allowedOperators: ReportFilterOperator[];
  filterable: boolean;
  sortable: boolean;
  groupable: boolean;
  aggregatable: boolean;
  defaultAggregation?: AggregationType | null;
  isDefault: boolean;
  displayOrder: number;
  formatPattern?: string | null;
  requiredPermission: string;
}

/** Business areas the current user may report on (subjects with ≥1 visible field). */
export const getReportSubjects = () =>
  apiFetch<ReportSubject[]>("/api/platform/reports/subjects");

/** The permission-scoped friendly fields of a subject. */
export const getReportSubjectFields = (subject: string) =>
  apiFetch<ReportFieldDescriptor[]>(`/api/platform/reports/subjects/${subject}/fields`);

// ── Quick (simplified) report create — Phases 3+4 ──────────────────────────────────

export interface QuickFilterInput {
  fieldKey: string;
  operator: ReportFilterOperator;
  value?: string | null;
  valueTo?: string | null;
  isParameter?: boolean;
}

export interface QuickSortInput {
  fieldKey: string;
  direction: SortDirection;
}

export interface CreateQuickReportInput {
  nameAr: string;
  nameEn: string;
  description?: string | null;
  scope?: ReportScope;
  /** Ordered registry field keys to show as columns. */
  fieldKeys: string[];
  filters?: QuickFilterInput[];
  groupByKeys?: string[];
  sorts?: QuickSortInput[];
}

export interface QuickReportResult {
  report: ReportDefinition;
  /** Fields dropped because they collided or couldn't be joined (surface to the user). */
  skippedFieldKeys: string[];
  skippedFilterKeys: string[];
  unknownKeys: string[];
}

/** Build a runnable report from friendly field keys. The server discovers joins automatically. */
export const createQuickReport = (input: CreateQuickReportInput) =>
  apiFetch<QuickReportResult>("/api/platform/reports/quick", { method: "POST", body: input });

// ── System / standard reports seeding — Phase 2 (idempotent, per tenant) ────────────

export interface SeedSystemReportsResult {
  created: number;
  skipped: number;
  codes: string[];
}

export const seedSystemReports = () =>
  apiFetch<SeedSystemReportsResult>("/api/platform/reports/seed-system", { method: "POST" });

/**
 * Register the main HR entities + the generic MasterDataItem table as reportable ObjectDefinitions.
 * Idempotent. Must run once (per tenant) before master-data references (leave type, nationality,
 * request type, …) resolve to their real names instead of Guids. Returns the number newly added.
 */
export const seedReportableObjects = () =>
  apiFetch<number>("/api/platform/objects/seed-reportable", { method: "POST" });
