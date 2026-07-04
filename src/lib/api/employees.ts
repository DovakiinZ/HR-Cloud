import { apiFetch, API_BASE_URL } from "../api-client";
import { getAccessToken } from "../auth-storage";
import { fileUrl } from "./files";
import { Employee } from "@/types";

// Per-employee allowance override.
export interface ApiEmployeeAllowance {
  id: string;
  allowanceTypeId: string;
  allowanceType?: string | null;
  allowanceTypeAr?: string | null;
  amount: number;
  isActive: boolean;
}

// Per-employee salary addition / deduction (type id + bilingual name + amount).
export interface ApiEmployeeCompItem {
  id: string;
  typeId: string;
  type?: string | null;
  typeAr?: string | null;
  code?: string | null;
  amount: number;
  isActive: boolean;
}

// Raw backend DTO (camelCase JSON). Governed fields are master-data / Core references
// (ids) resolved to Arabic/English labels by the API.
export interface ApiEmployee {
  id: string;
  employeeNumber: string;
  firstName: string;
  firstNameAr?: string | null;
  lastName: string;
  lastNameAr?: string | null;
  fullName: string;
  fullNameAr?: string | null;
  email: string;
  phone?: string | null;
  gender: string;
  genderAr: string;
  dateOfBirth: string;
  nationalId?: string | null;
  nationalityId?: string | null;
  nationality?: string | null;
  nationalityAr?: string | null;
  status: string;
  statusAr: string;
  contractTypeId?: string | null;
  contractType?: string | null;
  contractTypeAr?: string | null;
  employmentTypeId?: string | null;
  employmentType?: string | null;
  employmentTypeAr?: string | null;
  hireDate: string;
  terminationDate?: string | null;
  jobTitleId?: string | null;
  jobTitle?: string | null;
  jobTitleAr?: string | null;
  departmentId?: string | null;
  departmentName?: string | null;
  branchId?: string | null;
  branchName?: string | null;
  managerId?: string | null;
  managerName?: string | null;
  address?: string | null;
  city?: string | null;
  emergencyContactName?: string | null;
  emergencyContactPhone?: string | null;
  basicSalary: number;
  currency?: string | null;
  paymentMethodId?: string | null;
  paymentMethod?: string | null;
  paymentMethodAr?: string | null;
  paymentMethodCode?: string | null;
  bankId?: string | null;
  bank?: string | null;
  bankAr?: string | null;
  bankAccountNumber?: string | null;
  iban?: string | null;
  salaryCardNumber?: string | null;
  cardProvider?: string | null;
  workLocationId?: string | null;
  workLocation?: string | null;
  workLocationAr?: string | null;
  leavePolicyId?: string | null;
  leavePolicy?: string | null;
  leavePolicyAr?: string | null;
  payrollGroupId?: string | null;
  payrollGroup?: string | null;
  payrollGroupAr?: string | null;
  photoUrl?: string | null;
  notes?: string | null;
  allowances: ApiEmployeeAllowance[];
  additions: ApiEmployeeCompItem[];
  deductions: ApiEmployeeCompItem[];
  // Backend-computed salary breakdown.
  totalAllowances: number;
  totalAdditions: number;
  totalDeductions: number;
  gosiRate: number;
  gosiAmount: number;
  gosiEnabled: boolean;
  gosiRateOverride: number | null;
  grossSalary: number;
  netSalary: number;
  createdAt: string;
}

interface PaginatedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface EmployeeAllowanceInput {
  allowanceTypeId: string;
  amount: number;
}

export interface EmployeeCompItemInput {
  typeId: string;
  amount: number;
}

