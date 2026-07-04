"use client";

import { useEffect, useState } from "react";
import { Loader2, Wallet } from "lucide-react";
import { Combobox } from "@/components/ui/combobox";
import { getEmployees } from "@/lib/api/employees";
import { getEmployeeLeaveBalance, type LeaveBalance } from "@/lib/api/leaves";

export default function LeaveBalancesPage() {
  const [employees, setEmployees] = useState<{ value: string; label: string; hint?: string }[]>([]);
  const [employeeId, setEmployeeId] = useState<string | null>(null);
  const [balances, setBalances] = useState<LeaveBalance[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    getEmployees({ pageSize: 500 })
      .then((list) => setEmployees(list.map((e) => ({ value: e.id, label: e.name, hint: e.employeeId }))))
      .catch(() => {});
  }, []);

  useEffect(() => {
    if (!employeeId) { setBalances([]); return; }
    setLoading(true);
    getEmployeeLeaveBalance(employeeId).then(setBalances).catch(() => setBalances([])).finally(() => setLoading(false));
  }, [employeeId]);

  const num = (n: number) => n.toLocaleString("ar-SA", { maximumFractionDigits: 2 });

  return (
    <div className="space-y-5">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold"><Wallet className="h-6 w-6 text-primary" /> أرصدة الإجازات</h1>
        <p className="mt-1 text-sm text-muted-foreground">أرصدة الإجازات لكل موظف حسب النوع (المستحق / المستخدم / المتبقي).</p>
      </div>

      <div className="max-w-sm">
        <label className="mb-1 block text-xs text-muted-foreground">الموظف</label>
        <Combobox value={employeeId} onChange={setEmployeeId} options={employees} placeholder="اختر موظفاً…" />
      </div>

      {!employeeId ? (
        <div className="border border-dashed border-border bg-card px-6 py-16 text-center text-sm text-muted-foreground">اختر موظفاً لعرض أرصدة إجازاته.</div>
      ) : loading ? (
        <div className="flex items-center justify-center py-20 text-muted-foreground"><Loader2 className="h-6 w-6 animate-spin" /></div>
      ) : balances.length === 0 ? (
        <div className="border border-dashed border-border bg-card px-6 py-16 text-center text-sm text-muted-foreground">لا توجد أرصدة إجازات لهذا الموظف.</div>
      ) : (
        <div className="overflow-x-auto border border-border bg-card">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border text-right text-xs text-muted-foreground">
                <th className="px-4 py-2 font-medium">نوع الإجازة</th>
                <th className="px-4 py-2 font-medium">المستحق</th>
                <th className="px-4 py-2 font-medium">المستخدم</th>
                <th className="px-4 py-2 font-medium">المتبقي</th>
              </tr>
            </thead>
            <tbody>
              {balances.map((b) => (
                <tr key={b.leaveTypeId} className="border-b border-border/40 hover:bg-muted/30">
                  <td className="px-4 py-2 font-medium">{b.leaveTypeName}{!b.affectsBalance && <span className="ms-2 text-[10px] text-muted-foreground">(لا يؤثر على الرصيد)</span>}</td>
                  <td className="px-4 py-2 tabular-nums">{num(b.entitled)}</td>
                  <td className="px-4 py-2 tabular-nums text-destructive">{num(b.used)}</td>
                  <td className="px-4 py-2 font-bold tabular-nums text-primary">{num(b.remaining)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
