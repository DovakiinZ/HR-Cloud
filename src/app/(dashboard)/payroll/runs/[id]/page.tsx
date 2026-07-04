"use client";

import { use, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import {
  ArrowRight, Loader2, Calculator, ShieldCheck, Send,
  CheckCircle2, PlayCircle, XCircle, RefreshCw, AlertTriangle, Ban, FilePlus2,
} from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api-client";
import { AccessGuard } from "@/components/access/access-guard";
import { usePermissions } from "@/lib/permissions";
import { StateBadge } from "@/components/payroll/state-badge";
import { CalcStatusBadge, type CalcStatusExtended } from "@/components/payroll/calc-status-badge";
import { RunKpiCards } from "@/components/payroll/run-kpi-cards";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { RunEmployeesTable } from "@/components/payroll/run-employees-table";
import { RunExcludedPanel } from "@/components/payroll/run-excluded-panel";
import { RunTransactionsPanel } from "@/components/payroll/run-transactions-panel";
import { RunValidationPanel } from "@/components/payroll/run-validation-panel";
import { RunTimeline } from "@/components/payroll/run-timeline";
import { RunPayslipsPanel } from "@/components/payroll/run-payslips-panel";
import { RunExportsPanel } from "@/components/payroll/run-exports-panel";
import { RunAuditPanel } from "@/components/payroll/run-audit-panel";
import {
  getRunSummary, calculateRun, validateRun, submitRun, approveRun, executeRun, cancelRun,
  voidRun, amendRun, reissueRun,
  type PayrollRunSummary,
} from "@/lib/api/payroll";

/** Run states where no mutations are permitted (quick-add hidden). */
const IMMUTABLE_STATES = new Set(["Approved", "Executing", "Completed", "Locked", "Archived"]);

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
  const router = useRouter();
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

  async function handleVoid() {
    const reason = window.prompt("سبب إلغاء المسيّر (سيتم عكس جميع القيود المحاسبية):");
    if (!reason?.trim()) return;
    setBusy(true);
    try { await voidRun(id, reason.trim()); toast.success("تم إلغاء المسيّر وعكس قيوده"); await load(); }
    catch (err) { notifyError(err, "تعذر إلغاء المسيّر"); }
    finally { setBusy(false); }
  }

  async function handleAmend() {
    const reason = window.prompt("سبب التعديل (سيتم إلغاء هذا المسيّر وإنشاء مسيّر جديد يحل محله):");
    if (!reason?.trim()) return;
    setBusy(true);
    try {
      const r = await amendRun(id, reason.trim());
      toast.success(`تم إنشاء مسيّر التعديل ${r.newRunNumber}`);
      router.push(`/payroll/runs/${r.newRunId}`);
    } catch (err) { notifyError(err, "تعذر تعديل المسيّر"); setBusy(false); }
  }

  async function handleReissue() {
    setBusy(true);
    try { const n = await reissueRun(id); toast.success(`تم إعادة إصدار ${n.toLocaleString("ar-SA")} قسيمة راتب`); }
    catch (err) { notifyError(err, "تعذر إعادة إصدار القسائم"); }
    finally { setBusy(false); }
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

          {/* SP6 — posted-run corrections (Completed/Locked) */}
          {(s === "Completed" || s === "Locked") && has("Payroll.Run.Reissue") && (
            <Button onClick={handleReissue} disabled={busy} variant="outline" className="gap-2">
              <RefreshCw className="h-4 w-4" /> إعادة إصدار القسائم
            </Button>
          )}
          {(s === "Completed" || s === "Locked") && has("Payroll.Run.Amend") && (
            <Button onClick={handleAmend} disabled={busy} variant="outline" className="gap-2">
              <FilePlus2 className="h-4 w-4" /> تعديل (مسيّر جديد)
            </Button>
          )}
          {(s === "Completed" || s === "Locked") && has("Payroll.Run.Void") && (
            <Button onClick={handleVoid} disabled={busy} variant="outline" className="gap-2 text-destructive border-destructive/30 hover:bg-destructive/10">
              <Ban className="h-4 w-4" /> إلغاء وعكس القيود
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

      {/* ── Tabbed panels (Task 20) ─────────────────────────────────── */}
      <Tabs defaultValue="employees">
        <TabsList variant="line" className="w-full justify-start gap-1 border-b border-border rounded-none pb-0 h-auto">
          <TabsTrigger value="employees" className="pb-2">الموظفون</TabsTrigger>
          <TabsTrigger value="transactions" className="pb-2">الحركات</TabsTrigger>
          <TabsTrigger value="validation" className="pb-2">التحقق</TabsTrigger>
          <TabsTrigger value="excluded" className="pb-2">المستثنون</TabsTrigger>
          {has("Payroll.Payslip.View") && <TabsTrigger value="payslips" className="pb-2">قسائم الرواتب</TabsTrigger>}
          {has("Payroll.Export") && <TabsTrigger value="exports" className="pb-2">التصدير</TabsTrigger>}
          {has("Payroll.Audit.View") && <TabsTrigger value="audit" className="pb-2">سجل التدقيق</TabsTrigger>}
          <TabsTrigger value="timeline" className="pb-2">السجل الزمني</TabsTrigger>
        </TabsList>

        <TabsContent value="employees" className="pt-4">
          <RunEmployeesTable runId={id} currency={run.currency} />
        </TabsContent>

        <TabsContent value="transactions" className="pt-4">
          <RunTransactionsPanel
            runId={id}
            currency={run.currency}
            immutable={IMMUTABLE_STATES.has(s)}
          />
        </TabsContent>

        <TabsContent value="validation" className="pt-4">
          <RunValidationPanel runId={id} />
        </TabsContent>

        <TabsContent value="excluded" className="pt-4">
          <RunExcludedPanel runId={id} />
        </TabsContent>

        {has("Payroll.Payslip.View") && (
          <TabsContent value="payslips" className="pt-4">
            <RunPayslipsPanel runId={id} currency={run.currency} />
          </TabsContent>
        )}

        {has("Payroll.Export") && (
          <TabsContent value="exports" className="pt-4">
            <RunExportsPanel runId={id} />
          </TabsContent>
        )}

        {has("Payroll.Audit.View") && (
          <TabsContent value="audit" className="pt-4">
            <RunAuditPanel runId={id} />
          </TabsContent>
        )}


        <TabsContent value="timeline" className="pt-4">
          <RunTimeline runId={id} currency={run.currency} timeline={run.timeline} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
