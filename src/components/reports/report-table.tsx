"use client";

import { ReportResult, ReportColumn, ReportGroup } from "@/lib/api/reports";

function fmt(v: unknown): string {
  if (v === null || v === undefined) return "";
  if (typeof v === "number") return v.toLocaleString();
  return String(v);
}

function GroupRows({ group, columns, depth }: { group: ReportGroup; columns: ReportColumn[]; depth: number }) {
  return (
    <>
      <tr className="bg-secondary/60">
        <td colSpan={columns.length} className="px-4 py-2 font-semibold" style={{ paddingInlineStart: 16 + depth * 16 }}>
          {group.label}
        </td>
      </tr>
      {group.subGroups?.length
        ? group.subGroups.map((g, i) => <GroupRows key={i} group={g} columns={columns} depth={depth + 1} />)
        : group.rows.map((row, i) => (
            <tr key={i} className="border-b border-border/60">
              {columns.map((c) => (
                <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>{fmt(row[c.code])}</td>
              ))}
            </tr>
          ))}
      <tr className="border-b border-border bg-secondary/30 text-sm font-medium">
        {columns.map((c, idx) => (
          <td key={c.code} className={`px-4 py-1.5 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>
            {idx === 0 ? `${group.label} — إجمالي` : c.isMeasure ? fmt(group.aggregates?.[c.code]) : ""}
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
                    <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>{fmt(row[c.code])}</td>
                  ))}
                </tr>
              ))}
          {Object.keys(result.grandTotals ?? {}).length > 0 && (
            <tr className="border-t-2 border-border bg-secondary/50 font-semibold">
              {cols.map((c, idx) => (
                <td key={c.code} className={`px-4 py-2 ${c.isMeasure ? "text-left tabular-nums" : ""}`}>
                  {idx === 0 ? "الإجمالي العام" : c.isMeasure ? fmt(result.grandTotals[c.code]) : ""}
                </td>
              ))}
            </tr>
          )}
        </tbody>
      </table>
    </div>
  );
}
