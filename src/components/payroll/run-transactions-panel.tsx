"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Check } from "lucide-react";
import { toast } from "sonner";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/permissions";
import { getRunTransactions, calculateRun, money, type RunTransactionRow, type TxnBucket, type Paged } from "@/lib/api/payroll";
import { approveTransaction } from "@/lib/api/payroll-transactions";
import { QuickAddTransactionDialog } from "./quick-add-transaction-dialog";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

const BUCKET_LABEL: Record<TxnBucket, string> = {
  PendingApproval: "بانتظار الاعتماد",
  ApprovedNotConsumed: "معتمد — غير مستهلك",
  Consumed: "مستهلك",
  Posted: "مُرحَّل",
  Reversed: "معكوس",
  Other: "أخرى",
};

const BUCKET_STYLE: Record<TxnBucket, string> = {
  PendingApproval: "bg-amber-500/10 text-amber-700 border-amber-500/20",
  ApprovedNotConsumed: "bg-blue-500/10 text-blue-600 border-blue-500/20",
  Consumed: "bg-green-500/10 text-green-600 border-green-500/20",
  Posted: "bg-indigo-500/10 text-indigo-600 border-indigo-500/20",
  Reversed: "bg-zinc-500/10 text-zinc-500 border-zinc-500/20",
  Other: "bg-zinc-400/10 text-zinc-500 border-zinc-400/20",
};

function BucketBadge({ bucket }: { bucket: TxnBucket }) {
  const label = BUCKET_LABEL[bucket] ?? bucket;
  const style = BUCKET_STYLE[bucket] ?? "bg-zinc-400/10 text-zinc-500 border-zinc-400/20";
  return (
    <Badge variant="outline" className={`text-xs ${style}`}>
      {label}
    </Badge>
  );
}

function KindBadge({ kind }: { kind: 1 | 2 }) {
  if (kind === 1) return <Badge variant="outline" className="text-xs bg-green-500/10 text-green-600 border-green-500/20">إضافة</Badge>;
  return <Badge variant="outline" className="text-xs bg-destructive/10 text-destructive border-destructive/20">استقطاع</Badge>;
}

interface RunTransactionsPanelProps {
  runId: string;
  currency: string;
  /** Whether the run is in an immutable state (Approved/Executing/Completed/Locked/Archived) */
  immutable: boolean;
}

const PAGE_SIZE = 25;

export function RunTransactionsPanel({ runId, currency, immutable }: RunTransactionsPanelProps) {
  const { has } = usePermissions();
  const canApprove = has("Payroll.Approve");
  const canCreate = has("Payroll.Transaction.CreateFromRun");

  const [data, setData] = useState<Paged<RunTransactionRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await getRunTransactions(runId, { page, pageSize: PAGE_SIZE });
      setData(result);
    } catch (err) {
      notifyError(err, "تعذر تحميل الحركات");
    } finally {
      setLoading(false);
    }
  }, [runId, page]);

  useEffect(() => { load(); }, [load]);

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1;

  async function handleApproveAndRecalculate(txnId: string) {
    setBusyId(txnId);
    try {
      await approveTransaction(txnId);
      await calculateRun(runId);
      toast.success("تم الاعتماد وإعادة الاحتساب");
      await load();
    } catch (err) {
      notifyError(err, "تعذر الاعتماد أو الاحتساب");
    } finally {
      setBusyId(null);
    }
  }

  return (
    <div className="space-y-3">
      {/* Quick-add trigger: hidden when immutable OR permission not held */}
      {!immutable && canCreate && (
        <div className="flex justify-end">
          <QuickAddTransactionDialog runId={runId} onSuccess={load} />
        </div>
      )}

      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["الموظف", "النوع", "الفئة", "المبلغ", "السلة", ""].map((h, i) => (
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
                  لا توجد حركات
                </TableCell>
              </TableRow>
            ) : data.items.map((row) => (
              <TableRow key={row.id} className="border-border hover:bg-card/50">
                <TableCell>
                  <div className="font-medium">{row.employeeName}</div>
                  <div className="font-mono text-[10px] text-muted-foreground">{row.employeeNumber}</div>
                </TableCell>
                <TableCell className="text-sm">{row.typeName}</TableCell>
                <TableCell><KindBadge kind={row.kind} /></TableCell>
                <TableCell className="text-sm tabular-nums">{money(row.amount, currency)}</TableCell>
                <TableCell><BucketBadge bucket={row.bucket} /></TableCell>
                <TableCell>
                  {/* Approve & Recalculate — only on PendingApproval rows when user has Payroll.Approve */}
                  {row.bucket === "PendingApproval" && canApprove && (
                    <Button
                      size="sm"
                      variant="outline"
                      disabled={busyId === row.id}
                      onClick={() => handleApproveAndRecalculate(row.id)}
                      className="h-7 text-xs gap-1 font-bold"
                    >
                      {busyId === row.id
                        ? <Loader2 className="h-3 w-3 animate-spin" />
                        : <Check className="h-3 w-3" />}
                      اعتماد وإعادة احتساب
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {data && data.total > PAGE_SIZE && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>
            {data.total.toLocaleString("ar-SA")} حركة — صفحة {page} من {totalPages}
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
