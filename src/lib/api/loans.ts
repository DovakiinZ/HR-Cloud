import { apiFetch, API_BASE_URL } from "../api-client";
import { getAccessToken } from "../auth-storage";

export interface LoanInstallment { dueMonth: string; amount: number; paid: boolean }

export interface LoanRecord {
  id: string;
  employeeId: string;
  employeeName?: string | null;
  loanType?: string | null;
  kind: string;        // Loan | Advance
  principal: number;
  installmentMonths: number;
  monthlyInstallment: number;
  status: string;
  startDate: string;
  installments: LoanInstallment[];
}

export const getLoans = (scope: "mine" | "all" = "all") =>
  apiFetch<LoanRecord[]>(`/api/loans${scope === "all" ? "?scope=all" : ""}`);

export interface CreateLoanInput {
  employeeId: string;
  loanTypeId?: string | null;
  kind: string;                 // Loan | Advance
  principal: number;
  installmentMonths: number;
  startMonth?: string | null;   // ISO date (1st of the month the first installment is deducted)
}

export const createLoan = (input: CreateLoanInput) =>
  apiFetch<LoanRecord>(`/api/loans`, {
    method: "POST",
    body: {
      employeeId: input.employeeId,
      loanTypeId: input.loanTypeId ?? null,
      kind: input.kind,
      principal: input.principal,
      installmentMonths: input.installmentMonths,
      startMonth: input.startMonth ?? null,
    },
  });

// Cancel (void) a loan — stops deduction of remaining installments.
export const cancelLoan = (id: string) =>
  apiFetch<LoanRecord>(`/api/loans/${id}/cancel`, { method: "POST" });

// Settle (تسوية) a loan — marks it paid off and stops further deductions.
export const settleLoan = (id: string) =>
  apiFetch<LoanRecord>(`/api/loans/${id}/settle`, { method: "POST" });

async function fetchLoanBlob(id: string, mode: "pdf" | "download"): Promise<Blob> {
  const token = getAccessToken();
  const res = await fetch(`${API_BASE_URL}/api/loans/${id}/${mode}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!res.ok) throw new Error("تعذر تحميل المستند");
  return res.blob();
}

/** Open the loan document PDF in a new tab (optionally invoking the print dialog). */
export async function viewLoanDoc(id: string, print = false): Promise<void> {
  const blob = await fetchLoanBlob(id, "pdf");
  const url = URL.createObjectURL(blob);
  const win = window.open(url, "_blank");
  if (win && print) win.addEventListener("load", () => win.print());
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

/** Download the loan document PDF as an attachment. */
export async function downloadLoanDoc(id: string): Promise<void> {
  const blob = await fetchLoanBlob(id, "download");
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `loan-${id}.pdf`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 60_000);
}
