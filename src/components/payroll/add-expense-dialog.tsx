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
import { createExpense } from "@/lib/api/expenses";
import { getMasterDataItems, type MasterDataItem } from "@/lib/api/master-data";
import { getEmployees } from "@/lib/api/employees";
import type { Employee } from "@/types";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

/// <summary>Manually add an expense and assign it to any employee. Categories come from the
/// ExpenseCategory master-data (managed under Settings → Payroll → Expense types).</summary>
export function AddExpenseDialog({ onSuccess }: { onSuccess: () => void }) {
  const [open, setOpen] = useState(false);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [categories, setCategories] = useState<MasterDataItem[]>([]);
  const [loaded, setLoaded] = useState(false);

  const [employeeId, setEmployeeId] = useState<string | null>(null);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [amount, setAmount] = useState("");
  const [description, setDescription] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!open || loaded) return;
    Promise.all([
      getEmployees({ pageSize: 500 }),
      getMasterDataItems("ExpenseCategory"),
    ]).then(([emps, cats]) => {
      setEmployees(emps);
      setCategories(cats.filter((c) => c.isActive));
      setLoaded(true);
    }).catch((err) => notifyError(err, "تعذر تحميل البيانات"));
  }, [open, loaded]);

  const empOptions = useMemo(
    () => employees.map((e) => ({ value: e.id, label: `${e.name} (${e.employeeId})` })),
    [employees],
  );
  const catOptions = useMemo(
    () => categories.map((c) => ({ value: c.id, label: c.nameAr || c.nameEn })),
    [categories],
  );

  function reset() {
    setEmployeeId(null);
    setCategoryId(null);
    setAmount("");
    setDescription("");
  }

  function handleClose() {
    if (saving) return;
    setOpen(false);
    reset();
  }

  async function handleSubmit() {
    if (!employeeId) { toast.error("اختر موظفاً"); return; }
    const amt = Number(amount);
    if (Number.isNaN(amt) || amt <= 0) { toast.error("أدخل مبلغاً صحيحاً أكبر من صفر"); return; }
    setSaving(true);
    try {
      await createExpense({
        employeeId,
        expenseCategoryId: categoryId,
        amount: amt,
        description: description.trim() || null,
      });
      toast.success("تمت إضافة المصروف بنجاح");
      setOpen(false);
      reset();
      onSuccess();
    } catch (err) {
      notifyError(err, "تعذر إضافة المصروف");
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <Button onClick={() => { reset(); setOpen(true); }} size="sm" className="gap-2 font-bold">
        <Plus className="h-4 w-4" />
        إضافة مصروف
      </Button>

      <Dialog open={open} onOpenChange={(o) => { if (!o) handleClose(); }}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>إضافة مصروف</DialogTitle>
            <DialogDescription>أسند مصروفاً لأي موظف</DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-1">
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
              <Label className="text-xs font-bold uppercase tracking-wider">الفئة (اختياري)</Label>
              {catOptions.length === 0 ? (
                <p className="text-xs text-muted-foreground">لا توجد فئات — أضفها من إعدادات الرواتب</p>
              ) : (
                <Combobox value={categoryId} onChange={setCategoryId} options={catOptions} placeholder="اختر الفئة…" />
              )}
            </div>

            <div className="space-y-2">
              <Label className="text-xs font-bold uppercase tracking-wider">المبلغ</Label>
              <Input
                type="number" step="any" min={0} value={amount} onChange={(e) => setAmount(e.target.value)}
                className="bg-secondary border-border" dir="ltr" placeholder="0.00"
              />
            </div>

            <div className="space-y-2">
              <Label className="text-xs font-bold uppercase tracking-wider">الوصف (اختياري)</Label>
              <Input
                value={description} onChange={(e) => setDescription(e.target.value)}
                className="bg-secondary border-border" placeholder="أدخل وصفاً…"
              />
            </div>
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
