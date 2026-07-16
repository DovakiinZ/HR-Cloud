"use client";

import { ReportFilter } from "@/lib/api/reports";

/**
 * Renders one input per runtime-parameter filter. Values are keyed by the parameter key the
 * backend expects: `fieldCode`, plus `fieldCode:to` for a Between filter's upper bound.
 */
export function ReportParameters({
  filters, values, onChange, onRun,
}: {
  filters: ReportFilter[];
  values: Record<string, string>;
  onChange: (key: string, value: string) => void;
  onRun: () => void;
}) {
  if (filters.length === 0) return null;
  return (
    <div className="border border-border bg-card p-4 space-y-3">
      <h3 className="font-semibold text-sm">معاملات التشغيل</h3>
      <div className="flex flex-wrap items-end gap-3">
        {filters.map((f) => (
          <div key={f.id} className="space-y-1">
            <label className="block text-xs text-muted-foreground">{f.fieldCode}</label>
            <div className="flex items-center gap-1">
              <input
                value={values[f.fieldCode] ?? ""}
                onChange={(e) => onChange(f.fieldCode, e.target.value)}
                placeholder={f.operator === "Between" ? "من" : "القيمة"}
                className="h-9 w-40 border border-border bg-background px-3 text-sm"
              />
              {f.operator === "Between" && (
                <input
                  value={values[`${f.fieldCode}:to`] ?? ""}
                  onChange={(e) => onChange(`${f.fieldCode}:to`, e.target.value)}
                  placeholder="إلى"
                  className="h-9 w-40 border border-border bg-background px-3 text-sm"
                />
              )}
            </div>
          </div>
        ))}
        <button onClick={onRun} className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground">تشغيل</button>
      </div>
    </div>
  );
}
