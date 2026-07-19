"use client";

import { ReportFilter } from "@/lib/api/reports";

/**
 * Renders one input per runtime-parameter filter. Values are keyed by the parameter key the
 * backend expects: `fieldCode`, plus `fieldCode:to` for a Between filter's upper bound.
 *
 * Date-valued filters get real date pickers plus quick range presets — the difference between
 * "type 2026-02-01 in exactly the right format" and picking a month. `ReportFilter` carries no
 * field type, so date-ness is inferred from the shape of the stored default (the seeder writes
 * ISO dates); see `looksLikeDate`.
 */

const ISO_DATE = /^\d{4}-\d{2}-\d{2}/;

/** A filter whose stored bounds are ISO dates is a date filter. */
function looksLikeDate(f: ReportFilter): boolean {
  return ISO_DATE.test(f.value ?? "") || ISO_DATE.test(f.valueTo ?? "");
}

function isoDay(d: Date): string {
  // Local calendar day, not UTC: toISOString on a local midnight shifts the date backwards for
  // every timezone east of UTC, which is every timezone this product ships to.
  const m = d.getMonth() + 1;
  const day = d.getDate();
  return `${d.getFullYear()}-${m < 10 ? "0" : ""}${m}-${day < 10 ? "0" : ""}${day}`;
}

const PRESETS: { label: string; range: () => { from: string; to: string } }[] = [
  {
    label: "اليوم",
    range: () => { const t = new Date(); return { from: isoDay(t), to: isoDay(t) }; },
  },
  {
    label: "هذا الأسبوع",
    range: () => {
      const t = new Date();
      const start = new Date(t); start.setDate(t.getDate() - t.getDay());
      const end = new Date(start); end.setDate(start.getDate() + 6);
      return { from: isoDay(start), to: isoDay(end) };
    },
  },
  {
    label: "هذا الشهر",
    range: () => {
      const t = new Date();
      return {
        from: isoDay(new Date(t.getFullYear(), t.getMonth(), 1)),
        to: isoDay(new Date(t.getFullYear(), t.getMonth() + 1, 0)),
      };
    },
  },
  {
    label: "هذا العام",
    range: () => {
      const t = new Date();
      return { from: isoDay(new Date(t.getFullYear(), 0, 1)), to: isoDay(new Date(t.getFullYear(), 11, 31)) };
    },
  },
];

/** "EmployeeId" → "Employee". Better than a raw column name as the field's label. */
function humanize(fieldCode: string): string {
  return fieldCode.replace(/Id$/, "").replace(/([a-z0-9])([A-Z])/g, "$1 $2");
}

export function ReportParameters({
  filters, values, onChange, onRun,
}: {
  filters: ReportFilter[];
  values: Record<string, string>;
  onChange: (key: string, value: string) => void;
  onRun: () => void;
}) {
  if (filters.length === 0) return null;

  const applyPreset = (fieldCode: string, range: { from: string; to: string }) => {
    onChange(fieldCode, range.from);
    onChange(`${fieldCode}:to`, range.to);
  };

  const clearAll = () => {
    for (const f of filters) {
      onChange(f.fieldCode, "");
      if (f.operator === "Between") onChange(`${f.fieldCode}:to`, "");
    }
  };

  return (
    <div className="border border-border bg-card p-4 space-y-3">
      <div className="flex items-center justify-between gap-3">
        <h3 className="font-semibold text-sm">معاملات التشغيل</h3>
        <button onClick={clearAll} className="text-xs text-muted-foreground underline hover:no-underline">
          مسح الكل
        </button>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        {filters.map((f) => {
          const isDate = looksLikeDate(f);
          const isBetween = f.operator === "Between";
          const inputType = isDate ? "date" : "text";
          return (
            <div key={f.id} className="space-y-1">
              <label className="block text-xs text-muted-foreground">{humanize(f.fieldCode)}</label>
              <div className="flex items-center gap-1">
                <input
                  type={inputType}
                  value={values[f.fieldCode] ?? ""}
                  onChange={(e) => onChange(f.fieldCode, e.target.value)}
                  placeholder={isBetween ? "من" : "القيمة"}
                  className="h-9 w-40 border border-border bg-background px-3 text-sm"
                />
                {isBetween && (
                  <input
                    type={inputType}
                    value={values[`${f.fieldCode}:to`] ?? ""}
                    onChange={(e) => onChange(`${f.fieldCode}:to`, e.target.value)}
                    placeholder="إلى"
                    className="h-9 w-40 border border-border bg-background px-3 text-sm"
                  />
                )}
              </div>
              {isDate && isBetween && (
                <div className="flex flex-wrap gap-1 pt-1">
                  {PRESETS.map((p) => (
                    <button
                      key={p.label}
                      type="button"
                      onClick={() => applyPreset(f.fieldCode, p.range())}
                      className="border border-border bg-secondary px-2 py-0.5 text-[11px] hover:bg-secondary/70"
                    >
                      {p.label}
                    </button>
                  ))}
                </div>
              )}
            </div>
          );
        })}
        <button onClick={onRun} className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground">
          تشغيل
        </button>
      </div>

      <p className="text-xs text-muted-foreground">اترك أي معيار فارغاً لتجاهله.</p>
    </div>
  );
}
