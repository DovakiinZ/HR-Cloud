// Data layer for the Dashboard Platform. Talks to the live Platform API.
import { apiFetch, API_BASE_URL } from "../api-client";
import { getAccessToken } from "../auth-storage";
import { toast } from "sonner";
import {
  CatalogObject,
  CatalogField,
  DashboardDefinition,
  DashboardTemplate,
  DashboardWidget,
  ReadyTemplate,
  WidgetDataResult,
  WidgetFilterSpec,
  WidgetQuerySpec,
  WidgetSuggestion,
  widgetTypeId,
} from "@/types/dashboard";

interface Paginated<T> { items: T[]; pageNumber: number; pageSize: number; totalCount: number }

// ── Object / Property Registry catalog ──
export const getCatalog = () =>
  apiFetch<CatalogObject[]>("/api/platform/registry/objects");

export const getObjectFields = (code: string) =>
  apiFetch<CatalogField[]>(`/api/platform/registry/objects/${encodeURIComponent(code)}/fields`);

// ── Dashboards ──
export async function listDashboards(): Promise<DashboardDefinition[]> {
  const res = await apiFetch<Paginated<DashboardDefinition>>("/api/platform/dashboards?pageNumber=1&pageSize=100");
  return res?.items ?? [];
}

export const getDashboard = (id: string) =>
  apiFetch<DashboardDefinition>(`/api/platform/dashboards/${id}`);

export const getMyDashboards = (userId: string) =>
  apiFetch<DashboardDefinition[]>(`/api/platform/dashboards/my/${userId}`);

export const createDashboard = (payload: {
  code: string; nameEn: string; nameAr: string; description?: string; scope: number;
}) => apiFetch<DashboardDefinition>("/api/platform/dashboards", { method: "POST", body: payload });

export const deleteDashboard = (id: string) =>
  apiFetch<unknown>(`/api/platform/dashboards/${id}`, { method: "DELETE" });

export const cloneDashboard = (id: string, payload: { code: string; nameEn: string; nameAr: string }) =>
  apiFetch<DashboardDefinition>(`/api/platform/dashboards/${id}/clone`, { method: "POST", body: payload });

export const seedDefaultDashboard = () =>
  apiFetch<string>("/api/platform/dashboards/seed-defaults", { method: "POST" });

export const getTemplates = () =>
  apiFetch<DashboardTemplate[]>("/api/platform/dashboards/templates");

export const getReadyTemplates = () =>
  apiFetch<ReadyTemplate[]>("/api/platform/dashboards/ready-templates");

export const seedTemplate = (key: string) =>
  apiFetch<string>(`/api/platform/dashboards/seed-template/${encodeURIComponent(key)}`, { method: "POST" });

// ── AI builder ──
export const aiSuggestWidget = (prompt: string) =>
  apiFetch<WidgetSuggestion>("/api/platform/dashboards/widget-data/ai-suggest", {
    method: "POST",
    body: { prompt },
  });

// ── Sharing ──
export const shareDashboard = (
  id: string,
  payload: { sharedWithUserId?: string | null; sharedWithRoleId?: string | null; sharedWithDepartmentId?: string | null; canEdit?: boolean },
) => apiFetch<unknown>(`/api/platform/dashboards/${id}/share`, { method: "POST", body: payload });

export const revokeShare = (shareId: string) =>
  apiFetch<unknown>(`/api/platform/dashboards/shares/${shareId}`, { method: "DELETE" });

// ── Widgets ──
export function addWidget(
  dashboardId: string,
  payload: {
    widgetType: number; titleEn: string; titleAr: string; configuration: string;
    sortOrder?: number; layout?: { column: number; row: number; width: number; height: number };
  },
): Promise<DashboardWidget> {
  return apiFetch<DashboardWidget>(`/api/platform/dashboards/${dashboardId}/widgets`, {
    method: "POST",
    body: { dashboardDefinitionId: dashboardId, sortOrder: 0, ...payload },
  });
}

