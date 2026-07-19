"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Share2, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { apiFetch } from "@/lib/api-client";
import { getDepartments, orgLabel, type OrgOption } from "@/lib/api/org";
import { addReportShare, removeReportShare, ReportShare } from "@/lib/api/reports";

interface Opt { id: string; label: string }
type Target = "user" | "role" | "department";

export function ReportShareDialog({ reportId, reportName, shares, onClose, onChanged }: {
  reportId: string; reportName: string; shares: ReportShare[]; onClose: () => void; onChanged: () => void;
}) {
  const [target, setTarget] = useState<Target>("department");
  const [users, setUsers] = useState<Opt[]>([]);
  const [roles, setRoles] = useState<Opt[]>([]);
  const [departments, setDepartments] = useState<Opt[]>([]);
  const [selected, setSelected] = useState("");
  const [canEdit, setCanEdit] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    getDepartments().then((d: OrgOption[]) => setDepartments(d.map((x) => ({ id: x.id, label: orgLabel(x) })))).catch(() => {});
    apiFetch<{ id: string; name: string; nameAr?: string | null }[]>("/api/roles")
      .then((r) => setRoles((r ?? []).map((x) => ({ id: x.id, label: x.nameAr || x.name })))).catch(() => {});
    apiFetch<{ id: string; fullName?: string; email: string }[]>("/api/users")
      .then((u) => setUsers((u ?? []).map((x) => ({ id: x.id, label: x.fullName || x.email })))).catch(() => {});
  }, []);

  const options = target === "user" ? users : target === "role" ? roles : departments;
  const labelFor = useCallback((s: ReportShare): string => {
    if (s.sharedWithUserId) return users.find((u) => u.id === s.sharedWithUserId)?.label ?? "مستخدم";
    if (s.sharedWithRoleId) return roles.find((r) => r.id === s.sharedWithRoleId)?.label ?? "دور";
    if (s.sharedWithDepartmentId) return departments.find((d) => d.id === s.sharedWithDepartmentId)?.label ?? "إدارة";
    return "—";
  }, [users, roles, departments]);

  const onShare = async () => {
    if (!selected) return;
    setSaving(true);
    try {
      await addReportShare(reportId, {
        sharedWithUserId: target === "user" ? selected : null,
        sharedWithRoleId: target === "role" ? selected : null,
        sharedWithDepartmentId: target === "department" ? selected : null,
        canEdit,
      });
      toast.success("تمت المشاركة"); setSelected(""); onChanged();
    } catch { toast.error("تعذر المشاركة"); }
    finally { setSaving(false); }
  };

  const onRevoke = async (shareId: string) => {
    try { await removeReportShare(reportId, shareId); toast.success("تم إلغاء المشاركة"); onChanged(); }
    catch { toast.error("تعذر إلغاء المشاركة"); }
  };

  const tabCls = (t: Target) => `flex-1 py-2 text-sm ${target === t ? "border-b-2 border-primary font-bold" : "text-muted-foreground"}`;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" dir="rtl">
      <div className="absolute inset-0 bg-black/60" onClick={onClose} />
      <div className="relative z-10 w-full max-w-md border border-border bg-card shadow-xl">
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <h3 className="flex items-center gap-2 font-bold"><Share2 className="h-4 w-4" /> مشاركة: {reportName}</h3>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
        </div>
        <div className="space-y-3 p-5">
          <div className="flex border-b border-border">
            <button className={tabCls("department")} onClick={() => { setTarget("department"); setSelected(""); }}>إدارة</button>
            <button className={tabCls("role")} onClick={() => { setTarget("role"); setSelected(""); }}>دور</button>
            <button className={tabCls("user")} onClick={() => { setTarget("user"); setSelected(""); }}>مستخدم</button>
          </div>
          <select value={selected} onChange={(e) => setSelected(e.target.value)} className="h-10 w-full border border-border bg-secondary px-3 text-sm">
            <option value="">— اختر —</option>
            {options.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
          </select>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={canEdit} onChange={(e) => setCanEdit(e.target.checked)} /> السماح بالتعديل</label>
          <button onClick={onShare} disabled={!selected || saving} className="inline-flex h-10 w-full items-center justify-center gap-2 bg-primary text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-40">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Share2 className="h-4 w-4" />} مشاركة
          </button>
          {shares.length > 0 && (
            <div className="border-t border-border pt-3">
              <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">المشاركات الحالية</p>
              <div className="space-y-1">
                {shares.map((s) => (
                  <div key={s.id} className="flex items-center justify-between border border-border px-3 py-2 text-sm">
                    <span>{labelFor(s)} {s.canEdit && <span className="text-xs text-primary">(تعديل)</span>}</span>
                    <button onClick={() => onRevoke(s.id)} className="text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
