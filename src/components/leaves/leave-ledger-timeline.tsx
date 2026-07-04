"use client";

import { useMemo, useState } from "react";
import { ChevronDown } from "lucide-react";
import type { LeaveLedgerView } from "@/lib/api/leave-ledger";
import { LEDGER_TYPE_AR } from "@/lib/api/leave-ledger";

/**
 * Vertical editorial timeline of the leave accrual ledger, with pagination and an expandable
 * "Calculation Details" section per entry so HR can immediately understand why a balance changed.
 */

type TimelineItem =
  | { kind: "entry"; date: string; type: string; amount: number; running: number; reason?: string | null; unpaid: boolean }
  | { kind: "gap"; date: string; end: string; days: number };

const PAGE_SIZES = [20, 50, 100];

function fmt(n: number, max = 2): string {
  return n.toLocaleString("ar-SA", { minimumFractionDigits: 0, maximumFractionDigits: max });
}
function fmtSigned(n: number): string {
  const s = n.toLocaleString("ar-SA", { minimumFractionDigits: 0, maximumFractionDigits: 3 });
  return n > 0 ? `+${s}` : s;
}
function fmtDate(d: string): string {
  return new Date(d).toLocaleDateString("ar-SA", { year: "numeric", month: "short", day: "numeric" });
}

/** A human-readable Arabic explanation of why this entry changed the balance. */
function explain(it: Extract<TimelineItem, { kind: "entry" }>): string {
  const days = fmt(Math.abs(it.amount), 3);
  const base = (() => {
    switch (it.type) {
      case "Accrual": return `تم إضافة ${days} يوم كاستحقاق للإجازة عن هذه الفترة`;
      case "Usage": return `تم خصم ${days} يوم مقابل استخدام إجازة`;
      case "Adjustment": return `تسوية يدوية على الرصيد بمقدار ${fmtSigned(it.amount)} يوم`;
      case "Forfeiture": return `تم إسقاط ${days} يوم من الرصيد`;
      case "Restoration": return `تمت استعادة ${days} يوم إلى الرصيد`;
      default: return `تغيّر الرصيد بمقدار ${fmtSigned(it.amount)} يوم`;
    }
  })();
  const unpaid = it.unpaid ? " (خلال فترة إجازة بدون راتب — يتوقف الاستحقاق فيها)" : "";
  const why = it.reason ? ` — ${it.reason}` : "";
  return `${base}${why}${unpaid}. الرصيد بعد الحركة: ${fmt(it.running)} يوم.`;
}