// Full form input — governed selects carry ids, not free text.
export interface EmployeeInput {
  employeeNumber?: string;
  firstNameAr: string;
  lastNameAr: string;
  firstName: string;   // English
  lastName: string;    // English
  nationalId?: string;
  phone?: string;
  email: string;
  dateOfBirth: string;
  gender: string;      // "ذكر" | "أنثى"
  nationalityId?: string;
  jobTitleId?: string;
  contractTypeId?: string;
  employmentTypeId?: string;
  departmentId?: string;
  branchId?: string;
  managerId?: string;
  joinDate: string;
  status?: string;     // edit only — backend statusAr string
  address?: string;
  city?: string;
  emergencyContactName?: string;
  emergencyContactPhone?: string;
  salary: number;
  currency?: string;
  gosiEnabled?: boolean;
  gosiRateOverride?: number | null;
  paymentMethodId?: string;
  bankId?: string;
  bankAccountNumber?: string;
  iban?: string;
  salaryCardNumber?: string;
  cardProvider?: string;
  workLocationId?: string;
  leavePolicyId?: string;
  payrollGroupId?: string;
  photoUrl?: string;
  notes?: string;
  allowances: EmployeeAllowanceInput[];
  additions: EmployeeCompItemInput[];
  deductions: EmployeeCompItemInput[];
}

const GENDER_TO_INT: Record<string, number> = { "ذكر": 1, "أنثى": 2 };
const STATUS_TO_INT: Record<string, number> = {
  "نشط": 1, "في إجازة": 2, "موقوف": 3, "منتهي": 4, "مستقيل": 5,
};

// Postgres timestamptz columns require UTC — send full ISO with Z.
function toIsoUtc(dateStr: string): string {
  const d = new Date(dateStr);
  return isNaN(d.getTime()) ? new Date().toISOString() : d.toISOString();
}

// Empty string → null so the backend (Guid?) doesn't choke on "".
function idOrNull(v?: string): string | null {
  return v && v.trim() ? v : null;
}

// Map backend DTO -> Arabic display Employee (keeps table/list UI intact).
export function toDisplayEmployee(a: ApiEmployee): Employee {
  return {
    id: a.id,
    employeeId: a.employeeNumber,
    name: a.fullNameAr || a.fullName,
    email: a.email,
    phone: a.phone ?? "",
    nationalId: a.nationalId ?? "",
    dateOfBirth: a.dateOfBirth ? a.dateOfBirth.slice(0, 10) : "",
    gender: (a.genderAr as Employee["gender"]) || "ذكر",
    nationality: a.nationalityAr || a.nationality || "—",
    department: a.departmentName ?? "—",
    position: a.jobTitleAr || a.jobTitle || "—",
    joinDate: a.hireDate ? a.hireDate.slice(0, 10) : "",
    contractType: (a.contractTypeAr || a.contractType || "—") as Employee["contractType"],
    salary: a.basicSalary,
    status: (a.statusAr as Employee["status"]) || "نشط",
    leaveBalance: 0,
    attendanceRate: 0,
    avatar: fileUrl(a.photoUrl),
    jobTitleId: a.jobTitleId ?? null,
    nationalityId: a.nationalityId ?? null,
    contractTypeId: a.contractTypeId ?? null,
    departmentId: a.departmentId ?? null,
    branchId: a.branchId ?? null,
    managerId: a.managerId ?? null,
  };
}

