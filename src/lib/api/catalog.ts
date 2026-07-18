import { apiFetch } from "../api-client";

export interface SemanticDomain { code: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; icon: string; sortOrder: number; }
export interface SemanticFieldGroup { code: string; nameAr: string; nameEn: string; sortOrder: number; }
export interface SemanticField { objectCode: string; fieldCode: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; groupCode: string; icon?: string | null; keywords: string[]; role: string; defaultVisible: boolean; }
export interface SemanticFilter { fieldCode: string; nameAr: string; nameEn: string; controlType: string; referenceObjectCode?: string | null; }
export interface SemanticSort { fieldCode: string; direction: string; }
export interface SemanticObject { objectCode: string; domainCode: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; icon: string; keywords: string[]; defaultVisible: boolean; fieldGroups: SemanticFieldGroup[]; defaultSort?: SemanticSort | null; defaultFilters: SemanticFilter[]; recommendedMetricCodes: string[]; recommendedReportCodes: string[]; recommendedWidgetCodes: string[]; fields: SemanticField[]; }
export interface SemanticMetric { code: string; nameAr: string; nameEn: string; descriptionAr: string; descriptionEn: string; icon: string; domainCode: string; requiredPermissions: string[]; defaultVisualization: string; suggestedFilterFields: string[]; }

export const getDomains = () => apiFetch<SemanticDomain[]>("/api/platform/catalog/domains");
export const getCatalogObjects = (domain?: string) => apiFetch<SemanticObject[]>(`/api/platform/catalog/objects${domain ? `?domain=${encodeURIComponent(domain)}` : ""}`);
export const getCatalogObject = (code: string) => apiFetch<SemanticObject>(`/api/platform/catalog/objects/${encodeURIComponent(code)}`);
export const getMetrics = (domain?: string) => apiFetch<SemanticMetric[]>(`/api/platform/catalog/metrics${domain ? `?domain=${encodeURIComponent(domain)}` : ""}`);
export const getMetric = (code: string) => apiFetch<SemanticMetric>(`/api/platform/catalog/metrics/${encodeURIComponent(code)}`);
