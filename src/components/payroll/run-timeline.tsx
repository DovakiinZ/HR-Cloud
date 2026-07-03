"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, ArrowLeft, Clock } from "lucide-react";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { getRunCalculations, STATE_AR, money, type RunTimelineEntry, type RunCalculationRow, type Paged } from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

function TimelineEntry({ entry }: { entry: RunTimelineEntry }) {
  return (
    <div className="flex items-start gap-3">
      <div className="mt-1 flex h-6 w-6 shrink-0 items-center justify-center rounded-full border border-border bg-card">
        <ArrowLeft className="h-3 w-3 text-muted-foreground" />
      </div>
      <div className="flex-1 min-w-0">
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <span className="text-muted-foreground">{STATE_AR[entry.fromState] ?? entry.fromState}</span>
          <span className="text-muted-foreground">←</span>
          <span className="font-medium">{STATE_AR[entry.toState] ?? entry.toState}</span>
        </div>
        {entry.reason && (
          <p className="text-xs text-muted-foreground mt-0.5">{entry.reason}</p>
        )}
        <p className="text-xs text-muted-foreground mt-0.5" dir="ltr">
          {new Date(entry.at).toLocaleString("ar-SA")}
        </p>
      </div>
    </div>
  );
}

interface CalcHistoryProps {
  runId: string;
  currency: string;
}

const CALC_PAGE_SIZE = 10;

function CalcHistory({ runId, currency }: CalcHistoryProps) {
  const [data, setData] = useState<Paged<RunCalculationRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getRunCalculations(runId, { page, pageSize: CALC_PAGE_SIZE });
      setData(result);
    } catch (err) {
      notifyError(err, "تعذر تحميل سجل الاحتسابات");
    } finally {
      setLoading(false);
    }
  }, [runId, page]);

  useEffect(() => { load(); }, [load]);

  const totalPages = data ? Math.ceil(data.total / CALC_PAGE_SIZE) : 1;

  if (loading && !data) {
    return (
      <div className="py-8 text-center text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...
      </div>
    );
  }

  if (!data || data.items.length === 0) {
    return <p className="text-sm text-muted-foreground py-4">لا يوجد سجل احتسابات</p>;
  }

  return (
    <div className="space-y-3">
      <div className="space-y-2">
        {data.items.map((calc) => (
          <div key={calc.version} className="border border-border bg-card px-4 py-3 flex flex-wrap items-start gap-4">
            <div className="shrink-0">
              <div className="text-xs font-bold uppercase tracking-wider text-muted-foreground">إصدار</div>
              <div className="text-lg font-mono font-bold" dir="ltr">#{calc.version}</div>
            </div>
            <div className="flex-1 min-w-0 space-y-1">
              <div className="text-xs text-muted-foreground" dir="ltr">
                {new Date(calc.calculatedAt).toLocaleString("ar-SA")} — بواسطة {calc.byUserName}
              </div>
              <div className="flex flex-wrap gap-4 text-sm tabular-nums">
                <span className="text-muted-foreground">
                  الإجمالي: <span className="font-medium text-foreground">{money(calc.gross, currency)}</span>
                </span>
                <span className="text-muted-foreground">
                  الاستقطاعات: <span className="font-medium text-foreground">{money(calc.deductions, currency)}</span>
                </span>
                <span className="text-muted-foreground">
                  الصافي: <span className="font-bold text-foreground">{money(calc.net, currency)}</span>
                </span>
                <span className="text-muted-foreground">
                  {calc.employeeCount} موظف
                </span>
              </div>
            </div>
          </div>
        ))}
      </div>

      {data.total > CALC_PAGE_SIZE && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            {data.total.toLocaleString("ar-SA")} احتساب — صفحة {page} من {totalPages}
          </span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1 || loading}>
              السابق
            </Button>
            <Button variant="outline" size="sm" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages || loading}>
              التالي
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

interface RunTimelineProps {
  runId: string;
  currency: string;
  timeline: RunTimelineEntry[];
}

export function RunTimeline({ runId, currency, timeline }: RunTimelineProps) {
  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
      {/* Lifecycle transitions */}
      <div className="space-y-3">
        <div className="flex items-center gap-2 text-sm font-semibold">
          <Clock className="h-4 w-4 text-muted-foreground" />
          تاريخ الحالات
        </div>
        {timeline.length === 0 ? (
          <p className="text-sm text-muted-foreground">لا يوجد تاريخ حالات</p>
        ) : (
          <div className="space-y-3">
            {timeline.map((entry, idx) => (
              <TimelineEntry key={idx} entry={entry} />
            ))}
          </div>
        )}
      </div>

      {/* Calculation history */}
      <div className="space-y-3">
        <div className="flex items-center gap-2 text-sm font-semibold">
          <Clock className="h-4 w-4 text-muted-foreground" />
          سجل الاحتسابات
        </div>
        <CalcHistory runId={runId} currency={currency} />
      </div>
    </div>
  );
}
