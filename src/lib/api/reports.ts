import { apiFetch, API_BASE_URL } from "../api-client";
import { getAccessToken } from "../auth-storage";
import { toast } from "sonner";

// Mirrors the backend ReportDefinitionDto (subset the list UI needs).
export interface ReportDefinition {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  description?: string | null;
  reportType: string;
  scope: string;
  isPublished: boolean;
  version: number;
}

export interface PaginatedReports {
  items: ReportDefinition[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export type ExportFormat = "excel" | "csv" | "pdf";

/** Fetch the visible (access-filtered) report definitions. */
export async function getReports(params: { page?: number; pageSize?: number } = {}): Promise<PaginatedReports> {
  const q = new URLSearchParams();
  if (params.page) q.set("pageNumber", String(params.page));
  if (params.pageSize) q.set("pageSize", String(params.pageSize));
  const qs = q.toString();
  return apiFetch<PaginatedReports>(`/api/platform/reports${qs ? `?${qs}` : ""}`);
}

const EXT: Record<ExportFormat, string> = { excel: "xlsx", csv: "csv", pdf: "pdf" };

/**
 * Download a report export as a file. The export endpoint streams raw bytes (not the JSON
 * envelope), so we fetch the blob directly with the Bearer token and trigger a browser download.
 * The server sets Content-Disposition, but we also derive a sensible fallback filename.
 */
export async function exportReport(reportId: string, format: ExportFormat, fallbackName = "report"): Promise<void> {
  const token = getAccessToken();
  const res = await fetch(`${API_BASE_URL}/api/platform/reports/${reportId}/export?format=${format}`, {
    method: "GET",
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  });

  if (res.status === 401) { toast.error("انتهت الجلسة. يرجى تسجيل الدخول من جديد"); throw new Error("Unauthorized"); }
  if (res.status === 403) { toast.error("ليس لديك صلاحية لتصدير التقارير"); throw new Error("Forbidden"); }
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

// ── Detail + child types (mirror the backend DTOs) ──
export interface ReportField {
  id: string; fieldType: string; objectDefinitionId?: string | null;
  fieldCode: string; displayNameEn: string; displayNameAr: string;
  aggregation?: string | null; calculationExpression?: string | null;
  formatPattern?: string | null; width: number; sortOrder: number; isVisible: boolean;
}
export interface ReportFilter {
  id: string; fieldCode: string; operator: string; value?: string | null;
  valueTo?: string | null; logicalOperator?: string | null; isParameter: boolean;
}
export interface ReportGrouping { id: string; fieldCode: string; sortOrder: number; }
export interface ReportSorting { id: string; fieldCode: string; direction: string; sortOrder: number; }

export interface ReportDetail extends ReportDefinition {
  primaryObjectId: string;
  fields: ReportField[];
  filters: ReportFilter[];
  groupings?: ReportGrouping[];
  sortings?: ReportSorting[];
}

// ── Run result (mirror ReportResult / ReportColumn / ReportGroup) ──
export interface ReportColumn { code: string; label: string; type: string; isMeasure: boolean; aggregation?: string | null; formatPattern?: string | null; }
export interface ReportGroup { fieldCode: string; key: unknown; label: string; subGroups: ReportGroup[]; rows: Record<string, unknown>[]; aggregates: Record<string, number>; count: number; }
export interface ReportResult {
  reportCode: string; columns: ReportColumn[]; groups: ReportGroup[];
  rows: Record<string, unknown>[]; grandTotals: Record<string, number>;
  totalCount: number; page: number; pageSize: number; truncated: boolean;
}

// ── ObjectRegistry types (mirror ObjectDefinitionDto / ObjectFieldDto) ──
export interface ObjectDefinitionField {
  id: string; code: string; nameEn: string; nameAr: string; fieldType: string;
  isFilterable: boolean; isSortable: boolean; isSearchable: boolean;
}
export interface ObjectDefinitionRegistry {
  id: string; code: string; nameEn: string; nameAr: string;
  module: string; tableName: string; isActive: boolean;
  fields: ObjectDefinitionField[];
}

// ── Schedules (mirror ReportScheduleDto) ──
export interface ReportSchedule {
  id: string; frequency: string; cronExpression?: string | null;
  exportFormat: string; recipients: string; isActive: boolean;
  lastRunAt?: string | null; nextRunAt?: string | null;
}

export interface CreateReportBody {
  code: string; nameEn: string; nameAr: string; description?: string;
  reportType: number; scope: number; primaryObjectId: string;
}

export const getReport = (id: string) => apiFetch<ReportDetail>(`/api/platform/reports/${id}`);
export const createReport = (body: CreateReportBody) =>
  apiFetch<ReportDetail>(`/api/platform/reports`, { method: "POST", body });
export const updateReport = (id: string, body: Omit<CreateReportBody, "code" | "primaryObjectId">) =>
  apiFetch<ReportDetail>(`/api/platform/reports/${id}`, { method: "PUT", body: { id, ...body } });
export const deleteReport = (id: string) =>
  apiFetch<unknown>(`/api/platform/reports/${id}`, { method: "DELETE" });
export const publishReport = (id: string) =>
  apiFetch<ReportDetail>(`/api/platform/reports/${id}/publish`, { method: "POST" });
export const runReport = (id: string, page = 1, pageSize = 50) =>
  apiFetch<ReportResult>(`/api/platform/reports/${id}/run?page=${page}&pageSize=${pageSize}`, { method: "POST" });

export const addField = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportField>(`/api/platform/reports/${id}/fields`, { method: "POST", body });
export const deleteField = (fieldId: string) =>
  apiFetch<unknown>(`/api/platform/reports/fields/${fieldId}`, { method: "DELETE" });
export const addFilter = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportFilter>(`/api/platform/reports/${id}/filters`, { method: "POST", body });
export const deleteFilter = (filterId: string) =>
  apiFetch<unknown>(`/api/platform/reports/filters/${filterId}`, { method: "DELETE" });
export const addGrouping = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportGrouping>(`/api/platform/reports/${id}/groupings`, { method: "POST", body });
export const deleteGrouping = (groupingId: string) =>
  apiFetch<unknown>(`/api/platform/reports/groupings/${groupingId}`, { method: "DELETE" });
export const addSorting = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportSorting>(`/api/platform/reports/${id}/sortings`, { method: "POST", body });
export const deleteSorting = (sortingId: string) =>
  apiFetch<unknown>(`/api/platform/reports/sortings/${sortingId}`, { method: "DELETE" });

export const getObjectDefinitions = () => apiFetch<ObjectDefinitionRegistry[]>("/api/platform/objects");

export const getSchedules = (id: string) => apiFetch<ReportSchedule[]>(`/api/platform/reports/${id}/schedules`);
export const addSchedule = (id: string, body: Record<string, unknown>) =>
  apiFetch<ReportSchedule>(`/api/platform/reports/${id}/schedules`, { method: "POST", body });
export const deleteSchedule = (scheduleId: string) =>
  apiFetch<unknown>(`/api/platform/reports/schedules/${scheduleId}`, { method: "DELETE" });
