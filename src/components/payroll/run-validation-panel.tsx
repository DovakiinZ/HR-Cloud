"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Loader2, ExternalLink } from "lucide-react";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { getRunValidation, type RunValidationRow, type Paged } from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

function SeverityBadge({ severity }: { severity: string }) {
  if (severity === "Error") {
    return (
      <Badge variant="outline" className="text-xs bg-destructive/10 text-destructive border-destructive/20">
        خطأ — مانع
      </Badge>
    );
  }
  if (severity === "Warning") {
    return (
      <Badge variant="outline" className="text-xs bg-amber-500/10 text-amber-700 border-amber-500/20">
        تحذير
      </Badge>
    );
  }
  if (severity === "Information") {
    return (
      <Badge variant="outline" className="text-xs bg-blue-500/10 text-blue-600 border-blue-500/20">
        معلومة
      </Badge>
    );
  }
  return (
    <Badge variant="outline" className="text-xs">
      {severity}
    </Badge>
  );
}

function DeepLink({ row }: { row: RunValidationRow }) {
  // Best-effort: link to employee profile when relatedEntityType is Employee
  if (row.relatedEntityType === "Employee" && row.relatedEntityId) {
    return (
      <Link
        href={`/employees/${row.relatedEntityId}`}
        className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
      >
        <ExternalLink className="h-3 w-3" />
        عرض الملف
      </Link>
    );
  }
  // Fallback: show targetModule/targetScreen as text
  if (row.targetScreen || row.targetModule) {
    return (
      <span className="text-xs text-muted-foreground">
        {row.targetModule}
        {row.targetModule && row.targetScreen ? " / " : ""}
        {row.targetScreen}
      </span>
    );
  }
  return null;
}

interface RunValidationPanelProps {
  runId: string;
}

const PAGE_SIZE = 20;

export function RunValidationPanel({ runId }: RunValidationPanelProps) {
  const [data, setData] = useState<Paged<RunValidationRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getRunValidation(runId, { page, pageSize: PAGE_SIZE });
      setData(result);
    } catch (err) {
      notifyError(err, "تعذر تحميل نتائج التحقق");
    } finally {
      setLoading(false);
    }
  }, [runId, page]);

  useEffect(() => { load(); }, [load]);

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1;

  const errorCount = data?.items.filter((r) => r.severity === "Error").length ?? 0;
  const warnCount = data?.items.filter((r) => r.severity === "Warning").length ?? 0;

  return (
    <div className="space-y-3">
      {/* Summary bar */}
      {data && data.total > 0 && (
        <div className="flex items-center gap-3 text-xs text-muted-foreground border border-border bg-card px-4 py-2">
          {errorCount > 0 && (
            <span className="text-destructive font-semibold">{errorCount} خطأ مانع</span>
          )}
          {warnCount > 0 && (
            <span className="text-amber-600">{warnCount} تحذير</span>
          )}
          {errorCount === 0 && warnCount === 0 && (
            <span className="text-green-600 font-semibold">لا توجد مشكلات</span>
          )}
        </div>
      )}

      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["الخطورة", "الرسالة", "الإجراء المقترح", "الرابط"].map((h, i) => (
                <TableHead key={i} className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">
                  {h}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={4} className="py-12 text-center text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...
                </TableCell>
              </TableRow>
            ) : !data || data.items.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={4} className="py-12 text-center text-sm text-muted-foreground">
                  لا توجد نتائج تحقق
                </TableCell>
              </TableRow>
            ) : data.items.map((row, idx) => (
              <TableRow key={`${row.code}-${idx}`} className="border-border hover:bg-card/50">
                <TableCell>
                  <SeverityBadge severity={row.severity} />
                </TableCell>
                <TableCell className="text-sm max-w-sm">
                  <div>{row.message}</div>
                  {row.code && (
                    <div className="font-mono text-[10px] text-muted-foreground mt-0.5">{row.code}</div>
                  )}
                </TableCell>
                <TableCell className="text-sm text-muted-foreground max-w-xs">
                  {row.suggestedAction || "—"}
                </TableCell>
                <TableCell>
                  <DeepLink row={row} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {data && data.total > PAGE_SIZE && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            {data.total.toLocaleString("ar-SA")} سجل — صفحة {page} من {totalPages}
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
