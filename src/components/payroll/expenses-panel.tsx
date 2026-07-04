"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Receipt } from "lucide-react";
import { ExpenseRecord, getExpenses } from "@/lib/api/expenses";
import { AddExpenseDialog } from "./add-expense-dialog";

/// <summary>Expenses list (manual entries + approved expense claims). Reused by the standalone /expenses
/// route and the Payroll Run page's "expenses" tab.</summary>
export function ExpensesPanel() {
  const [rows, setRows] = useState<ExpenseRecord[]>([]);
  const [loading, setLoading] = useState(true);

  const load = useCallback(() => {
    setLoading(true);
    getExpenses("all").then(setRows).catch(() => setRows([])).finally(() => setLoading(false));
  }, []);
  useEffect(() => { load(); }, [load]);

  const money = (n: number, c: string) => `${Math.round(n).toLocaleString("en-US")} ${c}`;
  const total = rows.reduce((s, r) => s + r.amount, 0);

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="text-sm text-muted-foreground">الإجمالي: <span className="font-bold text-foreground">{money(total, rows[0]?.currency ?? "SAR")}</span></div>
        <AddExpenseDialog onSuccess={load} />
      </div>

      {loading ? (
        <Center><Loader2 className="h-6 w-6 animate-spin text-muted-foreground" /></Center>
      ) : rows.length === 0 ? (
        <Empty icon={Receipt} text="لا توجد مصروفات بعد — أضف مصروفاً يدوياً أو اعتمد مطالبة مصروف" />
      ) : (
      <div className="overflow-x-auto border border-border bg-card">
        <table className="w-full text-sm">
          <thead><tr className="border-b border-border text-right text-xs text-muted-foreground">
            <Th>الموظف</Th><Th>الفئة</Th><Th>المبلغ</Th><Th>الوصف</Th><Th>الرواتب</Th><Th>الحالة</Th><Th>التاريخ</Th>
          </tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} className="border-b border-border/40 hover:bg-muted/30">
                <Td>{r.employeeName ?? "—"}</Td>
                <Td>{r.category ?? "—"}</Td>
                <Td className="font-medium tabular-nums">{money(r.amount, r.currency)}</Td>
                <Td className="max-w-xs truncate text-muted-foreground">{r.description ?? "—"}</Td>
                <Td>
                  {r.includeInPayroll
                    ? <span className="border border-primary/30 bg-primary/10 px-2 py-0.5 text-xs text-primary">{r.payrollMonth?.slice(0, 7) ?? "نعم"}</span>
                    : <span className="text-xs text-muted-foreground">—</span>}
                </Td>
                <Td><span className="border border-green-500/30 bg-green-500/10 px-2 py-0.5 text-xs text-green-400">{r.status}</span></Td>
                <Td className="text-muted-foreground">{r.decidedAt?.slice(0, 10)}</Td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      )}
    </div>
  );
}

function Center({ children }: { children: React.ReactNode }) { return <div className="flex h-64 items-center justify-center">{children}</div>; }
function Empty({ icon: Icon, text }: { icon: React.ElementType; text: string }) {
  return <div className="flex flex-col items-center justify-center border border-dashed border-border p-12 text-center"><Icon className="mb-3 h-10 w-10 text-muted-foreground" /><p className="text-sm text-muted-foreground">{text}</p></div>;
}
function Th({ children }: { children: React.ReactNode }) { return <th className="px-4 py-2 font-medium">{children}</th>; }
function Td({ children, className }: { children: React.ReactNode; className?: string }) { return <td className={`px-4 py-2 ${className ?? ""}`}>{children}</td>; }
