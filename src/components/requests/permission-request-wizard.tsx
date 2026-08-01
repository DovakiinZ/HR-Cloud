"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Send, AlertTriangle, Ban, Info } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { RequestTypeDetail, RequestValue, submitRequest } from "@/lib/api/request-center";
import {
  getEligiblePermissionTypes, validatePermission,
  type EligiblePermissionType, type ValidatePermissionResult,
} from "@/lib/api/attendance-permissions";

function fmtMinutes(m: number): string {
  const h = Math.floor(m / 60), min = m % 60;
  if (h && min) return `${h} س ${min} د`;
  if (h) return `${h} س`;
  return `${min} د`;
}
function remainingText(remaining: number | null): string {
  return remaining == null ? "بلا حد" : fmtMinutes(remaining);
}

export function PermissionRequestWizard({ type, onCancel, onSubmitted }: {
  type: RequestTypeDetail; onCancel: () => void; onSubmitted: () => void;
}) {
  const [eligible, setEligible] = useState<EligiblePermissionType[]>([]);
  const [loading, setLoading] = useState(true);
  const [typeId, setTypeId] = useState("");
  const [date, setDate] = useState("");
  const [fromTime, setFromTime] = useState("");
  const [toTime, setToTime] = useState("");
  const [reason, setReason] = useState("");
  const [overrideReason, setOverrideReason] = useState("");
  const [result, setResult] = useState<ValidatePermissionResult | null>(null);
  const [validating, setValidating] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    getEligiblePermissionTypes()
      .then((t) => { setEligible(t); if (t.length === 1) setTypeId(t[0].id); })
      .catch(() => toast.error("تعذر تحميل أنواع الاستئذان المتاحة"))
      .finally(() => setLoading(false));
  }, []);

  const selected = eligible.find((e) => e.id === typeId) ?? null;
  const ready = !!(typeId && date && fromTime && toTime && toTime > fromTime);

  const runValidate = useCallback(async () => {
    if (!ready) { setResult(null); return; }
    setValidating(true);
    try { setResult(await validatePermission({ permissionTypeId: typeId, date, fromTime, toTime })); }
    catch { setResult(null); }
    finally { setValidating(false); }
  }, [ready, typeId, date, fromTime, toTime]);

  useEffect(() => { const t = setTimeout(runValidate, 350); return () => clearTimeout(t); }, [runValidate]);

  const outcome = result?.decision.outcome;
  const blocked = outcome === "Block";
  const overrideRequired = result?.overrideRequired ?? false;
  const durationMinutes = result?.durationMinutes ?? (ready ? toMinutes(toTime) - toMinutes(fromTime) : 0);

  const canSubmit = ready && !blocked && !submitting && !validating && (!overrideRequired || overrideReason.trim().length > 0);

  const submit = async () => {
    if (!ready) { toast.error("أكمل بيانات الاستئذان"); return; }
    if (blocked) { toast.error(result?.decision.reasonAr ?? "تجاوزت الحد المسموح"); return; }
    if (overrideRequired && !overrideReason.trim()) { toast.error("سبب التجاوز مطلوب"); return; }
    setSubmitting(true);
    try {
      const values: RequestValue[] = [
        { fieldCode: "permissionType", value: typeId },
        { fieldCode: "date", value: date },
        { fieldCode: "fromTime", value: fromTime },
        { fieldCode: "toTime", value: toTime },
        { fieldCode: "reason", value: reason.trim() || null },
        { fieldCode: "overrideReason", value: overrideReason.trim() || null },
      ];
      await submitRequest(type.id, values);
      toast.success("تم إرسال طلب الاستئذان");
      onSubmitted();
    } catch (e) { toast.error(e instanceof ApiError ? e.message : "تعذر الإرسال"); }
    finally { setSubmitting(false); }
  };

  if (loading) return <div className="flex h-40 items-center justify-center text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" /></div>;
  if (eligible.length === 0) return <p className="p-2 text-sm text-muted-foreground">لا توجد أنواع استئذان متاحة لك حالياً. تواصل مع مسؤول الموارد البشرية.</p>;

  const inp = "h-10 w-full border border-border bg-secondary px-3 text-sm";
  const usage = result?.usage ?? selected?.usage ?? null;

  return (
    <div className="space-y-4">
      <div className="space-y-1">
        <Label>نوع الاستئذان</Label>
        <select value={typeId} onChange={(e) => setTypeId(e.target.value)} className={inp}>
          <option value="">— اختر —</option>
          {eligible.map((e) => <option key={e.id} value={e.id}>{e.nameAr}{e.paid ? "" : " (غير مدفوع)"}</option>)}
        </select>
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div className="space-y-1"><Label>التاريخ</Label><input type="date" value={date} onChange={(e) => setDate(e.target.value)} className={inp} /></div>
        <div className="space-y-1"><Label>من الساعة</Label><input type="time" value={fromTime} onChange={(e) => setFromTime(e.target.value)} className={inp} dir="ltr" /></div>
        <div className="space-y-1"><Label>إلى الساعة</Label><input type="time" value={toTime} onChange={(e) => setToTime(e.target.value)} className={inp} dir="ltr" /></div>
      </div>

      {ready && toTime <= fromTime && <p className="text-xs text-destructive">وقت النهاية يجب أن يكون بعد وقت البداية</p>}

      {/* Live feedback: duration + used/remaining + decision */}
      {ready && (
        <div className="space-y-2 border border-border bg-secondary/40 p-3 text-sm">
          <div className="flex items-center justify-between">
            <span className="text-muted-foreground">مدة الاستئذان</span>
            <span className="font-bold">{fmtMinutes(durationMinutes)}{validating && <Loader2 className="ms-2 inline h-3 w-3 animate-spin" />}</span>
          </div>
          {usage && (
            <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs text-muted-foreground">
              <span>مستخدم اليوم: {fmtMinutes(usage.usedMinutesDay)}</span>
              <span>المتبقي اليوم: {remainingText(usage.remainingMinutesDay)}</span>
              <span>مستخدم الشهر: {fmtMinutes(usage.usedMinutesMonth)}</span>
              <span>المتبقي الشهر: {remainingText(usage.remainingMinutesMonth)}</span>
            </div>
          )}
          {result?.decision.reasonAr && (
            <div className={`flex items-start gap-2 border-t border-border pt-2 text-xs ${blocked ? "text-destructive" : overrideRequired ? "text-amber-600" : "text-muted-foreground"}`}>
              {blocked ? <Ban className="mt-0.5 h-3.5 w-3.5 shrink-0" /> : overrideRequired ? <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" /> : <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />}
              <span>{result.decision.reasonAr}</span>
            </div>
          )}
        </div>
      )}

      <div className="space-y-1">
        <Label>السبب {selected && !selected.paid ? "" : "(اختياري)"}</Label>
        <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} className="w-full border border-border bg-secondary px-3 py-2 text-sm" placeholder="سبب الاستئذان" />
      </div>

      {overrideRequired && (
        <div className="space-y-1">
          <Label>سبب تجاوز الحد (إلزامي)</Label>
          <textarea value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} rows={2} className="w-full border border-amber-500/60 bg-amber-500/5 px-3 py-2 text-sm" placeholder="وضّح مبرّر تجاوز الحد ليتمكّن المعتمِد من الموافقة" />
        </div>
      )}

      <div className="flex justify-end gap-2 border-t border-border pt-3">
        <button onClick={onCancel} className="h-10 px-4 text-sm text-muted-foreground hover:text-foreground">إلغاء</button>
        <button onClick={submit} disabled={!canSubmit} className="inline-flex h-10 items-center gap-2 bg-primary px-5 text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-50">
          {submitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />} إرسال الطلب
        </button>
      </div>
    </div>
  );
}

function toMinutes(hhmm: string): number {
  const [h, m] = hhmm.split(":").map(Number);
  return (h || 0) * 60 + (m || 0);
}
function Label({ children }: { children: React.ReactNode }) {
  return <label className="text-xs font-bold uppercase tracking-wider text-muted-foreground">{children}</label>;
}
