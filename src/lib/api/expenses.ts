import { apiFetch } from "../api-client";

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
