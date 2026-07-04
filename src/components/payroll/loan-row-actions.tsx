"use client";

import { useState } from "react";
import { Printer, Download, CheckCircle2, XCircle, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter,
} from "@/components/ui/dialog";
import { ApiError } from "@/lib/api-client";
import {
  LoanRecord, viewLoanDoc, downloadLoanDoc, cancelLoan, settleLoan,
} from "@/lib/api/loans";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

/// <summary>Per-row actions for a loan/advance: print/download the branded document, and (while active)
/// settle (تسوية) or cancel it.</summary>
export function LoanRowActions({ loan, onChanged }: { loan: LoanRecord; onChanged: () => void }) {
  const [busy, setBusy] = useState<null | "view" | "download">(null);
  const [confirm, setConfirm] = useState<null | "cancel" | "settle">(null);
  const [working, setWorking] = useState(false);

  const isActive = loan.status === "Active";

  async function doView() {
    setBusy("view");
    try { await viewLoanDoc(loan.id); } catch (e) { notifyError(e, "تعذر فتح المستند"); } finally { setBusy(null); }
  }
  async function doDownload() {
    setBusy("download");
    try { await downloadLoanDoc(loan.id); } catch (e) { notifyError(e, "تعذر تنزيل المستند"); } finally { setBusy(null); }
  }
  async function apply() {
    if (!confirm) return;
    setWorking(true);
    try {
      if (confirm === "cancel") await cancelLoan(loan.id);
      else await settleLoan(loan.id);
      toast.success(confirm === "cancel" ? "تم إلغاء القرض" : "تمت تسوية القرض");
      setConfirm(null);
      onChanged();
    } catch (e) { notifyError(e, "تعذر تنفيذ العملية"); } finally { setWorking(false); }
  }

  return (
    <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
      <IconBtn title="طباعة" onClick={doView} disabled={busy !== null}>
        {busy === "view" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Printer className="h-4 w-4" />}
      </IconBtn>
      <IconBtn title="تنزيل" onClick={doDownload} disabled={busy !== null}>
        {busy === "download" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Download className="h-4 w-4" />}
      </IconBtn>
      {isActive && (
        <>
          <IconBtn title="تسوية" onClick={() => setConfirm("settle")} className="text-green-500 hover:text-green-400">
            <CheckCircle2 className="h-4 w-4" />
          </IconBtn>
          <IconBtn title="إلغاء" onClick={() => setConfirm("cancel")} className="text-destructive hover:text-destructive/80">
            <XCircle className="h-4 w-4" />
          </IconBtn>
        </>
      )}

      <Dialog open={!!confirm} onOpenChange={(o) => { if (!o && !working) setConfirm(null); }}>
        <DialogContent showCloseButton={false}>
          <DialogHeader>
            <DialogTitle>{confirm === "cancel" ? "إلغاء القرض" : "تسوية القرض"}</DialogTitle>
            <DialogDescription>
              {confirm === "cancel"
                ? "سيتم إلغاء القرض وإيقاف خصم الأقساط المتبقية من الراتب. لا يمكن التراجع."
                : "سيتم اعتبار القرض مُسدداً بالكامل، وتعليم جميع الأقساط المتبقية كمسددة وإيقاف الخصم. لا يمكن التراجع."}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirm(null)} disabled={working}>تراجع</Button>
            <Button
              onClick={apply}
              disabled={working}
              className={confirm === "cancel" ? "bg-destructive text-white hover:bg-destructive/90" : "font-bold"}
            >
              {working ? "جاري…" : confirm === "cancel" ? "إلغاء القرض" : "تسوية"}
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
