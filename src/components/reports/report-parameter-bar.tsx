"use client";

import { useMemo } from "react";
import { CalendarRange, Loader2, Play, RotateCcw, SlidersHorizontal } from "lucide-react";
import {
  CatalogField,
  FieldKind,
  ReportFilter,
  ReportParameters,
} from "@/lib/api/reports";

/**
 * Runtime parameter inputs — everything the user sets BEFORE the report executes.
 *
 * Keying: a parameter is keyed by its `fieldCode`. A `Between` filter's upper bound is keyed
 * `<fieldCode>:to` — that exact suffix is `ReportParameterBinder.UpperBoundSuffix` on the
 * backend, and the export endpoint reads the same keys as `p.<fieldCode>` / `p.<fieldCode>:to`.
 *
 * Blank means "not supplied": the key is omitted entirely so the filter's stored default value
 * applies. Sending an empty string instead would bind an empty filter and return nothing.
 */

export const UPPER_BOUND_SUFFIX = ":to";
export const toKey = (fieldCode: string) => fieldCode;
export const toUpperKey = (fieldCode: string) => `${fieldCode}${UPPER_BOUND_SUFFIX}`;

/** Raw form state: every input's string value, keyed exactly as it will be sent. */
export type ParameterDraft = Record<string, string>;

/** Drop blanks so the backend falls back to each filter's saved default. */
export function draftToParameters(draft: ParameterDraft): ReportParameters {
  const out: ReportParameters = {};
  for (const [key, value] of Object.entries(draft)) {
    if (value !== undefined && value !== null && value.trim() !== "") out[key] = value;
  }
  return out;
}

const DATE_KINDS = new Set<FieldKind>(["Date", "DateTime"]);

