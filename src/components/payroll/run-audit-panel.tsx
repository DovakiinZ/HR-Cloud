"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, User } from "lucide-react";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { getRunAudit, AUDIT_ACTION_AR, type PayrollAuditRow, type Paged } from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

function actionLabel(a: string): string {
  return AUDIT_ACTION_AR[a] ?? a;
}

/** Compact one-line summary of a JSON before/after blob. */
function summarize(json?: string | null): string {
  if (!json) return "";
  try {
    const o = JSON.parse(json);
    return Object.entries(o).map(([k, v]) => `${k}: ${v}`).join(" · ");
  } catch { return json; }
}

const PAGE_SIZE = 25;

export function RunAuditPanel({ runId }: { runId: string }) {
  const [data, setData] = useState<Paged<PayrollAuditRow> | null>(null);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    try { setData(await getRunAudit(runId, { page, pageSize: PAGE_SIZE })); }
    catch (err) { notifyError(err, "تعذر تحميل سجل التدقيق"); }
    finally { setLoading(false); }
  }, [runId, page]);

  useEffect(() => { load(); }, [load]);

  const totalPages = data ? Math.ceil(data.total / PAGE_SIZE) : 1;

  return (
    <div className="space-y-3">
      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["الوقت", "الإجراء", "المستخدم", "التفاصيل"].map((h, i) => (
                <TableHead key={i} className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">{h}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={4} className="py-12 text-center text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...</TableCell></TableRow>
            ) : !data || data.items.length === 0 ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={4} className="py-12 text-center text-sm text-muted-foreground">لا يوجد سجل تدقيق لهذا المسيّر</TableCell></TableRow>
            ) : data.items.map((r, i) => (
              <TableRow key={i} className="border-border hover:bg-card/50 align-top">
                <TableCell className="text-xs text-muted-foreground whitespace-nowrap">{new Date(r.timestamp).toLocaleString("ar-SA")}</TableCell>
                <TableCell><Badge variant="outline" className="text-xs">{actionLabel(r.action)}</Badge></TableCell>
                <TableCell className="text-sm">
                  <span className="inline-flex items-center gap-1">
                    <User className="h-3 w-3 text-muted-foreground" />
                    {r.actorName ?? "—"}
                  </span>
                </TableCell>
                <TableCell className="text-xs text-muted-foreground max-w-md truncate" title={summarize(r.newValues)}>
                  {summarize(r.newValues) || "—"}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {data && data.total > PAGE_SIZE && (
        <div className="flex items-center justify-between text-sm text-muted-foreground">
          <span>{data.total.toLocaleString("ar-SA")} حدث — صفحة {page} من {totalPages}</span>
          <div className="flex gap-2">
            <Button variant="outline" size="sm" onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1 || loading}>السابق</Button>
            <Button variant="outline" size="sm" onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages || loading}>التالي</Button>
          </div>
        </div>
      )}
    </div>
  );
}
