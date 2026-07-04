"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Loader2, Download, FileDown, Landmark } from "lucide-react";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { usePermissions } from "@/lib/permissions";
import {
  getExportOptions, listExports, createExport, downloadExport, EXPORT_FORMATS,
  type ExportOption, type ExportJob,
} from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

const selectClass =
  "h-9 rounded-md border border-border bg-background px-3 text-sm focus:outline-none focus:ring-1 focus:ring-primary";

export function RunExportsPanel({ runId }: { runId: string }) {
  const { has } = usePermissions();
  const canBank = has("Payroll.Export.Bank");

  const [options, setOptions] = useState<ExportOption[]>([]);
  const [jobs, setJobs] = useState<ExportJob[]>([]);
  const [loading, setLoading] = useState(true);
  const [kind, setKind] = useState("");
  const [format, setFormat] = useState<string>("Excel");
  const [busy, setBusy] = useState(false);
  const [downloading, setDownloading] = useState<string | null>(null);

  const visibleOptions = useMemo(() => options.filter((o) => !o.isBank || canBank), [options, canBank]);
  const selected = visibleOptions.find((o) => o.kind === kind);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [opts, list] = await Promise.all([getExportOptions(runId), listExports(runId)]);
      setOptions(opts);
      setJobs(list);
      setKind((k) => k || opts.find((o) => !o.isBank || canBank)?.kind || "");
    } catch (err) {
      notifyError(err, "تعذر تحميل خيارات التصدير");
    } finally {
      setLoading(false);
    }
  }, [runId, canBank]);

  useEffect(() => { load(); }, [load]);

  async function handleCreate() {
    if (!kind) return;
    setBusy(true);
    try {
      const fmt = selected?.isBank ? selected.format : format;
      await createExport(runId, kind, fmt);
      toast.success("تم إنشاء ملف التصدير");
      await load();
    } catch (err) {
      notifyError(err, "تعذر إنشاء التصدير");
    } finally {
      setBusy(false);
    }
  }

  async function handleDownload(job: ExportJob) {
    setDownloading(job.jobId);
    try {
      await downloadExport(job.jobId, job.fileName ?? undefined);
    } catch (err) {
      notifyError(err, "تعذر تحميل الملف");
    } finally {
      setDownloading(null);
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-3 rounded-lg border border-border p-4">
        <div className="flex flex-col gap-1">
          <label className="text-xs text-muted-foreground">نوع التصدير</label>
          <select className={selectClass} value={kind} onChange={(e) => setKind(e.target.value)} disabled={loading || busy}>
            {visibleOptions.map((o) => (
              <option key={o.kind} value={o.kind}>{o.isBank ? `🏦 ${o.title}` : o.title}</option>
            ))}
          </select>
        </div>

        <div className="flex flex-col gap-1">
          <label className="text-xs text-muted-foreground">الصيغة</label>
          <select
            className={selectClass}
            value={selected?.isBank ? selected.format : format}
            onChange={(e) => setFormat(e.target.value)}
            disabled={loading || busy || !!selected?.isBank}
          >
            {(selected?.isBank ? [selected.format] : EXPORT_FORMATS).map((f) => (
              <option key={f} value={f}>{f}</option>
            ))}
          </select>
        </div>

        <Button onClick={handleCreate} disabled={loading || busy || !kind}>
          {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <FileDown className="h-4 w-4" />}
          إنشاء الملف
        </Button>

        {selected?.isBank && (
          <span className="flex items-center gap-1 text-xs text-amber-600">
            <Landmark className="h-3 w-3" /> ملف بنكي — يتضمن بيانات الآيبان
          </span>
        )}
      </div>

      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["النوع", "الصيغة", "عدد السجلات", "الحالة", "التاريخ", ""].map((h, i) => (
                <TableHead key={i} className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">{h}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...</TableCell></TableRow>
            ) : jobs.length === 0 ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground">لا توجد ملفات تصدير بعد</TableCell></TableRow>
            ) : jobs.map((j) => (
              <TableRow key={j.jobId} className="border-border hover:bg-card/50">
                <TableCell className="font-medium">{j.isBank && <Landmark className="h-3 w-3 inline text-amber-600 me-1" />}{j.kind}</TableCell>
                <TableCell className="text-sm">{j.format}</TableCell>
                <TableCell className="text-sm tabular-nums">{j.rowCount.toLocaleString("ar-SA")}</TableCell>
                <TableCell>
                  <Badge variant="outline" className={j.status === "Completed" ? "text-xs bg-emerald-500/10 text-emerald-700 border-emerald-500/20" : "text-xs text-muted-foreground"}>{j.status}</Badge>
                </TableCell>
                <TableCell className="text-xs text-muted-foreground">{new Date(j.createdAt).toLocaleString("ar-SA")}</TableCell>
                <TableCell>
                  {j.artifactFileId && (
                    <Button variant="ghost" size="sm" title="تحميل" disabled={downloading === j.jobId} onClick={() => handleDownload(j)}>
                      <Download className="h-4 w-4" />
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}
