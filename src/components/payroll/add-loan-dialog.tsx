"use client";

import { useEffect, useMemo, useState } from "react";
import { Plus, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Combobox } from "@/components/ui/combobox";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription,
} from "@/components/ui/dialog";
import { ApiError } from "@/lib/api-client";
import { createLoan } from "@/lib/api/loans";
import { getMasterDataItems, type MasterDataItem } from "@/lib/api/master-data";
import { getEmployees } from "@/lib/api/employees";
import type { Employee } from "@/types";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

/// <summary>Manually add a loan or salary advance and assign it to any employee. Generates the
/// monthly installment schedule on the backend. Types come from the LoanType master-data.</summary>
export function AddLoanDialog({ onSuccess }: { onSuccess: () => void }) {
  const [open, setOpen] = useState(false);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loanTypes, setLoanTypes] = useState<MasterDataItem[]>([]);
  const [loaded, setLoaded] = useState(false);

  const [employeeId, setEmployeeId] = useState<string | null>(null);
  const [kind, setKind] = useState<"Loan" | "Advance">("Loan");
  const [typeId, setTypeId] = useState<string | null>(null);
  const [principal, setPrincipal] = useState("");
  const [months, setMonths] = useState("1");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open || loaded) return;
    Promise.all([
      getEmployees({ pageSize: 500 }),
      getMasterDataItems("LoanType"),
    ]).then(([emps, types]) => {
      setEmployees(emps);
      setLoanTypes(types.filter((t) => t.isActive));
      setLoaded(true);
    }).catch((err) => notifyError(err, "تعذر تحميل البيانات"));
  }, [open, loaded]);

  const empOptions = useMemo(
    () => employees.map((e) => ({ value: e.id, label: `${e.name} (${e.employeeId})` })),
    [employees],
  );
  const typeOptions = useMemo(
    () => loanTypes.map((t) => ({ value: t.id, label: t.nameAr || t.nameEn })),
    [loanTypes],
  );

  const monthlyPreview = useMemo(() => {
    const p = Number(principal);
    const m = Math.max(1, Number(months) || 1);
    if (Number.isNaN(p) || p <= 0) return null;
    return Math.round((p / m) * 100) / 100;
  }, [principal, months]);

  function reset() {
    setEmployeeId(null);
    setKind("Loan");
    setTypeId(null);
    setPrincipal("");
    setMonths("1");
  }

  function handleClose() {
    if (saving) return;
    setOpen(false);
    reset();
  }

  async function handleSubmit() {
    if (!employeeId) { toast.error("اختر موظفاً"); return; }
    const p = Number(principal);
    if (Number.isNaN(p) || p <= 0) { toast.error("أدخل مبلغاً صحيحاً أكبر من صفر"); return; }
    const m = Math.max(1, Number(months) || 1);
    setSaving(true);
    try {
      await createLoan({ employeeId, loanTypeId: typeId, kind, principal: p, installmentMonths: m });
      toast.success(kind === "Advance" ? "تمت إضافة السلفة بنجاح" : "تمت إضافة القرض بنجاح");
      setOpen(false);
      reset();
      onSuccess();
    } catch (err) {
      notifyError(err, "تعذر إضافة القرض");
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <Button onClick={() => { reset(); setOpen(true); }} size="sm" className="gap-2 font-bold">
        <Plus className="h-4 w-4" />
        إضافة قرض / سلفة
      </Button>

      <Dialog open={open} onOpenChange={(o) => { if (!o) handleClose(); }}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>إضافة قرض / سلفة</DialogTitle>
            <DialogDescription>أسند قرضاً أو سلفة لأي موظف مع جدول الأقساط</DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-1">
            <div className="space-y-2">
              <Label className="text-xs font-bold uppercase tracking-wider">النوع</Label>
              <div className="flex gap-2">
                {(["Loan", "Advance"] as const).map((k) => (
                  <button
                    key={k}
                    type="button"
                    onClick={() => setKind(k)}
                    className={`px-3 py-1.5 text-xs font-medium border transition-colors ${
                      kind === k ? "border-primary bg-primary/10 text-primary" : "border-border hover:bg-muted text-foreground"
                    }`}
                  >
                    {k === "Advance" ? "سلفة" : "قرض"}
                  </button>
                ))}
              </div>
            </div>

            <div className="space-y-2">
              <Label className="text-xs font-bold uppercase tracking-wider">الموظف</Label>
              {!loaded ? (
                <div className="text-xs text-muted-foreground flex items-center gap-1">
                  <Loader2 className="h-3 w-3 animate-spin" /> جاري التحميل…
                </div>
              ) : (
                <Combobox value={employeeId} onChange={setEmployeeId} options={empOptions} placeholder="اختر موظفاً…" />
              )}
            </div>

            <div className="space-y-2">
              <Label className="text-xs font-bold uppercase tracking-wider">التصنيف (اختياري)</Label>
              {typeOptions.length === 0 ? (
                <p className="text-xs text-muted-foreground">لا توجد تصنيفات — أضفها من إعدادات الرواتب</p>
              ) : (
                <Combobox value={typeId} onChange={setTypeId} options={typeOptions} placeholder="اختر التصنيف…" />
              )}
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-2">
                <Label className="text-xs font-bold uppercase tracking-wider">المبلغ الإجمالي</Label>
                <Input
                  type="number" step="any" min={0} value={principal} onChange={(e) => setPrincipal(e.target.value)}
                  className="bg-secondary border-border" dir="ltr" placeholder="0.00"
                />
              </div>
              <div className="space-y-2">
                <Label className="text-xs font-bold uppercase tracking-wider">عدد الأقساط (شهور)</Label>
                <Input
                  type="number" step="1" min={1} value={months} onChange={(e) => setMonths(e.target.value)}
                  className="bg-secondary border-border" dir="ltr" placeholder="1"
                />
              </div>
            </div>

            {monthlyPreview !== null && (
              <p className="text-xs text-muted-foreground">
                القسط الشهري ≈ <span className="font-bold text-foreground tabular-nums">{monthlyPreview.toLocaleString("en-US")}</span> ريال
              </p>
            )}
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={handleClose} disabled={saving}>إلغاء</Button>
            <Button onClick={handleSubmit} disabled={saving} className="font-bold gap-2">
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
              {saving ? "جاري الحفظ…" : "إضافة"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
