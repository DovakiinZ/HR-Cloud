"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Search, Eye, Printer } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/permissions";
import { getRunEmployees, viewPayslip, money, type RunEmployeeRow, type Paged } from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

interface RunEmployeesTableProps {
  runId: string;
  currency: string;
}

const PAGE_SIZE = 20;

export function RunEmployeesTable({ runId, currency }: RunEmployeesTableProps) {
  const { has } = usePermissions();
  const canView = has("Payroll.Payslip.View");
  const canPrint = has("Payroll.Payslip.Print");
  const showPayslip = canView || canPrint;
  const cols = showPayslip ? 6 : 5;

  const [data, setData] = useState<Paged<RunEmployeeRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [page, setPage] = useState(1);
  const [busyId, setBusyId] = useState<string | null>(null);

  async function openPayslip(employeeId: string, print: boolean) {
    setBusyId(employeeId);
    try { await viewPayslip(runId, employeeId, print); }
    catch (err) { notifyError(err, "تعذر فتح قسيمة الراتب"); }
    finally { setBusyId(null); }
  }

  // Debounce search
  useEffect(() => {
    const t = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 350);
    return () => clearTimeout(t);
  }, [search]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getRunEmployees(runId, {
        page,
        pageSize: PAGE_SIZE,
        search: debouncedSearch || undefined,
      });
      setData(result);
    } catch (err) {
      notifyError(err, "تعذر تحميل بيانات الموظفين");
    } finally {
      setLoading(false);
    }
  }, [runId, page, debouncedSearch]);

  useEffect(() => { load(); }, [load]);

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1;

  return (
    <div className="space-y-3">
      {/* Search */}
      <div className="relative max-w-xs">
        <Search className="absolute right-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="بحث باسم الموظف أو رقمه…"
          className="pr-9 bg-secondary border-border"
        />
      </div>

      {/* Table */}
      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["الموظف", "الإجمالي", "الاستقطاعات", "الصافي", "حالة الترحيل", ...(showPayslip ? ["القسيمة"] : [])].map((h, i) => (
                <TableHead key={i} className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">
                  {h}
                </TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={cols} className="py-12 text-center text-sm text-muted-foreground">
                  <Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...
                </TableCell>
              </TableRow>
            ) : !data || data.items.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={cols} className="py-12 text-center text-sm text-muted-foreground">
                  لا توجد سجلات
                </TableCell>
              </TableRow>
            ) : data.items.map((row) => (
              <TableRow key={row.employeeId} className="border-border hover:bg-card/50">
                <TableCell>
                  <div className="font-medium">{row.employeeName}</div>
                  <div className="font-mono text-[10px] text-muted-foreground">{row.employeeNumber}</div>
                </TableCell>
                <TableCell className="text-sm tabular-nums">{money(row.gross, currency)}</TableCell>
                <TableCell className="text-sm tabular-nums text-destructive">{money(row.deductions, currency)}</TableCell>
                <TableCell className="text-sm tabular-nums font-bold">{money(row.net, currency)}</TableCell>
                <TableCell>
                  {row.ledgerPosted ? (
                    <span className="text-xs font-medium text-green-600">مُرحَّل</span>
                  ) : (
                    <span className="text-xs text-muted-foreground">غير مُرحَّل</span>
                  )}
                </TableCell>
                {showPayslip && (
                  <TableCell>
                    <div className="flex items-center gap-1">
                      {canView && (
                        <Button variant="ghost" size="sm" title="عرض القسيمة" disabled={busyId === row.employeeId}
                          onClick={() => openPayslip(row.employeeId, false)}>
                          <Eye className="h-4 w-4" />
                        </Button>
                      )}
                      {canPrint && (
                        <Button variant="ghost" size="sm" title="طباعة القسيمة" disabled={busyId === row.employeeId}
                          onClick={() => openPayslip(row.employeeId, true)}>
                          <Printer className="h-4 w-4" />
                        </Button>
                      )}
                    </div>
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {/* Pagination */}
      {data && data.total > PAGE_SIZE && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            {data.total.toLocaleString("ar-SA")} موظف — صفحة {page} من {totalPages}
          </span>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1 || loading}
            >
              السابق
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page >= totalPages || loading}
            >
              التالي
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
