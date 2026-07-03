import { apiFetch } from "../api-client";

// ---------------------------------------------------------------------------
// Paged contract — shared by all decomposed run list endpoints
// ---------------------------------------------------------------------------

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number;
}

/** Query params accepted by every paged run sub-resource endpoint. */
export interface PagedQuery {
  page?: number;
  pageSize?: number;
  sort?: string;
  search?: string;
  filter?: string;
}

function buildPagedQs(q?: PagedQuery): string {
  if (!q) return "";
  const p = new URLSearchParams();
  if (q.page != null) p.set("page", String(q.page));
  if (q.pageSize != null) p.set("pageSize", String(q.pageSize));
  if (q.sort) p.set("sort", q.sort);
  if (q.search) p.set("search", q.search);
  if (q.filter) p.set("filter", q.filter);
  const s = p.toString();
  return s ? `?${s}` : "";
}

// ---------------------------------------------------------------------------
// PayrollRunSummary — D9 decomposed run read model (GET /api/payroll/runs/{id})
// ---------------------------------------------------------------------------

export interface RunKpis {
  includedEmployees: number;
  excludedEmployees: number;
  gross: number;
  deductions: number;
  net: number;
  transactionsConsumed: number;
  approvedNotConsumed: number;
}

export interface RunCalcMeta {
  version: number;
  at: string;
  byUserId: string;
  byUserName: string;
}

export interface RunTimelineEntry {
  fromState: string;
  toState: string;
  at: string;
  reason?: string | null;
}

export type CalculationStatus = "UpToDate" | "RecalculationRequired";

export interface PayrollRunSummary {
  id: string;
  runNumber: string;
  periodStart: string;
  periodEnd: string;
  targetPeriodYear: number;
  targetPeriodMonth: number;
  state: string;
  currency: string;
  kpis: RunKpis;
  calc: RunCalcMeta | null;
  calculationStatus: CalculationStatus;
  timeline: RunTimelineEntry[];
}

// ---------------------------------------------------------------------------
// Paged row types for decomposed sub-resource endpoints
// ---------------------------------------------------------------------------

export interface RunEmployeeRow {
  employeeId: string;
  employeeNumber: string;
  employeeName: string;
  gross: number;
  deductions: number;
  net: number;
  ledgerPosted: boolean;
  componentsJson?: string | null;
}

export interface RunExcludedRow {
  employeeId: string;
  employeeNumber: string;
  employeeName: string;
  reasonCode: string;
  detail: string;
}

export interface RunValidationRow {
  code: string;
  severity: string;
  message: string;
  suggestedAction?: string | null;
  targetModule?: string | null;
  targetScreen?: string | null;
  relatedEntityType?: string | null;
  relatedEntityId?: string | null;
  employeeId?: string | null;
}

/** The bucket a transaction falls into for run-context display. */
export type TxnBucket =
  | "PendingApproval"
  | "ApprovedNotConsumed"
  | "Consumed"
  | "Posted"
  | "Reversed"
  | "Other";

export interface RunTransactionRow {
  id: string;
  employeeId: string;
  employeeNumber: string;
  employeeName: string;
  kind: import("./payroll-transactions").TransactionKind;
  typeId: string;
  typeName: string;
  amount: number;
  effectiveDate: string;
  notes?: string | null;
  bucket: TxnBucket;
}

export interface RunCalculationRow {
  version: number;
  calculatedAt: string;
  byUserId: string;
  byUserName: string;
  employeeCount: number;
  gross: number;
  deductions: number;
  net: number;
}

export interface RunCalculationDetail extends RunCalculationRow {
  snapshotJson?: string | null;
  findingsJson?: string | null;
}

// ---------------------------------------------------------------------------
// Body type for creating a transaction from within a run
// ---------------------------------------------------------------------------

export interface CreateRunTransactionBody {
  employeeId: string;
  kind: import("./payroll-transactions").TransactionKind;
  typeId: string;
  amount: number;
  effectiveDate?: string | null;
  notes?: string | null;
}

export interface CreateRunTransactionResult {
  id: string;
}

export interface PayrollDefinitionDto {
  id: string;
  code: string;
  name: string;
  nameAr?: string | null;
  status: string;
  currentVersionId?: string | null;
  currency: string;
}

export interface PayrollRunListItem {
  id: string;
  runNumber: string;
  periodStart: string;
  periodEnd: string;
  state: string;
  currency: string;
  employeeCount: number;
  grossTotal: number;
  deductionTotal: number;
  netTotal: number;
  createdAt: string;
}

export interface PayslipDto {
  id: string;
  employeeId: string;
  employeeNumber: string;
  employeeName: string;
  currency: string;
  grossEarnings: number;
  totalDeductions: number;
  netAmount: number;
  ledgerPosted: boolean;
  componentsJson?: string | null;
}

export interface ValidationFindingDto {
  code: string;
  severity: string;
  message: string;
  employeeId?: string | null;
  employeeName?: string | null;
}

export interface RunTransitionDto { fromState: string; toState: string; at: string; reason?: string | null }

export interface PayrollRunDetail extends PayrollRunListItem {
  payrollDefinitionId: string;
  payrollDefinitionVersionId: string;
  ruleSetVersionId?: string | null;
  notes?: string | null;
  validatedAt?: string | null;
  approvedAt?: string | null;
  payslips: PayslipDto[];
  validation: ValidationFindingDto[];
  transitions: RunTransitionDto[];
}

