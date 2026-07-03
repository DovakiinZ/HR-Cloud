"use client";

import { useEffect, useMemo, useState } from "react";
import { Plus, Loader2, AlertTriangle } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Combobox } from "@/components/ui/combobox";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter, DialogDescription,
} from "@/components/ui/dialog";
import { ApiError } from "@/lib/api-client";
import { getMasterDataItems, type MasterDataItem } from "@/lib/api/master-data";
import { getRunEmployees, createRunTransaction, type RunEmployeeRow } from "@/lib/api/payroll";
import type { TransactionKind } from "@/lib/api/payroll-transactions";

// Attendance sub-kinds that map to DeductionType items
const ATTENDANCE_CODES = ["ABSENCE", "LATE", "SHORTAGE"];

type Preset = "Addition" | "Deduction" | "AttendanceDeduction" | "OvertimeAddition";

interface PresetDef {
  id: Preset;
  label: string;
  kind: TransactionKind;
  /** If set, filter master-data types to items with this code (case-insensitive). */
  codeFilter?: string[];
  objectType: string;
}

const PRESETS: PresetDef[] = [
  {
    id: "Addition",
    label: "إضافة",
    kind: 1,
    objectType: "AdditionType",
  },
  {
    id: "Deduction",
    label: "استقطاع",
    kind: 2,
    objectType: "DeductionType",
  },
  {
    id: "AttendanceDeduction",
    label: "استقطاع حضور",
    kind: 2,
    codeFilter: ATTENDANCE_CODES,
    objectType: "DeductionType",
  },
  {
    id: "OvertimeAddition",
    label: "إضافة عمل إضافي",
    kind: 1,
    codeFilter: ["OVERTIME"],
    objectType: "AdditionType",
  },
];

interface QuickAddTransactionDialogProps {
  runId: string;
  onSuccess: () => void;
}