export function updateWidget(
  widgetId: string,
  payload: { widgetType: number; titleEn: string; titleAr: string; configuration: string; sortOrder?: number; isVisible?: boolean },
): Promise<DashboardWidget> {
  return apiFetch<DashboardWidget>(`/api/platform/dashboards/widgets/${widgetId}`, {
    method: "PUT",
    body: { id: widgetId, sortOrder: 0, isVisible: true, ...payload },
  });
}

export const deleteWidget = (widgetId: string) =>
  apiFetch<unknown>(`/api/platform/dashboards/widgets/${widgetId}`, { method: "DELETE" });

export function saveLayout(
  dashboardId: string,
  widgetLayouts: { widgetId: string; column: number; row: number; width: number; height: number }[],
): Promise<unknown> {
  return apiFetch<unknown>(`/api/platform/dashboards/${dashboardId}/layout`, {
    method: "PUT",
    body: {
      dashboardDefinitionId: dashboardId,
      layoutConfiguration: JSON.stringify({ cols: 12 }),
      widgetLayouts,
    },
  });
}

// Build the AddWidget payload from a builder spec + visualization name.
export function widgetPayloadFromSpec(
  visualization: string,
  titleAr: string,
  titleEn: string,
  spec: WidgetQuerySpec,
  layout?: { column: number; row: number; width: number; height: number },
) {
  return {
    widgetType: widgetTypeId(visualization),
    titleEn: titleEn || titleAr,
    titleAr: titleAr || titleEn,
    configuration: JSON.stringify({ ...spec, visualization }),
    layout,
  };
}

// ── Widget data execution (the engine) ──
export const previewWidgetData = (spec: WidgetQuerySpec, dashboardFilters?: WidgetFilterSpec[]) =>
  apiFetch<WidgetDataResult>("/api/platform/dashboards/widget-data/preview", {
    method: "POST",
    body: { spec, dashboardFilters },
  });

export const executeWidget = (widgetId: string, dashboardFilters?: WidgetFilterSpec[]) =>
  apiFetch<WidgetDataResult>(`/api/platform/dashboards/widget-data/${widgetId}/execute`, {
    method: "POST",
    body: { dashboardFilters },
  });

export const drilldownWidget = (
  spec: WidgetQuerySpec,
  segmentKey: string | null,
  dashboardFilters?: WidgetFilterSpec[],
  page = 1,
  pageSize = 25,
) =>
  apiFetch<WidgetDataResult>("/api/platform/dashboards/widget-data/drilldown", {
    method: "POST",
    body: { spec, segmentKey, dashboardFilters, page, pageSize },
  });

// ── Widget export (server-side Excel / PDF / CSV) ──
export type WidgetExportFormat = "excel" | "pdf" | "csv";
const WIDGET_EXT: Record<WidgetExportFormat, string> = { excel: "xlsx", pdf: "pdf", csv: "csv" };

/** Download a widget's data as a real file (server-side Excel/PDF/CSV). Streams raw bytes. */
export async function exportWidget(widgetId: string, format: WidgetExportFormat, fallbackName = "widget"): Promise<void> {
  const token = getAccessToken();
  const res = await fetch(`${API_BASE_URL}/api/platform/dashboards/widget-data/${widgetId}/export?format=${format}`, {
    method: "GET",
    headers: { ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  });
  if (res.status === 401) { toast.error("انتهت الجلسة. يرجى تسجيل الدخول من جديد"); throw new Error("Unauthorized"); }
  if (res.status === 403) { toast.error("ليس لديك صلاحية لتصدير هذه الودجة"); throw new Error("Forbidden"); }
  if (!res.ok) { toast.error("تعذر تصدير الودجة"); throw new Error(`Export failed (${res.status})`); }

  let filename = `${fallbackName}-${new Date().toISOString().slice(0, 10)}.${WIDGET_EXT[format]}`;
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
