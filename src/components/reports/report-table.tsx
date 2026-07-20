"use client";

import { useState } from "react";
import { ChevronDown, ChevronLeft } from "lucide-react";
import { ReportResult, ReportColumn, ReportGroup } from "@/lib/api/reports";
import { formatValue, formatAggregate } from "./report-cell";

/**
 * Cell text for a column. Driven by ReportColumn.type (the backend FieldKind), never by the JS
 * runtime type: `typeof v === "number" → toLocaleString()` grouped every number, so a year 2026
 * rendered as "2,026" and an employee number 1001 as "1,001".
 */
function fmt(v: unknown, column: ReportColumn): string {
  return formatValue(v, column.type, column.formatPattern) ?? "";
}

function GroupRows({ group, columns, depth }: { group: ReportGroup; columns: ReportColumn[]; depth: number }) {
  const [open, setOpen] = useState(true);

  return (
    <>
      <tr className="bg-secondary/60">
        <td colSpan={columns.length} className="px-4 py-2 font-semibold" style={{ paddingInlineStart: 16 + depth * 16 }}>
          <button
            type="button"
            onClick={() => setOpen((o) => !o)}
            aria-expanded={open}
            className="flex items-center gap-1.5 text-right hover:opacity-80"
          >
            {/* RTL: collapsed points to the start of the line, i.e. left. */}
            {open ? <ChevronDown className="h-4 w-4 shrink-0" /> : <ChevronLeft className="h-4 w-4 shrink-0" />}
            <span>{group.label}</span>
            <span className="text-xs font-normal text-muted-foreground">({group.count})</span>
          </button>
        </td>
      </tr>

      {open && (group.subGroups?.length
        ? group.subGroups.map((g, i) => <GroupRows key={i} group={g} columns={columns} depth={depth + 1} />)
        : group.rows.map((row, i) => (
            <tr key={i} className="border-b border-border/60">
              {columns.map((c) => (
                <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>{fmt(row[c.code], c)}</td>
              ))}
            </tr>
          )))}

      {/* The subtotal stays visible when collapsed — it is the summary the collapse is for. */}
      <tr className="border-b border-border bg-secondary/30 text-sm font-medium">
        {columns.map((c, idx) => (
          <td key={c.code} className={`px-4 py-1.5 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>
            {idx === 0 ? `${group.label} — إجمالي` : c.isMeasure ? formatAggregate(group.aggregates?.[c.code], c) : ""}
          </td>
        ))}
      </tr>
    </>
  );
}

export function ReportTable({ result }: { result: ReportResult }) {
  const cols = result.columns;
  const grouped = result.groups?.length > 0;
  return (
    <div className="border border-border bg-card overflow-x-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-border text-right text-xs uppercase tracking-wider text-muted-foreground">
            {cols.map((c) => (
              <th key={c.code} className={`px-4 py-3 font-medium ${c.isMeasure ? "text-left" : ""}`}>{c.label}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {grouped
            ? result.groups.map((g, i) => <GroupRows key={i} group={g} columns={cols} depth={0} />)
            : result.rows.map((row, i) => (
                <tr key={i} className="border-b border-border/60 hover:bg-secondary/40">
                  {cols.map((c) => (
                    <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>{fmt(row[c.code], c)}</td>
                  ))}
                </tr>
              ))}
          {Object.keys(result.grandTotals ?? {}).length > 0 && (
            <tr className="border-t-2 border-border bg-secondary/50 font-semibold">
              {cols.map((c, idx) => (
                <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>
                  {idx === 0 ? "الإجمالي العام" : c.isMeasure ? formatAggregate(result.grandTotals[c.code], c) : ""}
                </td>
              ))}
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
