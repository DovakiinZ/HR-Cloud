"use client";

import { ExpensesPanel } from "@/components/payroll/expenses-panel";

export default function ExpensesPage() {
  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-bold">المصروفات</h1>
        <p className="mt-1 text-sm text-muted-foreground">سجلات المصروفات الناتجة عن مطالبات المصروف المعتمدة</p>
      </div>
      <ExpensesPanel />
    </div>
  );
}
