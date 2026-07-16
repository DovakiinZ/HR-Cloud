// ──────────────────────────────────────────────────────────────────────────────
//  Dashboard Platform types — mirror the backend Platform DTOs and the
//  object-driven Widget Data engine. Widget query specs reference objects/fields by
//  canonical code only, so any object discovered by the registry works here too.
// ──────────────────────────────────────────────────────────────────────────────

// ── Object / Property Registry catalog ──
export interface CatalogField {
  code: string;
  nameEn: string;
  nameAr: string;
  fieldType: string;
  isMeasure: boolean;
  isGroupable: boolean;
  isFilterable: boolean;
  isDate: boolean;
  isReference: boolean;
  referenceObjectCode?: string | null;
  options?: { value: number; label: string }[] | null;
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

// ── Widget query spec (stored in widget Configuration JSONB) ──
export type AggregationName = "Count" | "Sum" | "Average" | "Min" | "Max" | "DistinctCount" | "Percentage" | "Formula";

export interface WidgetFilterSpec {
  field: string;
  operator: string; // eq|ne|gt|gte|lt|lte|contains|startsWith|in|between|last_n_days|is_null|not_null
  value?: string | null;
}

export interface WidgetMeasure {
  name: string;
  aggregation: AggregationName;
  aggregationField?: string | null;
  filters?: WidgetFilterSpec[];
}

export interface WidgetQuerySpec {
  objectCode: string;
  aggregation: AggregationName;
  aggregationField?: string | null;
  groupByField?: string | null;
  dateGranularity?: string | null;
  visualization?: string | null;
  limit?: number | null;
  requiredPermission?: string | null;
  filters: WidgetFilterSpec[];
  measures?: WidgetMeasure[];
  formula?: string;
}

export interface WidgetSuggestion {
  spec: WidgetQuerySpec;
  visualization: string;
  titleAr: string;
  explanation: string;
}

export interface ReadyTemplate {
  key: string;
  nameAr: string;
  nameEn: string;
  description: string;
  icon: string;
}

// ── Execution result ──
export interface SeriesPoint { key: string; label: string; value: number }
export interface TableColumn { code: string; label: string; type: string }

export interface WidgetDataResult {
  kind: "scalar" | "series" | "table";
  objectCode: string;
  aggregation: string;
  groupByField?: string | null;
  value?: number | null;
  series: SeriesPoint[];
  columns: TableColumn[];
  rows: Record<string, unknown>[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// ── Visualization types (must match backend WidgetType enum values) ──
export const WIDGET_TYPE = {
  KpiCard: 1,
  Table: 2,
  BarChart: 3,
  LineChart: 4,
  PieChart: 5,
  DonutChart: 6,
  TrendChart: 7,
  ProgressWidget: 8,
  ActivityFeed: 9,
  CalendarWidget: 10,
} as const;

export type WidgetTypeName = keyof typeof WIDGET_TYPE;

// Extra visualization names not in the storage enum → nearest enum for persistence.
// Rendering is driven by spec.visualization, so the stored enum is only a coarse hint.
const VIS_TO_ENUM: Record<string, number> = {
  Gauge: WIDGET_TYPE.KpiCard,
  Leaderboard: WIDGET_TYPE.Table,
  HorizontalBar: WIDGET_TYPE.BarChart,
  AreaChart: WIDGET_TYPE.TrendChart,
};

export const widgetTypeId = (name: string): number =>
  (WIDGET_TYPE as Record<string, number>)[name] ?? VIS_TO_ENUM[name] ?? WIDGET_TYPE.KpiCard;

export const DASHBOARD_SCOPE = { Personal: 1, Department: 2, Company: 3, Shared: 4 } as const;

// ── Dashboard / widget entities (read shapes from the API) ──
export interface WidgetLayout { id?: string; column: number; row: number; width: number; height: number }

export interface DashboardWidget {
  id: string;
  widgetType: string; // enum name on read
  objectDefinitionId?: string | null;
  titleEn: string;
  titleAr: string;
  configuration?: string | null; // JSON WidgetQuerySpec
  dataSourceConfig?: string | null;
  sortOrder: number;
  isVisible: boolean;
  layout?: WidgetLayout | null;
}

export interface DashboardShare {
  id: string;
  sharedWithUserId?: string | null;
  sharedWithDepartmentId?: string | null;
  sharedWithRoleId?: string | null;
  canEdit: boolean;
  sharedAt: string;
}

export interface DashboardDefinition {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  description?: string | null;
  categoryId?: string | null;
  scope: string;
  ownerId?: string | null;
  isDefault: boolean;
  isSystem: boolean;
  isActive: boolean;
  layoutConfiguration?: string | null;
  sortOrder: number;
  widgets: DashboardWidget[];
  shares?: DashboardShare[];
}

export interface DashboardTemplate {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  description?: string | null;
  defaultScope: string;
  isSystem: boolean;
  sortOrder: number;
}

// Helper: parse a widget's stored spec safely.
export function parseWidgetSpec(w: DashboardWidget): WidgetQuerySpec | null {
  if (!w.configuration) return null;
  try {
    const spec = JSON.parse(w.configuration) as WidgetQuerySpec;
    if (!spec.objectCode) return null;
    if (!spec.filters) spec.filters = [];
    return spec;
  } catch {
    return null;
  }
}