export function LeaveLedgerTimeline({ ledger }: { ledger: LeaveLedgerView }) {
  const [pageSize, setPageSize] = useState(20);
  const [page, setPage] = useState(1);
  const [openIdx, setOpenIdx] = useState<number | null>(null);

  const items = useMemo<TimelineItem[]>(() => {
    const list: TimelineItem[] = ledger.entries.map((e) => ({
      kind: "entry" as const, date: e.date, type: e.type, amount: e.amount,
      running: e.runningBalance, reason: e.reason, unpaid: e.isUnpaidPeriod,
    }));
    for (const g of ledger.unpaidPeriods) list.push({ kind: "gap", date: g.start, end: g.end, days: g.days });
    // Newest first (most useful for HR); keep chronological running balance intact.
    return list.sort((a, b) => +new Date(b.date) - +new Date(a.date));
  }, [ledger]);

  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const pageItems = items.slice((page - 1) * pageSize, page * pageSize);

  if (items.length === 0) {
    return (
      <div className="border border-dashed border-border bg-card px-6 py-16 text-center text-sm text-muted-foreground">
        لا توجد حركات في سجل الإجازة بعد. أعد الاحتساب لبناء دفتر الاستحقاق.
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between text-xs text-muted-foreground">
        <span>{fmt(items.length, 0)} حركة</span>
        <div className="flex items-center gap-2">
          <span>لكل صفحة:</span>
          <select value={pageSize} onChange={(e) => { setPageSize(Number(e.target.value)); setPage(1); }}
            className="h-8 rounded-md border border-border bg-background px-2">
            {PAGE_SIZES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
      </div>

      <ol className="relative">
        {pageItems.map((it, i) => {
          const globalIdx = (page - 1) * pageSize + i;
          const last = i === pageItems.length - 1;
          const isEntry = it.kind === "entry";
          const open = openIdx === globalIdx;
          return (
            <li key={globalIdx} className="relative flex gap-4 pb-6">
              <div className="relative flex w-4 shrink-0 flex-col items-center">
                <Dot item={it} />
                {!last && <Stem item={it} />}
              </div>

              <div className="-mt-0.5 flex-1">
                {!isEntry ? (
                  <div className="text-sm">
                    <div className="font-medium text-muted-foreground">إجازة بدون راتب — يتوقف الاستحقاق</div>
                    <div className="mt-0.5 text-xs text-muted-foreground/80 tabular-nums">{fmtDate(it.date)} ← {fmtDate((it as { end: string }).end)} · {fmt((it as { days: number }).days)} يوم</div>
                  </div>
                ) : (
                  <div>
                    <button onClick={() => setOpenIdx(open ? null : globalIdx)} className="group flex w-full items-start justify-between gap-3 text-sm text-right">
                      <div>
                        <div className="font-medium text-foreground flex items-center gap-1">
                          {LEDGER_TYPE_AR[it.type] ?? it.type}
                          <span className={`ms-2 tabular-nums ${it.amount >= 0 ? "text-primary" : "text-destructive"}`}>{fmtSigned(it.amount)}</span>
                          <ChevronDown className={`h-3.5 w-3.5 text-muted-foreground transition-transform ${open ? "rotate-180" : ""}`} />
                        </div>
                        <div className="mt-0.5 text-xs text-muted-foreground tabular-nums">{fmtDate(it.date)}{it.reason ? <span className="mx-1">·</span> : null}{it.reason}</div>
                      </div>
                      <div className="shrink-0 text-end">
                        <div className="font-heading text-base tabular-nums text-foreground">{fmt(it.running)}</div>
                        <div className="text-[10px] text-muted-foreground">الرصيد</div>
                      </div>
                    </button>

                    {open && (
                      <div className="mt-2 rounded-md border border-border bg-secondary/30 p-3 text-xs">
                        <div className="mb-2 font-medium text-foreground">تفاصيل الاحتساب</div>
                        <div className="grid grid-cols-2 gap-x-4 gap-y-1 sm:grid-cols-3">
                          <Detail k="الرصيد السابق" v={fmt(it.running - it.amount)} />
                          <Detail k="الاستحقاق" v={it.type === "Accrual" ? fmt(it.amount) : "—"} />
                          <Detail k="المُستخدم" v={it.type === "Usage" ? fmt(Math.abs(it.amount)) : "—"} />
                          <Detail k="التسوية اليدوية" v={it.type === "Adjustment" ? fmtSigned(it.amount) : "—"} />
                          <Detail k="أثر الإجازة بدون راتب" v={it.unpaid ? "نعم" : "لا"} />
                          <Detail k="القاعدة / السبب" v={it.reason || "—"} />
                          <Detail k="الرصيد النهائي" v={fmt(it.running)} accent />
                        </div>
                        <p className="mt-3 leading-relaxed text-muted-foreground">{explain(it)}</p>
                      </div>
                    )}
                  </div>
                )}
              </div>
            </li>
          );
        })}
      </ol>

      {totalPages > 1 && (
        <div className="flex items-center justify-between text-xs text-muted-foreground">
          <span>صفحة {page} من {totalPages}</span>
          <div className="flex gap-2">
            <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page === 1} className="rounded-md border border-border px-3 py-1 disabled:opacity-40">السابق</button>
            <button onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages} className="rounded-md border border-border px-3 py-1 disabled:opacity-40">التالي</button>
          </div>
        </div>
      )}
    </div>
  );
}

function Detail({ k, v, accent }: { k: string; v: string; accent?: boolean }) {
  return (
    <div>
      <div className="text-[10px] text-muted-foreground">{k}</div>
      <div className={`tabular-nums ${accent ? "font-bold text-primary" : "text-foreground"}`}>{v}</div>
    </div>
  );
}

function Dot({ item }: { item: TimelineItem }) {
  if (item.kind === "gap") return <span className="z-10 mt-1 h-2.5 w-2.5 rounded-full border-2 border-dashed border-muted-foreground/50 bg-background" />;
  const accrual = item.type === "Accrual";
  const positive = item.amount >= 0;
  return <span className={`z-10 mt-1 h-2.5 w-2.5 rounded-full ${accrual ? "bg-primary" : positive ? "bg-primary/50" : "border border-destructive/60 bg-background"}`} />;
}

function Stem({ item }: { item: TimelineItem }) {
  const dotted = item.kind === "gap" || (item.kind === "entry" && item.unpaid);
  return <span className={`mt-1 w-px flex-1 ${dotted ? "border-s border-dashed border-muted-foreground/40" : "bg-primary/30"}`} />;
}
