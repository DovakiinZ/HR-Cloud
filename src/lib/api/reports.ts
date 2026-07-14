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