function toPayload(input: EmployeeInput) {
  const firstAr = input.firstNameAr.trim();
  const lastAr = input.lastNameAr.trim();
  const firstEn = input.firstName.trim() || firstAr;
  const lastEn = input.lastName.trim() || lastAr;
  return {
    firstName: firstEn,
    firstNameAr: firstAr || firstEn,
    lastName: lastEn,
    lastNameAr: lastAr || lastEn,
    email: input.email,
    phone: input.phone || null,
    gender: GENDER_TO_INT[input.gender] ?? 1,
    dateOfBirth: toIsoUtc(input.dateOfBirth),
    nationalId: input.nationalId || null,
    nationalityId: idOrNull(input.nationalityId),
    contractTypeId: idOrNull(input.contractTypeId),
    employmentTypeId: idOrNull(input.employmentTypeId),
    jobTitleId: idOrNull(input.jobTitleId),
    departmentId: idOrNull(input.departmentId),
    branchId: idOrNull(input.branchId),
    managerId: idOrNull(input.managerId),
    address: input.address || null,
    city: input.city || null,
    emergencyContactName: input.emergencyContactName || null,
    emergencyContactPhone: input.emergencyContactPhone || null,
    basicSalary: input.salary,
    currency: input.currency || "SAR",
    gosiEnabled: input.gosiEnabled ?? true,
    gosiRateOverride: input.gosiRateOverride ?? null,
    paymentMethodId: idOrNull(input.paymentMethodId),
    bankId: idOrNull(input.bankId),
    bankAccountNumber: input.bankAccountNumber || null,
    iban: input.iban || null,
    salaryCardNumber: input.salaryCardNumber || null,
    cardProvider: input.cardProvider || null,
    workLocationId: idOrNull(input.workLocationId),
    leavePolicyId: idOrNull(input.leavePolicyId),
    payrollGroupId: idOrNull(input.payrollGroupId),
    photoUrl: input.photoUrl || null,
    notes: input.notes || null,
    allowances: (input.allowances || []).filter((a) => a.allowanceTypeId).map((a) => ({
      allowanceTypeId: a.allowanceTypeId,
      amount: Number(a.amount) || 0,
    })),
    additions: (input.additions || []).filter((a) => a.typeId).map((a) => ({ typeId: a.typeId, amount: Number(a.amount) || 0 })),
    deductions: (input.deductions || []).filter((a) => a.typeId).map((a) => ({ typeId: a.typeId, amount: Number(a.amount) || 0 })),
  };
}

export async function getEmployees(params?: {
  search?: string; pageNumber?: number; pageSize?: number; includeTerminated?: boolean;
}): Promise<Employee[]> {
  const q = new URLSearchParams();
  q.set("pageNumber", String(params?.pageNumber ?? 1));
  q.set("pageSize", String(params?.pageSize ?? 200));
  if (params?.search) q.set("search", params.search);
  if (params?.includeTerminated) q.set("includeTerminated", "true");
  const result = await apiFetch<PaginatedResult<ApiEmployee>>(`/api/employees?${q.toString()}`);
  return (result?.items ?? []).map(toDisplayEmployee);
}

export async function getEmployee(id: string): Promise<Employee> {
  const a = await apiFetch<ApiEmployee>(`/api/employees/${id}`);
  return toDisplayEmployee(a);
}

// Full raw record — used by the profile + edit form which need every field.
export async function getEmployeeRaw(id: string): Promise<ApiEmployee> {
  return apiFetch<ApiEmployee>(`/api/employees/${id}`);
}

export async function createEmployee(input: EmployeeInput): Promise<ApiEmployee> {
  const payload = {
    employeeNumber: input.employeeNumber?.trim() || `EMP-${Date.now().toString().slice(-6)}`,
    ...toPayload(input),
  };
  return apiFetch<ApiEmployee>("/api/employees", { method: "POST", body: payload });
}

export async function updateEmployee(id: string, input: EmployeeInput): Promise<ApiEmployee> {
  const payload = {
    id,
    ...toPayload(input),
    status: STATUS_TO_INT[input.status ?? "نشط"] ?? 1,
  };
  return apiFetch<ApiEmployee>(`/api/employees/${id}`, { method: "PUT", body: payload });
}

export async function deleteEmployee(id: string): Promise<void> {
  await apiFetch<unknown>(`/api/employees/${id}`, { method: "DELETE" });
}

export interface EmployeeExportRequest {
  groups: string[];                 // personal | employment | salary | bank | leave | attendance
  departmentId?: string | null;
  jobTitleId?: string | null;
  branchId?: string | null;
  status?: string | null;
  ids?: string[];
}

// Download the employees .xlsx (auth fetch; backend gates salary/bank by permission).
export async function exportEmployees(body: EmployeeExportRequest): Promise<void> {
  const token = getAccessToken();
  const res = await fetch(`${API_BASE_URL}/api/employees/export`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error("تعذر تصدير الموظفين");
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = `employees-${new Date().toISOString().slice(0, 10)}.xlsx`;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