/** yyyy-MM-dd in local time — `toISOString` would shift the day across the UTC boundary. */
function isoDay(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

type PresetKey = "today" | "week" | "month" | "year";

const PRESETS: { key: PresetKey; label: string }[] = [
  { key: "today", label: "اليوم" },
  { key: "week", label: "هذا الأسبوع" },
  { key: "month", label: "هذا الشهر" },
  { key: "year", label: "هذا العام" },
];

/** Saturday-start week, matching the Saudi working week. */
function presetRange(key: PresetKey): { from: string; to: string } {
  const now = new Date();
  switch (key) {
    case "today":
      return { from: isoDay(now), to: isoDay(now) };
    case "week": {
      const start = new Date(now);
      start.setDate(now.getDate() - ((now.getDay() + 1) % 7));
      const end = new Date(start);
      end.setDate(start.getDate() + 6);
      return { from: isoDay(start), to: isoDay(end) };
    }
    case "month":
      return {
        from: isoDay(new Date(now.getFullYear(), now.getMonth(), 1)),
        to: isoDay(new Date(now.getFullYear(), now.getMonth() + 1, 0)),
      };
    case "year":
      return {
        from: isoDay(new Date(now.getFullYear(), 0, 1)),
        to: isoDay(new Date(now.getFullYear(), 11, 31)),
      };
  }
}

const INPUT_CLASS =
  "h-9 w-full border border-border bg-background px-2 text-sm outline-none focus:border-primary disabled:opacity-50";

interface ReportParameterBarProps {
  /** Every filter on the definition; only `isParameter` ones are rendered. */
  filters: ReportFilter[];
  /** Catalog fields of the primary object, keyed by field code — supplies labels and enum options. */
  fieldsByCode: Map<string, CatalogField>;
  draft: ParameterDraft;
  onChange: (draft: ParameterDraft) => void;
  onRun: () => void;
  onReset: () => void;
  running: boolean;
  /** False until the first successful run — drives the "press Run" hint. */
  hasRun: boolean;
}

export function ReportParameterBar({
  filters,
  fieldsByCode,
  draft,
  onChange,
  onRun,
  onReset,
  running,
  hasRun,
}: ReportParameterBarProps) {
  const parameters = useMemo(() => filters.filter((f) => f.isParameter), [filters]);

  const set = (key: string, value: string) => onChange({ ...draft, [key]: value });
  const setRange = (fieldCode: string, from: string, to: string) =>
    onChange({ ...draft, [toKey(fieldCode)]: from, [toUpperKey(fieldCode)]: to });

  return (
    <div className="border border-border bg-card" dir="rtl">
      <div className="flex items-center justify-between gap-3 border-b border-border px-4 py-3">
        <div className="flex items-center gap-2">
          <SlidersHorizontal className="h-4 w-4 text-muted-foreground" />
          <h2 className="text-sm font-bold">معايير التشغيل</h2>
          {parameters.length > 0 && (
            <span className="border border-border bg-secondary px-1.5 py-0.5 text-xs text-muted-foreground">
              {parameters.length}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          {parameters.length > 0 && (
            <button
              type="button"
              onClick={onReset}
              disabled={running}
              className="inline-flex h-9 items-center gap-1.5 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70 disabled:opacity-50"
            >
              <RotateCcw className="h-3.5 w-3.5" />
              مسح
            </button>
          )}
          <button
            type="button"
            onClick={onRun}
            disabled={running}
            className="inline-flex h-9 items-center gap-2 border border-primary bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
          >
            {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
            تشغيل التقرير
          </button>
        </div>
      </div>

      {parameters.length === 0 ? (
        <p className="px-4 py-3 text-sm text-muted-foreground">
          {hasRun
            ? "لا توجد معايير قابلة للتعديل في هذا التقرير."
            : "لا توجد معايير قابلة للتعديل — اضغط «تشغيل التقرير» لعرض النتائج."}
        </p>
      ) : (
        <div className="grid gap-4 p-4 sm:grid-cols-2 lg:grid-cols-3">
          {parameters.map((filter) => (
            <ParameterInput
              key={filter.id}
              filter={filter}
              field={fieldsByCode.get(filter.fieldCode)}
              draft={draft}
              disabled={running}
              onSet={set}
              onSetRange={setRange}
            />
          ))}
        </div>
      )}

      {!hasRun && parameters.length > 0 && (
        <p className="border-t border-border px-4 py-2 text-xs text-muted-foreground">
          اترك أي معيار فارغاً لاستخدام القيمة الافتراضية المحفوظة في التقرير.
        </p>
      )}
    </div>
  );
}

interface ParameterInputProps {
  filter: ReportFilter;
  field?: CatalogField;
  draft: ParameterDraft;
  disabled: boolean;
  onSet: (key: string, value: string) => void;
  onSetRange: (fieldCode: string, from: string, to: string) => void;
}

function ParameterInput({ filter, field, draft, disabled, onSet, onSetRange }: ParameterInputProps) {
  const label = field?.nameAr || field?.nameEn || filter.fieldCode;
  const kind: FieldKind = field?.fieldType ?? "Text";
  const key = toKey(filter.fieldCode);
  const upperKey = toUpperKey(filter.fieldCode);
  const isDate = DATE_KINDS.has(kind) || field?.isDate === true;

  // IsNull/IsNotNull take no operand — there is nothing for the user to supply.
  if (filter.operator === "IsNull" || filter.operator === "IsNotNull") {
    return (
      <Wrapper label={label} hint={OPERATOR_LABEL[filter.operator]}>
        <div className="flex h-9 items-center border border-dashed border-border px-2 text-sm text-muted-foreground">
          لا يتطلب قيمة
        </div>
      </Wrapper>
    );
  }

  // ── Between on a date field → a from/to pair plus quick presets. ──
  if (filter.operator === "Between" && isDate) {
    return (
      <Wrapper label={label} hint="نطاق تاريخ" span>
        <div className="flex flex-wrap items-center gap-1.5">
          {PRESETS.map((p) => (
            <button
              key={p.key}
              type="button"
              disabled={disabled}
              onClick={() => {
                const { from, to } = presetRange(p.key);
                onSetRange(filter.fieldCode, from, to);
              }}
              className="inline-flex h-7 items-center gap-1 border border-border bg-secondary px-2 text-xs hover:bg-secondary/70 disabled:opacity-50"
            >
              <CalendarRange className="h-3 w-3" />
              {p.label}
            </button>
          ))}
        </div>
        <div className="mt-2 grid grid-cols-2 gap-2">
          <label className="block">
            <span className="mb-1 block text-xs text-muted-foreground">من</span>
            <input
              type="date"
              dir="ltr"
              disabled={disabled}
              value={draft[key] ?? ""}
              onChange={(e) => onSet(key, e.target.value)}
              className={INPUT_CLASS}
            />
          </label>
          <label className="block">
            <span className="mb-1 block text-xs text-muted-foreground">إلى</span>
            <input
              type="date"
              dir="ltr"
              disabled={disabled}
              value={draft[upperKey] ?? ""}
              onChange={(e) => onSet(upperKey, e.target.value)}
              className={INPUT_CLASS}
            />
          </label>
        </div>
      </Wrapper>
    );
  }

  // ── Between on a non-date field → a plain numeric/text pair. ──
  if (filter.operator === "Between") {
    const type = kind === "Number" || kind === "Decimal" || kind === "Currency" || kind === "Percentage" ? "number" : "text";
    return (
      <Wrapper label={label} hint="نطاق">
        <div className="grid grid-cols-2 gap-2">
          <input
            type={type}
            disabled={disabled}
            placeholder="من"
            value={draft[key] ?? ""}
            onChange={(e) => onSet(key, e.target.value)}
            className={INPUT_CLASS}
          />
          <input
            type={type}
            disabled={disabled}
            placeholder="إلى"
            value={draft[upperKey] ?? ""}
            onChange={(e) => onSet(upperKey, e.target.value)}
            className={INPUT_CLASS}
          />
        </div>
      </Wrapper>
    );
  }

  // ── Enum → a select built from the catalog's options. ──
  if (kind === "Enum" && field?.options && field.options.length > 0) {
    return (
      <Wrapper label={label} hint={OPERATOR_LABEL[filter.operator]}>
        <select
          disabled={disabled}
          value={draft[key] ?? ""}
          onChange={(e) => onSet(key, e.target.value)}
          className={INPUT_CLASS}
        >
          <option value="">الكل</option>
          {field.options.map((o) => (
            <option key={o.value} value={String(o.value)}>
              {o.label}
            </option>
          ))}
        </select>
      </Wrapper>
    );
  }

  // ── Boolean → نعم / لا. ──
  if (kind === "Boolean") {
    return (
      <Wrapper label={label} hint={OPERATOR_LABEL[filter.operator]}>
        <select
          disabled={disabled}
          value={draft[key] ?? ""}
          onChange={(e) => onSet(key, e.target.value)}
          className={INPUT_CLASS}
        >
          <option value="">الكل</option>
          <option value="true">نعم</option>
          <option value="false">لا</option>
        </select>
      </Wrapper>
    );
  }

  // ── Single date. ──
  if (isDate) {
    return (
      <Wrapper label={label} hint={OPERATOR_LABEL[filter.operator]}>
        <input
          type="date"
          dir="ltr"
          disabled={disabled}
          value={draft[key] ?? ""}
          onChange={(e) => onSet(key, e.target.value)}
          className={INPUT_CLASS}
        />
      </Wrapper>
    );
  }

  // ── Reference → free text for now; a record picker is out of scope. ──
  // ── Everything else → text / number. ──
  const numeric = kind === "Number" || kind === "Decimal" || kind === "Currency" || kind === "Percentage";
  const isList = filter.operator === "In" || filter.operator === "NotIn";
  return (
    <Wrapper
      label={label}
      hint={OPERATOR_LABEL[filter.operator]}
      note={
        kind === "Reference"
          ? "أدخل المعرّف أو الاسم"
          : isList
            ? "افصل القيم بفاصلة"
            : undefined
      }
    >
      <input
        type={numeric && !isList ? "number" : "text"}
        disabled={disabled}
        value={draft[key] ?? ""}
        onChange={(e) => onSet(key, e.target.value)}
        placeholder={isList ? "قيمة1، قيمة2" : "الكل"}
        className={INPUT_CLASS}
      />
    </Wrapper>
  );
}

const OPERATOR_LABEL: Record<ReportFilter["operator"], string> = {
  Equals: "يساوي",
  NotEquals: "لا يساوي",
  Contains: "يحتوي",
  StartsWith: "يبدأ بـ",
  EndsWith: "ينتهي بـ",
  GreaterThan: "أكبر من",
  LessThan: "أصغر من",
  GreaterThanOrEqual: "أكبر من أو يساوي",
  LessThanOrEqual: "أصغر من أو يساوي",
  Between: "بين",
  In: "ضمن",
  NotIn: "ليس ضمن",
  IsNull: "فارغ",
  IsNotNull: "غير فارغ",
};

function Wrapper({
  label,
  hint,
  note,
  span,
  children,
}: {
  label: string;
  hint?: string;
  note?: string;
  span?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div className={span ? "sm:col-span-2" : undefined}>
      <div className="mb-1.5 flex items-baseline gap-2">
        <span className="text-sm font-medium">{label}</span>
        {hint && <span className="text-xs text-muted-foreground">{hint}</span>}
      </div>
      {children}
      {note && <p className="mt-1 text-xs text-muted-foreground">{note}</p>}
    </div>
  );
}
