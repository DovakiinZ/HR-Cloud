"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Eye, Printer, Download, FileCheck2, FileClock } from "lucide-react";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/permissions";
import {
  getRunPayslips, generateRunPayslips, viewPayslip, downloadPayslip,
  money, type PayslipRow, type Paged,
} from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

interface RunPayslipsPanelProps {
  runId: string;
  currency: string;
}

const PAGE_SIZE = 20;

export function RunPayslipsPanel({ runId, currency }: RunPayslipsPanelProps) {
  const { has } = usePermissions();
  const canView = has("Payroll.Payslip.View");
  const canPrint = has("Payroll.Payslip.Print");
  const canDownload = has("Payroll.Payslip.Download");

  const [data, setData] = useState<Paged<PayslipRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [generating, setGenerating] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setData(await getRunPayslips(runId, { page, pageSize: PAGE_SIZE }));
    } catch (err) {
      notifyError(err, "تعذر تحميل قسائم الرواتب");
    } finally {
      setLoading(false);
    }
  }, [runId, page]);

  useEffect(() => { load(); }, [load]);

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1;

  async function handleGenerate() {
    setGenerating(true);
    try {
      const count = await generateRunPayslips(runId);
      toast.success(`تم إنشاء وأرشفة ${count.toLocaleString("ar-SA")} قسيمة راتب`);
      await load();
    } catch (err) {
      notifyError(err, "تعذر إنشاء قسائم الرواتب");
    } finally {
      setGenerating(false);
    }
  }

  async function withBusy(employeeId: string, fn: () => Promise<void>, fallback: string) {
    setBusyId(employeeId);
    try { await fn(); } catch (err) { notifyError(err, fallback); } finally { setBusyId(null); }
  }

  return (
    <div className="space-y-3">
      {canDownload && (
        <div className="flex justify-end">
          <Button size="sm" onClick={handleGenerate} disabled={generating || loading}>
            {generating ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileCheck2 className="h-4 w-4" />}
            إنشاء وأرشفة القسائم
          </Button>
        </div>
      )}

      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["الموظف", "الإجمالي", "الاستقطاعات", "الصافي", "الأرشفة", "الإجراءات"].map((h, i) => (
                <TableHead key={i} className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">
                  {h}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...
                </TableCell>
              </TableRow>
            ) : !data || data.items.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground">
                  لا توجد قسائم رواتب لهذا المسيّر
                </TableCell>
              </TableRow>
            ) : data.items.map((row) => (
              <TableRow key={row.employeeId} className="border-border hover:bg-card/50">
                <TableCell>
                  <div className="font-medium">{row.employeeName}</div>
                  <div className="font-mono text-[10px] text-muted-foreground">{row.employeeNumber}</div>
                </TableCell>
                <TableCell className="text-sm tabular-nums">{money(row.grossEarnings, row.currency || currency)}</TableCell>
                <TableCell className="text-sm tabular-nums text-muted-foreground">{money(row.totalDeductions, row.currency || currency)}</TableCell>
                <TableCell className="text-sm font-semibold tabular-nums">{money(row.netAmount, row.currency || currency)}</TableCell>
                <TableCell>
                  {row.archived ? (
                    <Badge variant="outline" className="text-xs bg-emerald-500/10 text-emerald-700 border-emerald-500/20">
                      <FileCheck2 className="h-3 w-3" /> مؤرشفة
                    </Badge>
                  ) : (
                    <Badge variant="outline" className="text-xs text-muted-foreground">
                      <FileClock className="h-3 w-3" /> غير مؤرشفة
                    </Badge>
                  )}
                </TableCell>
                <TableCell>
                  <div className="flex items-center gap-1">
                    {canView && (
                      <Button variant="ghost" size="sm" title="عرض" disabled={busyId === row.employeeId}
                        onClick={() => withBusy(row.employeeId, () => viewPayslip(runId, row.employeeId, false), "تعذر عرض القسيمة")}>
                        <Eye className="h-4 w-4" />
                      </Button>
                    )}
                    {canPrint && (
                      <Button variant="ghost" size="sm" title="طباعة" disabled={busyId === row.employeeId}
                        onClick={() => withBusy(row.employeeId, () => viewPayslip(runId, row.employeeId, true), "تعذر طباعة القسيمة")}>
                        <Printer className="h-4 w-4" />
                      </Button>
                    )}
                    {canDownload && (
                      <Button variant="ghost" size="sm" title="تحميل" disabled={busyId === row.employeeId}
                        onClick={() => withBusy(row.employeeId, () => downloadPayslip(runId, row.employeeId, `payslip-${row.employeeNumber}.pdf`), "تعذر تحميل القسيمة")}>
                        <Download className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {data && data.total > PAGE_SIZE && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>{data.total.toLocaleString("ar-SA")} قسيمة — صفحة {page} من {totalPages}</span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1 || loading}>السابق</Button>
            <Button variant="outline" size="sm" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages || loading}>التالي</Button>
          </div>
        </div>
      )}
    </div>
  );
}
