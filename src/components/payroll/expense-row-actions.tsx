"use client";

import { useState } from "react";
import { Printer, Download, XCircle, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog";
import { ApiError } from "@/lib/api-client";
import {
  ExpenseRecord, viewExpenseDoc, downloadExpenseDoc, cancelExpense,
} from "@/lib/api/expenses";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

/// <summary>Per-row actions for an expense: print/download the branded document, and cancel it (removing
/// it from payroll inclusion).</summary>
export function ExpenseRowActions({ expense, onChanged }: { expense: ExpenseRecord; onChanged: () => void }) {
  const [busy, setBusy] = useState<null | "view" | "download">(null);
  const [confirm, setConfirm] = useState(false);
  const [working, setWorking] = useState(false);

  const cancellable = ["Approved", "Pending"].includes(expense.status);

  async function doView() {
    setBusy("view");
    try { await viewExpenseDoc(expense.id); } catch (e) { notifyError(e, "تعذر فتح المستند"); } finally { setBusy(null); }
  }
  async function doDownload() {
    setBusy("download");
    try { await downloadExpenseDoc(expense.id); } catch (e) { notifyError(e, "تعذر تنزيل المستند"); } finally { setBusy(null); }
  }
  async function apply() {
    setWorking(true);
    try {
      await cancelExpense(expense.id);
      toast.success("تم إلغاء المصروف");
      setConfirm(false);
      onChanged();
    } catch (e) { notifyError(e, "تعذر إلغاء المصروف"); } finally { setWorking(false); }
  }

  return (
    <div className="flex items-center justify-end gap-1">
      <IconBtn title="طباعة" onClick={doView} disabled={busy !== null}>
        {busy === "view" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Printer className="h-4 w-4" />}
      </IconBtn>
      <IconBtn title="تنزيل" onClick={doDownload} disabled={busy !== null}>
        {busy === "download" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
      </IconBtn>
      {cancellable && (
        <IconBtn title="إلغاء" onClick={() => setConfirm(true)} className="text-destructive hover:text-destructive/80">
          <XCircle className="h-4 w-4" />
        </IconBtn>
      )}

      <Dialog open={confirm} onOpenChange={(o) => { if (!o && !working) setConfirm(false); }}>
        <DialogContent showCloseButton={false}>
          <DialogHeader>
            <DialogTitle>إلغاء المصروف</DialogTitle>
            <DialogDescription>
              سيتم إلغاء المصروف واستبعاده من الرواتب. لا يمكن التراجع.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirm(false)} disabled={working}>تراجع</Button>
            <Button onClick={apply} disabled={working} className="bg-destructive text-white hover:bg-destructive/90">
              {working ? "جاري…" : "إلغاء المصروف"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}

function IconBtn({
  children, title, onClick, disabled, className,
}: {
  children: React.ReactNode; title: string; onClick: () => void; disabled?: boolean; className?: string;
}) {
  return (
    <button
      type="button"
      title={title}
      onClick={onClick}
      disabled={disabled}
      className={`h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-foreground disabled:opacity-40 ${className ?? ""}`}
    >
      {children}
    </button>
  );
}