export interface PayrollPreviewLineDto {
  employeeId: string; employeeNumber: string; employeeName: string;
  gross: number; deductions: number; net: number; hasErrors: boolean;
}
export interface PayrollPreviewDto {
  employeeCount: number; grossTotal: number; deductionTotal: number; netTotal: number; currency: string;
  isValid: boolean; findings: ValidationFindingDto[]; lines: PayrollPreviewLineDto[];
}

export const bootstrapPayroll = () => apiFetch<string>("/api/payroll/bootstrap", { method: "POST" });
export const getDefinitions = () => apiFetch<PayrollDefinitionDto[]>("/api/payroll/definitions");
export const previewPayroll = (definitionId: string, year: number, month: number) =>
  apiFetch<PayrollPreviewDto>("/api/payroll/preview", { method: "POST", body: { definitionId, year, month } });
export const listRuns = () => apiFetch<PayrollRunListItem[]>("/api/payroll/runs");
export const getRun = (id: string) => apiFetch<PayrollRunDetail>(`/api/payroll/runs/${id}`);
export const createRun = (definitionId: string, year: number, month: number) =>
  apiFetch<PayrollRunDetail>("/api/payroll/runs", { method: "POST", body: { definitionId, year, month } });
export const calculateRun = (id: string) => apiFetch<PayrollRunDetail>(`/api/payroll/runs/${id}/calculate`, { method: "POST" });
export const validateRun = (id: string) => apiFetch<PayrollRunDetail>(`/api/payroll/runs/${id}/validate`, { method: "POST" });
export const submitRun = (id: string) => apiFetch<PayrollRunDetail>(`/api/payroll/runs/${id}/submit`, { method: "POST" });
export const approveRun = (id: string) => apiFetch<PayrollRunDetail>(`/api/payroll/runs/${id}/approve`, { method: "POST" });
export const executeRun = (id: string) => apiFetch<PayrollRunDetail>(`/api/payroll/runs/${id}/execute`, { method: "POST" });
export const cancelRun = (id: string, reason: string) =>
  apiFetch<PayrollRunDetail>(`/api/payroll/runs/${id}/cancel`, { method: "POST", body: { reason } });

// ---------------------------------------------------------------------------
// Decomposed run endpoints (D9) — Tasks 19/20 consume these
// ---------------------------------------------------------------------------

/** GET /api/payroll/runs/{id} — D9 summary read model */
export const getRunSummary = (id: string): Promise<PayrollRunSummary> =>
  apiFetch<PayrollRunSummary>(`/api/payroll/runs/${id}`);

/** GET /api/payroll/runs/{id}/employees */
export const getRunEmployees = (id: string, q?: PagedQuery): Promise<Paged<RunEmployeeRow>> =>
  apiFetch<Paged<RunEmployeeRow>>(`/api/payroll/runs/${id}/employees${buildPagedQs(q)}`);

/** GET /api/payroll/runs/{id}/excluded */
export const getRunExcluded = (id: string, q?: PagedQuery): Promise<Paged<RunExcludedRow>> =>
  apiFetch<Paged<RunExcludedRow>>(`/api/payroll/runs/${id}/excluded${buildPagedQs(q)}`);

/** GET /api/payroll/runs/{id}/validation */
export const getRunValidation = (id: string, q?: PagedQuery): Promise<Paged<RunValidationRow>> =>
  apiFetch<Paged<RunValidationRow>>(`/api/payroll/runs/${id}/validation${buildPagedQs(q)}`);

/** GET /api/payroll/runs/{id}/transactions */
export const getRunTransactions = (id: string, q?: PagedQuery): Promise<Paged<RunTransactionRow>> =>
  apiFetch<Paged<RunTransactionRow>>(`/api/payroll/runs/${id}/transactions${buildPagedQs(q)}`);

/** GET /api/payroll/runs/{id}/calculations */
export const getRunCalculations = (id: string, q?: PagedQuery): Promise<Paged<RunCalculationRow>> =>
  apiFetch<Paged<RunCalculationRow>>(`/api/payroll/runs/${id}/calculations${buildPagedQs(q)}`);

/** GET /api/payroll/runs/{id}/calculations/{version} */
export const getRunCalculation = (id: string, version: number): Promise<RunCalculationDetail> =>
  apiFetch<RunCalculationDetail>(`/api/payroll/runs/${id}/calculations/${version}`);

/** POST /api/payroll/runs/{id}/transactions — create a transaction from within a run context */
export const createRunTransaction = (
  id: string,
  body: CreateRunTransactionBody,
): Promise<CreateRunTransactionResult> =>
  apiFetch<CreateRunTransactionResult>(`/api/payroll/runs/${id}/transactions`, { method: "POST", body });

export const STATE_AR: Record<string, string> = {
  Draft: "مسودة", Preview: "معاينة", Validated: "تم التحقق", PendingApproval: "بانتظار الاعتماد",
  Approved: "معتمد", Executing: "قيد التنفيذ", Completed: "مكتمل", Locked: "مقفل", Archived: "مؤرشف",
  Failed: "فشل", Cancelled: "ملغي",
};

export function money(n: number, currency = "SAR"): string {
  return `${n.toLocaleString("ar-SA", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;
}
