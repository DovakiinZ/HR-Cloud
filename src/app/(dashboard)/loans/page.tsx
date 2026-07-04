"use client";

import { LoansPanel } from "@/components/payroll/loans-panel";

export default function LoansPage() {
  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-bold">القروض والسلف</h1>
        <p className="mt-1 text-sm text-muted-foreground">القروض والسلف الناتجة عن الطلبات المعتمدة وجداول الأقساط</p>
      </div>
      <LoansPanel />
    </div>
  );
}
