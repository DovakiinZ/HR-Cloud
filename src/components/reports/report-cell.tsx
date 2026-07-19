"use client";

import { FieldKind, ReportColumn } from "@/lib/api/reports";

/**
 * Type-aware cell formatting for report results.
 *
 * Formatting is driven by `ReportColumn.type` (the backend FieldKind), never by the JS runtime
 * type of the value. Branching on `typeof v === "number"` is the bug in
 * `src/components/dashboard/widget-renderer.tsx` (renderCell): it pushes every number through
 * `Intl.NumberFormat` with grouping, so a year `2026` renders as "2,026" and an employee id
 * `1001` as "1,001". `FieldKind.Number` here is an ungrouped integer for exactly that reason —
 * only Decimal/Currency/Percentage are money-ish and get separators.
 */

const INTEGER = new Intl.NumberFormat("en-US", { useGrouping: false, maximumFractionDigits: 0 });
const GROUPED = new Intl.NumberFormat("en-US", {
  useGrouping: true,
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

/**
 * Honor a .NET-style numeric format pattern when the field carries one — `N0`/`F3` and
 * `#,##0.000` both just tell us how many decimals to show. Anything we can't read falls
 * back to the 2-decimal grouped default.
 */
function grouped(n: number, formatPattern?: string | null): string {
  if (formatPattern) {
    const std = /^[NnFfDd](\d+)$/.exec(formatPattern.trim());
    const custom = formatPattern.includes(".") ? /\.(0+|#+)/.exec(formatPattern) : null;
    const digits = std ? Number(std[1]) : custom ? custom[1].length : null;
    if (digits !== null && digits >= 0 && digits <= 6) {
      return new Intl.NumberFormat("en-US", {
        useGrouping: true,
        minimumFractionDigits: digits,
        maximumFractionDigits: digits,
      }).format(n);
    }
  }
  return GROUPED.format(n);
}

/** Kinds that are numeric measures — right-aligned and rendered with `tabular-nums`. */
const NUMERIC_KINDS = new Set<FieldKind>(["Number", "Decimal", "Currency", "Percentage"]);

export const isNumericKind = (kind: FieldKind) => NUMERIC_KINDS.has(kind);

function toNumber(v: unknown): number | null {
  if (typeof v === "number") return Number.isFinite(v) ? v : null;
  if (typeof v === "string" && v.trim() !== "") {
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

function toDate(v: unknown): Date | null {
  if (v instanceof Date) return Number.isNaN(v.getTime()) ? null : v;
  if (typeof v === "string" || typeof v === "number") {
    const d = new Date(v);
    return Number.isNaN(d.getTime()) ? null : d;
  }
  return null;
}

function toBool(v: unknown): boolean | null {
  if (typeof v === "boolean") return v;
  if (typeof v === "number") return v !== 0;
  if (typeof v === "string") {
    const s = v.trim().toLowerCase();
    if (s === "true" || s === "1") return true;
    if (s === "false" || s === "0") return false;
  }
  return null;
}

/**
 * Format a raw row value as display text according to the column's FieldKind.
 * Returns `null` for null/undefined/unparseable so the caller can render the muted em-dash.
 */
export function formatValue(value: unknown, kind: FieldKind, formatPattern?: string | null): string | null {
  if (value === null || value === undefined || value === "") return null;

  switch (kind) {
    case "Number": {
      const n = toNumber(value);
      // No thousands separator: this kind carries years, counts, ids and codes.
      return n === null ? String(value) : INTEGER.format(n);
    }
    case "Decimal": {
      const n = toNumber(value);
      return n === null ? String(value) : grouped(n, formatPattern);
    }
    case "Currency": {
      const n = toNumber(value);
      return n === null ? String(value) : `${grouped(n, formatPattern)} ر.س`;
    }
    case "Percentage": {
      const n = toNumber(value);
      return n === null ? String(value) : `${grouped(n, formatPattern)}%`;
    }
    case "Date": {
      const d = toDate(value);
      return d === null ? String(value) : d.toLocaleDateString("ar");
    }
    case "DateTime": {
      const d = toDate(value);
      if (d === null) return String(value);
      return `${d.toLocaleDateString("ar")} ${d.toLocaleTimeString("ar", { hour: "2-digit", minute: "2-digit" })}`;
    }
    case "Boolean": {
      const b = toBool(value);
      return b === null ? String(value) : b ? "نعم" : "لا";
    }
    // Reference / Enum / Text / Guid — the backend already resolved these to display text.
    default:
      return String(value);
  }
}

/** A single `<td>` body: formatted text, or a muted em-dash when the value is absent. */
export function ReportCellValue({ value, column }: { value: unknown; column: ReportColumn }) {
  const text = formatValue(value, column.type, column.formatPattern);
  if (text === null) return <span className="text-muted-foreground">—</span>;
  return <>{text}</>;
}

/** Aggregate/total values are always numeric doubles keyed by the measure column's code. */
export function formatAggregate(value: number | undefined, column: ReportColumn): string {
  if (value === null || value === undefined || Number.isNaN(value)) return "—";
  // Counts stay ungrouped integers; sums/averages of money read better grouped.
  if (column.aggregation === "Count") return INTEGER.format(value);
  return formatValue(value, column.type === "Number" ? "Decimal" : column.type, column.formatPattern) ?? "—";
}

/** Alignment + numeral rendering for both header and body cells of a column. */
export function cellAlignClass(column: ReportColumn): string {
  return column.isMeasure || isNumericKind(column.type) ? "text-left tabular-nums" : "text-right";
}
