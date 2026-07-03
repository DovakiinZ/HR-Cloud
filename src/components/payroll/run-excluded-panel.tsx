"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Loader2, ExternalLink } from "lucide-react";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { getRunExcluded, type RunExcludedRow, type Paged } from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

/** Map backend reasonCode strings to Arabic labels. */
const REASON_CODE_AR: Record<string, string> = {
  NoActiveSalary: "لا يوجد راتب نشط",
  NoActiveContract: "لا يوجد عقد نشط",
  Terminated: "منتهي الخدمة",
  OutOfScope: "خارج النطاق",
  AlreadyIncluded: "مشمول مسبقاً",
  ManuallyExcluded: "مستبعد يدوياً",
  MissingPaymentMethod: "طريقة دفع مفقودة",
  NegativeSalary: "راتب سالب",
  NoDefinitionVersion: "لا يوجد إصدار تعريف",
  ZeroSalary: "راتب صفري",
};

function reasonLabel(code: string): string {
  return REASON_CODE_AR[code] ?? code;
}

interface RunExcludedPanelProps {
  runId: string;
}

const PAGE_SIZE = 20;

export function RunExcludedPanel({ runId }: RunExcludedPanelProps) {
  const [data, setData] = useState<Paged<RunExcludedRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getRunExcluded(runId, { page, pageSize: PAGE_SIZE });
      setData(result);
    } catch (err) {
      notifyError(err, "تعذر تحميل بيانات المستثنين");
    } finally {
      setLoading(false);
    }
  }, [runId, page]);

  useEffect(() => { load(); }, [load]);

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1;

  return (
    <div className="space-y-3">
      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["الموظف", "سبب الاستثناء", "التفاصيل", ""].map((h, i) => (
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
                  لا يوجد موظفون مستثنون
                </TableCell>
              </TableRow>
            ) : data.items.map((row) => (
              <TableRow key={row.employeeId} className="border-border hover:bg-card/50">
                <TableCell>
                  <div className="font-medium">{row.employeeName}</div>
                  <div className="font-mono text-[10px] text-muted-foreground">{row.employeeNumber}</div>
                </TableCell>
                <TableCell>
                  <Badge variant="outline" className="text-xs bg-amber-500/10 text-amber-700 border-amber-500/20">
                    {reasonLabel(row.reasonCode)}
                  </Badge>
                </TableCell>
                <TableCell className="text-sm text-muted-foreground max-w-xs">
                  {row.detail || "—"}
                </TableCell>
                <TableCell>
                  <Link
                    href={`/employees/${row.employeeId}`}
                    className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
                  >
                    <ExternalLink className="h-3 w-3" />
                    عرض الملف
                  </Link>
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