export function QuickAddTransactionDialog({ runId, onSuccess }: QuickAddTransactionDialogProps) {
  const [open, setOpen] = useState(false);
  const [preset, setPreset] = useState<Preset | null>(null);

  // Master data
  const [additionTypes, setAdditionTypes] = useState<MasterDataItem[]>([]);
  const [deductionTypes, setDeductionTypes] = useState<MasterDataItem[]>([]);
  const [typesLoaded, setTypesLoaded] = useState(false);

  // Run employees
  const [employees, setEmployees] = useState<RunEmployeeRow[]>([]);
  const [empsLoaded, setEmpsLoaded] = useState(false);

  // Form
  const [employeeId, setEmployeeId] = useState<string | null>(null);
  const [typeId, setTypeId] = useState<string | null>(null);
  const [amount, setAmount] = useState("");
  const [notes, setNotes] = useState("");

  // Submit state
  const [saving, setSaving] = useState(false);
  /** Inline 422 PAYROLL_PERIOD_CLOSED error message */
  const [periodClosedMsg, setPeriodClosedMsg] = useState<string | null>(null);

  // Load types + employees when dialog opens
  useEffect(() => {
    if (!open) return;
    if (!typesLoaded) {
      Promise.all([
        getMasterDataItems("AdditionType"),
        getMasterDataItems("DeductionType"),
      ]).then(([adds, deds]) => {
        setAdditionTypes(adds);
        setDeductionTypes(deds);
        setTypesLoaded(true);
      }).catch(() => toast.error("تعذر تحميل الأنواع"));
    }
    if (!empsLoaded) {
      // Load all run employees (first page, large enough for most runs)
      getRunEmployees(runId, { pageSize: 500 }).then((res) => {
        setEmployees(res.items);
        setEmpsLoaded(true);
      }).catch(() => toast.error("تعذر تحميل الموظفين"));
    }
  }, [open, runId, typesLoaded, empsLoaded]);

  // Determine if the OVERTIME preset should be visible
  const overtimeExists = useMemo(() => {
    return additionTypes.some((t) => t.code?.toUpperCase() === "OVERTIME" && t.isActive);
  }, [additionTypes]);

  const visiblePresets = useMemo(() => {
    return PRESETS.filter((p) => {
      if (p.id === "OvertimeAddition" && !overtimeExists && typesLoaded) return false;
      return true;
    });
  }, [overtimeExists, typesLoaded]);

  // Derive available type options for the selected preset
  const typeOptions = useMemo(() => {
    if (!preset) return [];
    const def = PRESETS.find((p) => p.id === preset);
    if (!def) return [];
    const pool = def.kind === 1 ? additionTypes : deductionTypes;
    const items = def.codeFilter
      ? pool.filter((t) => def.codeFilter!.some((c) => t.code?.toUpperCase() === c.toUpperCase()) && t.isActive)
      : pool.filter((t) => t.isActive);
    return items.map((t) => ({ value: t.id, label: t.nameAr || t.nameEn }));
  }, [preset, additionTypes, deductionTypes]);

  const empOptions = useMemo(() => {
    return employees.map((e) => ({ value: e.employeeId, label: `${e.employeeName} (${e.employeeNumber})` }));
  }, [employees]);

  function resetForm() {
    setPreset(null);
    setEmployeeId(null);
    setTypeId(null);
    setAmount("");
    setNotes("");
    setPeriodClosedMsg(null);
  }

  function handleOpen() {
    resetForm();
    setOpen(true);
  }

  function handleClose() {
    if (!saving) {
      setOpen(false);
      resetForm();
    }
  }

  function selectPreset(p: Preset) {
    setPreset(p);
    setTypeId(null); // reset type when preset changes
    setPeriodClosedMsg(null);
  }

  async function handleSubmit() {
    if (!preset) { toast.error("اختر نوع العملية"); return; }
    if (!employeeId) { toast.error("اختر موظفاً"); return; }
    if (!typeId) { toast.error("اختر النوع"); return; }
    const amt = Number(amount);
    if (Number.isNaN(amt) || amt <= 0) { toast.error("أدخل مبلغاً صحيحاً أكبر من صفر"); return; }

    const def = PRESETS.find((p) => p.id === preset)!;
    setPeriodClosedMsg(null);
    setSaving(true);
    try {
      await createRunTransaction(runId, {
        employeeId,
        kind: def.kind,
        typeId,
        amount: amt,
        notes: notes.trim() || null,
      });
      toast.success("تمت إضافة الحركة بنجاح");
      setOpen(false);
      resetForm();
      onSuccess();
    } catch (err) {
      if (err instanceof ApiError && err.status === 422) {
        // Try to extract blockingRunNumber from errors array or message
        const raw = err.errors?.[0] ?? err.message ?? "";
        // The backend may return "PAYROLL_PERIOD_CLOSED:RunNumber=PAY-2025-01-001"
        const match = raw.match(/RunNumber=([^\s,;]+)/i)
          ?? raw.match(/blocking[_\s]?run[_\s]?number[:\s]+([^\s,;]+)/i);
        const blockingRun = match?.[1] ?? null;
        if (raw.includes("PAYROLL_PERIOD_CLOSED")) {
          setPeriodClosedMsg(
            blockingRun
              ? `الفترة مُقفلة — المسيّر الحاكم: ${blockingRun}`
              : "الفترة مُقفلة — لا يمكن إضافة حركات"
          );
          return;
        }
      }
      if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
        toast.error(err instanceof ApiError ? err.message : "تعذر إضافة الحركة");
      }
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <Button onClick={handleOpen} size="sm" className="gap-2 font-bold">
        <Plus className="h-4 w-4" />
        إضافة حركة
      </Button>

      <Dialog open={open} onOpenChange={(o) => { if (!o) handleClose(); }}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle>إضافة حركة للمسيّر</DialogTitle>
            <DialogDescription>اختر نوع العملية ثم عبئ التفاصيل</DialogDescription>
          </DialogHeader>

          <div className="space-y-4 py-1">
            {/* Preset selector */}
            <div className="space-y-2">
              <Label className="text-xs font-bold uppercase tracking-wider">نوع العملية</Label>
              <div className="flex flex-wrap gap-2">
                {!typesLoaded ? (
                  <div className="text-xs text-muted-foreground flex items-center gap-1">
                    <Loader2 className="h-3 w-3 animate-spin" /> جاري التحميل…
                  </div>
                ) : visiblePresets.map((p) => (
                  <button
                    key={p.id}
                    type="button"
                    onClick={() => selectPreset(p.id)}
                    className={`px-3 py-1.5 text-xs font-medium border transition-colors ${
                      preset === p.id
                        ? "border-primary bg-primary/10 text-primary"
                        : "border-border hover:bg-muted text-foreground"
                    }`}
                  >
                    {p.label}
                  </button>
                ))}
              </div>
            </div>

            {preset && (
              <>
                {/* Employee */}
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الموظف</Label>
                  {!empsLoaded ? (
                    <div className="text-xs text-muted-foreground flex items-center gap-1">
                      <Loader2 className="h-3 w-3 animate-spin" /> جاري التحميل…
                    </div>
                  ) : (
                    <Combobox
                      value={employeeId}
                      onChange={setEmployeeId}
                      options={empOptions}
                      placeholder="اختر موظفاً…"
                    />
                  )}
                </div>

                {/* Type */}
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">النوع</Label>
                  {typeOptions.length === 0 ? (
                    <p className="text-xs text-muted-foreground">لا توجد أنواع متاحة لهذه الفئة</p>
                  ) : (
                    <Combobox
                      value={typeId}
                      onChange={setTypeId}
                      options={typeOptions}
                      placeholder="اختر النوع…"
                    />
                  )}
                </div>

                {/* Amount */}
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">المبلغ</Label>
                  <Input
                    type="number"
                    step="any"
                    min={0}
                    value={amount}
                    onChange={(e) => setAmount(e.target.value)}
                    className="bg-secondary border-border"
                    dir="ltr"
                    placeholder="0.00"
                  />
                </div>

                {/* Notes */}
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">ملاحظات (اختياري)</Label>
                  <Input
                    value={notes}
                    onChange={(e) => setNotes(e.target.value)}
                    className="bg-secondary border-border"
                    placeholder="أدخل ملاحظات…"
                  />
                </div>

                {/* 422 PAYROLL_PERIOD_CLOSED inline error */}
                {periodClosedMsg && (
                  <div className="flex items-start gap-2 border border-destructive/30 bg-destructive/5 px-3 py-2 text-sm text-destructive">
                    <AlertTriangle className="h-4 w-4 mt-0.5 shrink-0" />
                    <span>{periodClosedMsg}</span>
                  </div>
                )}
              </>
            )}
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={handleClose} disabled={saving}>
              إلغاء
            </Button>
            <Button onClick={handleSubmit} disabled={saving || !preset} className="font-bold gap-2">
              {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
              {saving ? "جاري الحفظ…" : "إضافة"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
