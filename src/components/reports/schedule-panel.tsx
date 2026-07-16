"use client";

import { useCallback, useEffect, useState } from "react";
import { Trash2 } from "lucide-react";
import { toast } from "sonner";
import { getSchedules, addSchedule, deleteSchedule, ReportSchedule } from "@/lib/api/reports";

const FREQ = [{ v: 1, l: "يومي" }, { v: 2, l: "أسبوعي" }, { v: 3, l: "شهري" }, { v: 4, l: "ربع سنوي" }];
// FMT values match HR.Domain.Enums.ExportFormat: Pdf=1, Xlsx=2, Csv=3
const FMT = [{ v: 2, l: "Excel" }, { v: 3, l: "CSV" }, { v: 1, l: "PDF" }];

export function SchedulePanel({ reportId }: { reportId: string }) {
  const [items, setItems] = useState<ReportSchedule[]>([]);
  const [freq, setFreq] = useState(1); const [fmt, setFmt] = useState(2); const [emails, setEmails] = useState("");

  const load = useCallback(async () => {
    try { setItems(await getSchedules(reportId)); } catch { /* ignore */ }
  }, [reportId]);
  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  const add = async () => {
    const list = emails.split(",").map((e) => e.trim()).filter(Boolean);
    if (list.length === 0) { toast.error("أدخل بريدًا واحدًا على الأقل"); return; }
    try {
      await addSchedule(reportId, { frequency: freq, exportFormat: fmt, recipients: JSON.stringify(list) });
      setEmails(""); await load(); toast.success("تمت إضافة الجدولة");
    } catch { toast.error("تعذّر إضافة الجدولة"); }
  };

  return (
    <div className="border border-border bg-card p-4 space-y-3" dir="rtl">
      <h3 className="font-semibold">الجدولة والتسليم</h3>
      <ul className="space-y-1 text-sm">
        {items.map((s) => (
          <li key={s.id} className="flex items-center justify-between">
            <span>
              {FREQ.find((f) => String(f.v) === s.frequency || f.l === s.frequency)?.l ?? s.frequency}
              {" · "}
              {FMT.find((f) => String(f.v) === s.exportFormat || f.l === s.exportFormat)?.l ?? s.exportFormat}
              {" · "}
              {s.nextRunAt ? new Date(s.nextRunAt).toLocaleDateString("ar-SA") : "—"}
            </span>
            <button
              className="text-destructive"
              onClick={async () => { await deleteSchedule(s.id); await load(); }}
              title="حذف الجدولة"
            >
              <Trash2 className="h-4 w-4" />
            </button>
          </li>
        ))}
        {items.length === 0 && <li className="text-muted-foreground">لا توجد جدولة.</li>}
      </ul>
      <div className="flex flex-wrap items-center gap-2">
        <select
          value={freq}
          onChange={(e) => setFreq(Number(e.target.value))}
          className="h-9 border border-border bg-background px-2 text-sm"
        >
          {FREQ.map((f) => <option key={f.v} value={f.v}>{f.l}</option>)}
        </select>
        <select
          value={fmt}
          onChange={(e) => setFmt(Number(e.target.value))}
          className="h-9 border border-border bg-background px-2 text-sm"
        >
          {FMT.map((f) => <option key={f.v} value={f.v}>{f.l}</option>)}
        </select>
        <input
          value={emails}
          onChange={(e) => setEmails(e.target.value)}
          placeholder="بريد1، بريد2"
          className="h-9 flex-1 border border-border bg-background px-3 text-sm"
        />
        <button
          onClick={add}
          className="inline-flex h-9 items-center bg-primary px-4 text-sm text-primary-foreground"
        >
          إضافة
        </button>
      </div>
    </div>
  );
}
