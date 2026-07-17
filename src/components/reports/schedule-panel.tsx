"use client";

import { useCallback, useEffect, useState } from "react";
import { Trash2 } from "lucide-react";
import { toast } from "sonner";
import { getSchedules, addSchedule, deleteSchedule, ReportSchedule } from "@/lib/api/reports";

const FREQ = [{ v: 1, l: "يومي" }, { v: 2, l: "أسبوعي" }, { v: 3, l: "شهري" }, { v: 4, l: "ربع سنوي" }];
// FMT values match HR.Domain.Enums.ExportFormat: Pdf=1, Xlsx=2, Csv=3
const FMT = [{ v: 2, l: "Excel" }, { v: 3, l: "CSV" }, { v: 1, l: "PDF" }];
const DOW = [{ v: 0, l: "الأحد" }, { v: 1, l: "الاثنين" }, { v: 2, l: "الثلاثاء" }, { v: 3, l: "الأربعاء" }, { v: 4, l: "الخميس" }, { v: 5, l: "الجمعة" }, { v: 6, l: "السبت" }];

export function SchedulePanel({ reportId }: { reportId: string }) {
  const [items, setItems] = useState<ReportSchedule[]>([]);
  const [freq, setFreq] = useState(1); const [fmt, setFmt] = useState(2); const [emails, setEmails] = useState("");
  const [time, setTime] = useState("08:00");        // HH:mm, Riyadh
  const [dow, setDow] = useState(0);                // 0=Sunday
  const [dom, setDom] = useState(1);                // 1..28

  const load = useCallback(async () => {
    try { setItems(await getSchedules(reportId)); } catch { /* ignore */ }
  }, [reportId]);
  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  const add = async () => {
    const list = emails.split(",").map((e) => e.trim()).filter(Boolean);
    if (list.length === 0) { toast.error("أدخل بريدًا واحدًا على الأقل"); return; }
    try {
      const [hh, mm] = time.split(":").map(Number);
      const timeOfDayMinutes = (hh || 0) * 60 + (mm || 0);
      await addSchedule(reportId, {
        frequency: freq, exportFormat: fmt, recipients: JSON.stringify(list),
        timeOfDayMinutes,
        dayOfWeek: freq === 2 ? dow : null,
        dayOfMonth: freq === 3 || freq === 4 ? Math.min(Math.max(dom, 1), 28) : null,
      });
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
          type="time"
          value={time}
          onChange={(e) => setTime(e.target.value)}
          className="h-9 border border-border bg-background px-2 text-sm"
          title="وقت الإرسال (توقيت الرياض)"
        />
        {freq === 2 && (
          <select value={dow} onChange={(e) => setDow(Number(e.target.value))}
            className="h-9 border border-border bg-background px-2 text-sm">
            {DOW.map((d) => <option key={d.v} value={d.v}>{d.l}</option>)}
          </select>
        )}
        {(freq === 3 || freq === 4) && (
          <select value={dom} onChange={(e) => setDom(Number(e.target.value))}
            className="h-9 border border-border bg-background px-2 text-sm" title="يوم الشهر">
            {Array.from({ length: 28 }, (_, i) => i + 1).map((d) => <option key={d} value={d}>{d}</option>)}
          </select>
        )}
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
