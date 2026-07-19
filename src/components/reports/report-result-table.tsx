"use client";

import { useState } from "react";
import { AlertTriangle, ChevronDown, ChevronLeft, FileX2 } from "lucide-react";
import { ReportColumn, ReportGroup, ReportResult, ReportRow } from "@/lib/api/reports";
import { ReportCellValue, cellAlignClass, formatAggregate } from "./report-cell";

/**
 * Renders a `ReportResult`.
 *
 * Two shape rules from `ReportRowShaper.Shape` drive the branching here:
 *  - `groups` and `rows` are mutually exclusive — grouped runs return `groups` and leave the
 *    top-level `rows` empty, so branch on `groups.length > 0`.
 *  - a `ReportGroup` has either `subGroups` (non-leaf) or `rows` (leaf), never both.
 *
 * `aggregates` and `grandTotals` are keyed by the measure column's `code`, and only columns
 * with `isMeasure && aggregation != null` appear in them.
 */

interface ReportResultTableProps {
  result: ReportResult;
  page: number;
  onPageChange: (page: number) => void;
  loading: boolean;
}

export function ReportResultTable({ result, page, onPageChange, loading }: ReportResultTableProps) {
  const { columns, groups, rows, grandTotals, totalCount, pageSize, truncated } = result;
  const isGrouped = groups.length > 0;
  const hasGrandTotals = Object.keys(grandTotals ?? {}).length > 0;
  // Grouped runs return every group regardless of page/pageSize — a pager there would do nothing.
  const totalPages = Math.max(1, Math.ceil(totalCount / Math.max(pageSize, 1)));
  const showPager = !isGrouped && totalPages > 1;

  const isEmpty = !isGrouped && rows.length === 0;

  return (
    <div className="space-y-3" dir="rtl">
      {truncated && (
        <div className="flex items-start gap-2.5 border border-amber-600/40 bg-amber-500/10 px-4 py-3 text-sm">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400" />
          <div>
            <p className="font-medium text-amber-700 dark:text-amber-300">النتائج غير مكتملة</p>
            <p className="mt-0.5 text-amber-700/80 dark:text-amber-300/80">
              تجاوز التقرير الحد الأقصى للصفوف، وتم اقتطاع النتائج. الإجماليات والصفحات محسوبة على
              الجزء المعروض فقط — أضف معايير لتضييق النطاق للحصول على أرقام دقيقة.
            </p>
          </div>
        </div>
      )}

      {isEmpty ? (
        <div className="flex flex-col items-center justify-center border border-border bg-card p-12 text-center">
          <FileX2 className="mb-3 h-10 w-10 text-muted-foreground" />
          <p className="text-sm text-muted-foreground">لا توجد بيانات مطابقة للمعايير المحددة.</p>
        </div>
      ) : (
        <div className="border border-border bg-card overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted-foreground">
                {columns.map((c) => (
                  <th key={c.code} className={`whitespace-nowrap px-4 py-3 font-medium ${cellAlignClass(c)}`}>
                    {c.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {isGrouped
                ? groups.map((g, i) => (
                    <GroupRows key={`${g.fieldCode}:${String(g.key)}:${i}`} group={g} columns={columns} depth={0} />
                  ))
                : rows.map((row, i) => <DataRow key={i} row={row} columns={columns} depth={0} />)}
            </tbody>
            {hasGrandTotals && (
              <tfoot>
                <tr className="border-t-2 border-border bg-secondary/60 font-bold">
                  {columns.map((c, i) => (
                    <td key={c.code} className={`whitespace-nowrap px-4 py-3 ${cellAlignClass(c)}`}>
                      {i === 0 && !(c.code in grandTotals) ? "الإجمالي العام" : null}
                      {c.code in grandTotals ? formatAggregate(grandTotals[c.code], c) : null}
                    </td>
                  ))}
                </tr>
              </tfoot>
            )}
          </table>
        </div>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3 text-sm">
        <span className="text-muted-foreground">
          {totalCount.toLocaleString("en-US")} سجل
          {isGrouped && ` · ${groups.length.toLocaleString("en-US")} مجموعة`}
        </span>
        {showPager && (
          <div className="flex items-center gap-2">
            <button
              type="button"
              disabled={page <= 1 || loading}
              onClick={() => onPageChange(page - 1)}
              className="h-8 border border-border px-3 hover:bg-secondary/70 disabled:opacity-40"
            >
              السابق
            </button>
            <span className="text-xs tabular-nums text-muted-foreground">
              {page} / {totalPages}
            </span>
            <button
              type="button"
              disabled={page >= totalPages || loading}
              onClick={() => onPageChange(page + 1)}
              className="h-8 border border-border px-3 hover:bg-secondary/70 disabled:opacity-40"
            >
              التالي
            </button>
          </div>
        )}
        {isGrouped && (
          <span className="text-xs text-muted-foreground">
            التقارير المجمّعة تُعرض كاملة بدون تقسيم صفحات.
          </span>
        )}
      </div>
    </div>
  );
}

function DataRow({ row, columns, depth }: { row: ReportRow; columns: ReportColumn[]; depth: number }) {
  return (
    <tr className="border-b border-border/60 last:border-0 hover:bg-secondary/40">
      {columns.map((c, i) => (
        <td
          key={c.code}
          className={`whitespace-nowrap px-4 py-2.5 ${cellAlignClass(c)}`}
          style={i === 0 && depth > 0 ? { paddingInlineStart: `${depth * 1.25 + 1}rem` } : undefined}
        >
          <ReportCellValue value={row[c.code]} column={c} />
        </td>
      ))}
    </tr>
  );
}

/**
 * One group renders as: a collapsible header row, its children (sub-groups or leaf rows), then a
 * subtotal row built from `aggregates`. Returns a fragment of `<tr>`s so the whole tree stays in
 * a single `<tbody>` and columns keep their alignment.
 */
function GroupRows({ group, columns, depth }: { group: ReportGroup; columns: ReportColumn[]; depth: number }) {
  const [open, setOpen] = useState(true);
  const hasAggregates = Object.keys(group.aggregates ?? {}).length > 0;
  const indent = `${depth * 1.25 + 1}rem`;

  return (
    <>
      <tr className="border-b border-border/60 bg-secondary/40">
        <td colSpan={columns.length} className="px-4 py-2" style={{ paddingInlineStart: indent }}>
          <button
            type="button"
            onClick={() => setOpen((v) => !v)}
            className="inline-flex items-center gap-1.5 font-medium hover:text-primary"
          >
            {open ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronLeft className="h-3.5 w-3.5" />}
            <span>{group.label || "—"}</span>
            <span className="text-xs font-normal tabular-nums text-muted-foreground">
              ({group.count.toLocaleString("en-US")})
            </span>
          </button>
        </td>
      </tr>

      {open &&
        (group.subGroups.length > 0
          ? group.subGroups.map((g, i) => (
              <GroupRows key={`${g.fieldCode}:${String(g.key)}:${i}`} group={g} columns={columns} depth={depth + 1} />
            ))
          : group.rows.map((row, i) => <DataRow key={i} row={row} columns={columns} depth={depth + 1} />))}

      {hasAggregates && (
        <tr className="border-b border-border bg-secondary/20 text-xs font-medium">
          {columns.map((c, i) => (
            <td
              key={c.code}
              className={`whitespace-nowrap px-4 py-2 ${cellAlignClass(c)}`}
              style={i === 0 ? { paddingInlineStart: indent } : undefined}
            >
              {i === 0 && !(c.code in group.aggregates) ? (
                <span className="text-muted-foreground">إجمالي {group.label}</span>
              ) : null}
              {c.code in group.aggregates ? formatAggregate(group.aggregates[c.code], c) : null}
            </td>
          ))}
        </tr>
      )}
    </>
  );
}
