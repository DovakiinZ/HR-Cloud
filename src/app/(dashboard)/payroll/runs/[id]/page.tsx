"use client";

import { use, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import {
  ArrowRight, Loader2, Calculator, ShieldCheck, Send,
  CheckCircle2, PlayCircle, XCircle, RefreshCw, AlertTriangle,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api-client";
import { AccessGuard } from "@/components/access/access-guard";
import { usePermissions } from "@/lib/permissions";
import { StateBadge } from "@/components/payroll/state-badge";
import { CalcStatusBadge, type CalcStatusExtended } from "@/components/payroll/calc-status-badge";
import { RunKpiCards } from "@/components/payroll/run-kpi-cards";
import {
  getRunSummary, calculateRun, validateRun, submitRun, approveRun, executeRun, cancelRun,
  type PayrollRunSummary,
} from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

export default function RunDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  return <AccessGuard anyOf={["Payroll.View"]}><Inner id={id} /></AccessGuard>;
}

function Inner({ id }: { id: string }) {
  const { has } = usePermissions();
  const [run, setRun] = useState<PayrollRunSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  /** Client-only transient calc status — overrides run.calculationStatus while a recalc is in flight or has failed */
  const [calcStatus, setCalcStatus] = useState<CalcStatusExtended | null>(null);

  const load = useCallback(async () => {
    try {
      const summary = await getRunSummary(id);
      setRun(summary);
      setCalcStatus(null); // reset transient status after a successful fetch
    } catch (err) {
      notifyError(err, "تعذر تحميل المسيّر");
    }
  }, [id]);

  useEffect(() => {
    (async () => {
      setLoading(true);
      try { setRun(await getRunSummary(id)); } catch (err) { notifyError(err, "تعذر التحميل"); } finally { setLoading(false); }
    })();
  }, [id]);

  async function act(fn: () => Promise<PayrollRunSummary>, ok: string) {
    setBusy(true);
    try { setRun(await fn()); toast.success(ok); }
    catch (err) { notifyError(err, "تعذر تنفيذ العملية"); await load(); } finally { setBusy(false); }
  }

  async function handleRecalculate() {
    setBusy(true);
    setCalcStatus("Calculating");
    try {
      const updated = await calculateRun(id);
      setRun(updated);
      setCalcStatus(null);
      toast.success("تم الاحتساب بنجاح");
    } catch (err) {
      setCalcStatus("Failed");
      notifyError(err, "تعذر الاحتساب");
    } finally {
      setBusy(false);
    }
  }

  async function handleApproveAndRecalculate() {
    setBusy(true);
    setCalcStatus("Calculating");
    try {
      await approveRun(id);
      const updated = await calculateRun(id);
      setRun(updated);
      setCalcStatus(null);
      toast.success("تم الاعتماد والاحتساب بنجاح");
    } catch (err) {
      setCalcStatus("Failed");
      notifyError(err, "تعذر الاعتماد أو الاحتساب");
    } finally {
      setBusy(false);
    }
  }

  if (loading) return (
    <div className="py-20 text-center text-muted-foreground">
      <Loader2 className="h-5 w-5 animate-spin inline" /> جاري التحميل…
    </div>
  );
  if (!run) return <div className="py-20 text-center text-muted-foreground">المسيّر غير موجود</div>;

  const s = run.state;
  const effectiveCalcStatus: CalcStatusExtended = calcStatus ?? run.calculationStatus;

  const canRun = has("Payroll.Run");
  const canApprove = has("Payroll.Approve");
  const canExec = has("Payroll.Lock");
  const preExec = ["Draft", "Preview", "Validated", "PendingApproval", "Approved"].includes(s);

  const showRecalcBanner = effectiveCalcStatus === "RecalculationRequired" || effectiveCalcStatus === "Failed";

  return (
    <div className="space-y-6">
      {/* Breadcrumb */}
      <div className="flex items-center gap-2 text-sm">
        <Link href="/payroll" className="text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1">
          <ArrowRight className="h-4 w-4" /> الرواتب
        </Link>
        <span className="text-muted-foreground">/</span>
        <span dir="ltr">{run.runNumber}</span>
      </div>

      {/* Header */}
      <div className="flex items-start justify-between flex-wrap gap-4">
        <div className="space-y-2">
          <div className="flex items-center gap-3 flex-wrap">
            <h1 className="text-2xl font-bold" dir="ltr">{run.runNumber}</h1>
            <StateBadge state={s} />
            <CalcStatusBadge status={effectiveCalcStatus} />
          </div>
          <p className="text-sm text-muted-foreground" dir="ltr">
            {run.periodStart.slice(0, 10)} → {run.periodEnd.slice(0, 10)}
          </p>
          {run.calc && (
            <p className="text-xs text-muted-foreground">
              آخر احتساب: {new Date(run.calc.at).toLocaleString("ar-SA")} بواسطة {run.calc.byUserName}
            </p>
          )}
        </div>

        {/* Lifecycle action buttons */}
        <div className="flex flex-wrap items-center gap-2">
          {canRun && (s === "Draft" || s === "Preview") && (
            <Button onClick={() => act(() => calculateRun(id), "تم الاحتساب")} disabled={busy} variant="outline" className="gap-2">
              <Calculator className="h-4 w-4" /> احتساب
            </Button>
          )}
          {canRun && (s === "Preview" || s === "Validated") && (
            <Button onClick={() => act(() => validateRun(id), "تم التحقق")} disabled={busy} variant="outline" className="gap-2">
              <ShieldCheck className="h-4 w-4" /> تحقّق
            </Button>
          )}
          {canRun && s === "Validated" && (
            <Button onClick={() => act(() => submitRun(id), "تم الإرسال للاعتماد")} disabled={busy} variant="outline" className="gap-2">
              <Send className="h-4 w-4" /> إرسال للاعتماد
            </Button>
          )}
          {canApprove && s === "PendingApproval" && (
            <Button onClick={() => act(() => approveRun(id), "تم الاعتماد")} disabled={busy} className="gap-2 font-bold">
              <CheckCircle2 className="h-4 w-4" /> اعتماد
            </Button>
          )}
          {canExec && (s === "Approved" || s === "Failed") && (
            <Button onClick={() => act(() => executeRun(id), "تم تنفيذ المسيّر وترحيله للأستاذ")} disabled={busy} className="gap-2 font-bold">
              <PlayCircle className="h-4 w-4" /> تنفيذ وترحيل
            </Button>
          )}
          {canRun && preExec && (
            <Button
              onClick={() => act(() => cancelRun(id, "إلغاء يدوي"), "تم الإلغاء")}
              disabled={busy}
              variant="outline"
              className="gap-2 text-destructive border-destructive/30 hover:bg-destructive/10"
            >
              <XCircle className="h-4 w-4" /> إلغاء
            </Button>
          )}
        </div>
      </div>

      {/* Recalculation Required banner */}
      {showRecalcBanner && (
        <div className="border border-amber-500/30 bg-amber-500/5 p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="flex items-start gap-3">
            <AlertTriangle className="h-5 w-5 text-amber-600 mt-0.5 shrink-0" />
            <div>
              <p className="text-sm font-semibold text-amber-700">
                {effectiveCalcStatus === "Failed" ? "فشل الاحتساب الأخير" : "يتطلب المسيّر إعادة احتساب"}
              </p>
              <p className="text-xs text-amber-600 mt-0.5">
                {effectiveCalcStatus === "Failed"
                  ? "حدث خطأ أثناء الاحتساب. يرجى المحاولة مرة أخرى."
                  : "تغيّرت بيانات الرواتب أو الحضور منذ آخر احتساب. أعد الاحتساب لتحديث الأرقام."}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            {canRun && (
              <Button
                onClick={handleRecalculate}
                disabled={busy}
                variant="outline"
                className="gap-2 border-amber-500/40 text-amber-700 hover:bg-amber-500/10"
              >
                {calcStatus === "Calculating"
                  ? <Loader2 className="h-4 w-4 animate-spin" />
                  : <RefreshCw className="h-4 w-4" />}
                إعادة احتساب
              </Button>
            )}
            {canApprove && s === "PendingApproval" && (
              <Button
                onClick={handleApproveAndRecalculate}
                disabled={busy}
                className="gap-2 font-bold"
              >
                {calcStatus === "Calculating"
                  ? <Loader2 className="h-4 w-4 animate-spin" />
                  : <CheckCircle2 className="h-4 w-4" />}
                اعتماد وإعادة احتساب
              </Button>
            )}
          </div>
        </div>
      )}

      {/* KPI cards — 7 metrics from summary.kpis */}
      <RunKpiCards kpis={run.kpis} currency={run.currency} />

      {/*
       * ── Task 20 panels go here ──────────────────────────────────────────
       * The following tabbed panels will be added by Task 20:
       *   - Employees panel   (getRunEmployees — paged RunEmployeeRow table)
       *   - Excluded panel    (getRunExcluded — paged RunExcludedRow table)
       *   - Transactions panel(getRunTransactions — paged RunTransactionRow table)
       *   - Validation panel  (getRunValidation — paged RunValidationRow table)
       *   - Timeline panel    (run.timeline entries — already on summary)
       *   - Calculations panel(getRunCalculations — paged RunCalculationRow table)
       * ──────────────────────────────────────────────────────────────────
       */}
    </div>
  );
}
