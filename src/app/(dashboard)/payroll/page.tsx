"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Banknote, Plus, Loader2, Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Combobox } from "@/components/ui/combobox";
import { ApiError } from "@/lib/api-client";
import { AccessGuard } from "@/components/access/access-guard";
import { usePermissions } from "@/lib/permissions";
import { StateBadge } from "@/components/payroll/state-badge";
import {
  bootstrapPayroll, getDefinitions, listRuns, createRun, money,
  type PayrollDefinitionDto, type PayrollRunListItem,
} from "@/lib/api/payroll";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

export default function PayrollPage() {
  return <AccessGuard anyOf={["Payroll.View"]}><Inner /></AccessGuard>;
}

function Inner() {
  const router = useRouter();
  const { has } = usePermissions();
  const canRun = has("Payroll.Run");

  const [defs, setDefs] = useState<PayrollDefinitionDto[]>([]);
  const [runs, setRuns] = useState<PayrollRunListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [createOpen, setCreateOpen] = useState(false);

  const load = useCallback(async () => {
    const [d, r] = await Promise.all([getDefinitions(), listRuns()]);
    setDefs(d); setRuns(r);
  }, []);

  useEffect(() => {
    (async () => {
      setLoading(true);
      try { await load(); } catch (err) { notifyError(err, "تعذر تحميل الرواتب"); } finally { setLoading(false); }
    })();
  }, [load]);

  async function doBootstrap() {
    setBusy(true);
    try { await bootstrapPayroll(); toast.success("تم تجهيز مسيّر الرواتب الشهري"); await load(); }
    catch (err) { notifyError(err, "تعذر التجهيز"); } finally { setBusy(false); }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">الرواتب</h1>
          <p className="text-sm text-muted-foreground mt-1">مسيّرات الرواتب — معاينة، تحقق، اعتماد، وتنفيذ</p>
        </div>
        {canRun && defs.length > 0 && (
          <Button onClick={() => setCreateOpen(true)} className="h-10 gap-2 font-bold text-sm"><Plus className="h-4 w-4" /> مسيّر جديد</Button>
        )}
      </div>

      <div className="flex items-center gap-2 flex-wrap">
        <Link href="/payroll/additions" className="text-sm border border-border px-3 py-1.5 hover:bg-card/50 transition-colors">الإضافات</Link>
        <Link href="/payroll/deductions" className="text-sm border border-border px-3 py-1.5 hover:bg-card/50 transition-colors">الاستقطاعات</Link>
        <Link href="/loans" className="text-sm border border-border px-3 py-1.5 hover:bg-card/50 transition-colors">القروض والسلف</Link>
        <Link href="/expenses" className="text-sm border border-border px-3 py-1.5 hover:bg-card/50 transition-colors">المصروفات</Link>
      </div>

      {loading ? (
        <div className="py-16 text-center text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin inline" /> جاري التحميل…</div>
      ) : defs.length === 0 ? (
        <div className="border border-dashed border-border bg-card p-12 flex flex-col items-center text-center">
          <Banknote className="h-12 w-12 text-muted-foreground mb-4" />
          <h2 className="text-lg font-semibold mb-1">لا يوجد مسيّر رواتب بعد</h2>
          <p className="text-sm text-muted-foreground mb-4">جهّز مسيّر الرواتب الشهري القياسي (أساسي + بدلات − تأمينات − استقطاعات) للبدء.</p>
          {canRun && <Button onClick={doBootstrap} disabled={busy} className="gap-2"><Sparkles className="h-4 w-4" /> تجهيز المسيّر الشهري القياسي</Button>}
        </div>
      ) : (
        <div className="border border-border">
          <Table>
            <TableHeader>
              <TableRow className="border-border hover:bg-transparent">
                {["المسيّر", "الفترة", "الحالة", "الموظفون", "الإجمالي", "الصافي"].map((h, i) => (
                  <TableHead key={i} className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">{h}</TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {runs.length === 0 ? (
                <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground">لا توجد مسيّرات — أنشئ مسيّراً جديداً</TableCell></TableRow>
              ) : runs.map((r) => (
                <TableRow key={r.id} className="border-border hover:bg-card/50 cursor-pointer" onClick={() => router.push(`/payroll/runs/${r.id}`)}>
                  <TableCell className="font-medium" dir="ltr">{r.runNumber}</TableCell>
                  <TableCell className="text-sm text-muted-foreground" dir="ltr">{r.periodStart.slice(0, 7)}</TableCell>
                  <TableCell><StateBadge state={r.state} /></TableCell>
                  <TableCell className="text-sm text-muted-foreground">{r.employeeCount}</TableCell>
                  <TableCell className="text-sm tabular-nums">{money(r.grossTotal, r.currency)}</TableCell>
                  <TableCell className="text-sm tabular-nums font-medium">{money(r.netTotal, r.currency)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {createOpen && <CreateRunDialog defs={defs} onClose={() => setCreateOpen(false)} onCreated={(id) => router.push(`/payroll/runs/${id}`)} />}
    </div>
  );
}

function CreateRunDialog({ defs, onClose, onCreated }: { defs: PayrollDefinitionDto[]; onClose: () => void; onCreated: (id: string) => void; }) {
  const now = new Date();
  const [definitionId, setDefinitionId] = useState<string | null>(defs[0]?.id ?? null);
  const [year, setYear] = useState(now.getFullYear());
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [saving, setSaving] = useState(false);

  async function save() {
    if (!definitionId) { toast.error("اختر مسيّراً"); return; }
    setSaving(true);
    try { const r = await createRun(definitionId, year, month); toast.success("تم إنشاء المسيّر"); onCreated(r.id); }
    catch (err) { notifyError(err, "تعذر إنشاء المسيّر"); } finally { setSaving(false); }
  }

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !saving) onClose(); }}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader><DialogTitle>مسيّر رواتب جديد</DialogTitle></DialogHeader>
        <div className="space-y-4 py-2">
          <div className="space-y-2"><Label className="text-xs font-bold uppercase tracking-wider">المسيّر</Label>
            <Combobox value={definitionId} onChange={setDefinitionId}
              options={defs.map((d) => ({ value: d.id, label: d.nameAr || d.name }))} placeholder="اختر مسيّراً…" /></div>
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-2"><Label className="text-xs font-bold uppercase tracking-wider">السنة</Label>
              <Input type="number" value={year} onChange={(e) => setYear(+e.target.value)} dir="ltr" className="bg-secondary border-border" /></div>
            <div className="space-y-2"><Label className="text-xs font-bold uppercase tracking-wider">الشهر</Label>
              <Input type="number" min={1} max={12} value={month} onChange={(e) => setMonth(+e.target.value)} dir="ltr" className="bg-secondary border-border" /></div>
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>إلغاء</Button>
          <Button onClick={save} disabled={saving} className="font-bold">{saving ? "جاري الإنشاء…" : "إنشاء"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
