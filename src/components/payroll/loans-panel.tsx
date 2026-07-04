"use client";

import { useEffect, useState } from "react";
import { HandCoins, Loader2 } from "lucide-react";
import { getLoans, LoanRecord } from "@/lib/api/loans";

/// <summary>Loans & advances list (installment schedules). Reused by the standalone /loans route and the
/// Payroll Run page's "loans" tab so HR can review them while preparing the run.</summary>
export function LoansPanel() {
  const [rows, setRows] = useState<LoanRecord[]>([]);
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState<string | null>(null);

  useEffect(() => { getLoans("all").then(setRows).catch(() => setRows([])).finally(() => setLoading(false)); }, []);
  const money = (n: number) => `${Math.round(n).toLocaleString("en-US")} ريال`;

  if (loading) return <div className="flex h-64 items-center justify-center"><Loader2 className="h-6 w-6 animate-spin text-muted-foreground" /></div>;
  if (rows.length === 0) return (
    <div className="flex flex-col items-center justify-center border border-dashed border-border p-12 text-center"><HandCoins className="mb-3 h-10 w-10 text-muted-foreground" /><p className="text-sm text-muted-foreground">لا توجد قروض أو سلف بعد — تُنشأ عند اعتماد طلبات القروض/السلف</p></div>
  );

  return (
    <div className="space-y-2">
      {rows.map((l) => (
        <div key={l.id} className="border border-border bg-card">
          <button onClick={() => setOpen(open === l.id ? null : l.id)} className="flex w-full flex-wrap items-center justify-between gap-3 px-4 py-3 text-right hover:bg-muted/30">
            <div className="flex items-center gap-3">
              <HandCoins className="h-5 w-5 text-primary" />
              <div>
                <div className="font-medium">{l.employeeName ?? "—"} <span className="text-xs text-muted-foreground">· {l.kind === "Advance" ? "سلفة" : "قرض"}{l.loanType ? ` · ${l.loanType}` : ""}</span></div>
                <div className="text-xs text-muted-foreground">{l.installmentMonths} قسط × {money(l.monthlyInstallment)}</div>
              </div>
            </div>
            <div className="flex items-center gap-3">
              <span className="font-bold tabular-nums">{money(l.principal)}</span>
              <span className="border border-border px-2 py-0.5 text-xs text-muted-foreground">{l.status}</span>
            </div>
          </button>
          {open === l.id && l.installments.length > 0 && (
            <div className="border-t border-border bg-secondary/30 px-4 py-3">
              <table className="w-full text-sm">
                <thead><tr className="text-right text-xs text-muted-foreground"><th className="py-1">الشهر</th><th>المبلغ</th><th>الحالة</th></tr></thead>
                <tbody>{l.installments.map((i, idx) => (
                  <tr key={idx} className="border-t border-border/40"><td className="py-1">{i.dueMonth?.slice(0, 7)}</td><td className="tabular-nums">{money(i.amount)}</td><td>{i.paid ? "مسدد" : "مستحق"}</td></tr>
                ))}</tbody>
              </table>
            </div>
          )}
        </div>
      ))}
    </div>
  );
}
