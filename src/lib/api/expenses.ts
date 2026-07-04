import { apiFetch, API_BASE_URL } from "../api-client";
import { getAccessToken } from "../auth-storage";

export interface ExpenseRecord {
  id: string;
  employeeId: string;
  employeeName?: string | null;
  category?: string | null;
  amount: number;
  currency: string;
  description?: string | null;
  receiptUrl?: string | null;
  status: string;
  decidedAt: string;
  includeInPayroll: boolean;
  payrollMonth?: string | null;
}

export const getExpenses = (scope: "mine" | "all" = "all") =>
  apiFetch<ExpenseRecord[]>(`/api/expenses${scope === "all" ? "?scope=all" : ""}`);

export interface CreateExpenseInput {
  employeeId: string;
  expenseCategoryId?: string | null;
  amount: number;
  currency?: string;
  description?: string | null;
  status?: string;
  includeInPayroll?: boolean;
  payrollMonth?: string | null;   // ISO date (1st of the target month), required if includeInPayroll
}

export const createExpense = (input: CreateExpenseInput) =>
  apiFetch<ExpenseRecord>(`/api/expenses`, {
    method: "POST",
    body: {
      employeeId: input.employeeId,
      expenseCategoryId: input.expenseCategoryId ?? null,
      amount: input.amount,
      currency: input.currency || "SAR",
      description: input.description ?? null,
      status: input.status ?? "Approved",
      includeInPayroll: input.includeInPayroll ?? false,
      payrollMonth: input.payrollMonth ?? null,
    },
  });

// Cancel an expense — removes it from payroll inclusion.
export const cancelExpense = (id: string) =>
  apiFetch<ExpenseRecord>(`/api/expenses/${id}/cancel`, { method: "POST" });

async function fetchExpenseBlob(id: string, mode: "pdf" | "download"): Promise<Blob> {
  const token = getAccessToken();
  const res = await fetch(`${API_BASE_URL}/api/expenses/${id}/${mode}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!res.ok) throw new Error("تعذر تحميل المستند");
  return res.blob();
}

/** Open the expense document PDF in a new tab (optionally invoking the print dialog). */
export async function viewExpenseDoc(id: string, print = false): Promise<void> {
  const blob = await fetchExpenseBlob(id, "pdf");
  const url = URL.createObjectURL(blob);
  const win = window.open(url, "_blank");
  if (win && print) win.addEventListener("load", () => win.print());
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** Download the expense document PDF as an attachment. */
export async function downloadExpenseDoc(id: string): Promise<void> {
  const blob = await fetchExpenseBlob(id, "download");
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `expense-${id}.pdf`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
