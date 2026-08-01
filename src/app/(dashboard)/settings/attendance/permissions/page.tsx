"use client";

import { type ReactNode, useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowRight, Loader2, Pencil, Plus, Save, X } from "lucide-react";
import { toast } from "sonner";
import { usePermissions } from "@/lib/permissions";
import { ApiError } from "@/lib/api-client";
import {
  createMasterDataItem, getMasterDataItems, MasterDataItem, parseMetadata, updateMasterDataItem,
} from "@/lib/api/master-data";
import { getDepartments, getBranches, orgLabel, type OrgOption } from "@/lib/api/org";
import { getEmployees } from "@/lib/api/employees";
import {
  DEFAULT_PERMISSION_RULES, EXCEED_BEHAVIORS, PERMISSION_TYPE_OBJECT,
  type PermissionExceedBehavior, type PermissionTypeRules, type SelectionScope,
} from "@/lib/api/attendance-permissions";
import type { Employee } from "@/types";

const OBJ = PERMISSION_TYPE_OBJECT;

function limitTag(v: number | null, unit: string): string | null {
  return v == null ? null : `${v} ${unit}`;
}
function behaviorLabel(b: PermissionExceedBehavior): string {
  return EXCEED_BEHAVIORS.find((x) => x.value === b)?.labelAr ?? "—";
}
function eligibilitySummary(s: SelectionScope | null): string {
  if (!s || s.mode === "All") return "كامل الشركة";
  const parts: string[] = [];
  const dep = s.include.find((c) => c.dimension === "Department")?.valueIds.length ?? 0;
  const br = s.include.find((c) => c.dimension === "Branch")?.valueIds.length ?? 0;
  if (dep) parts.push(`${dep} إدارة`);
  if (br) parts.push(`${br} فرع`);
  if (s.includeEmployeeIds.length) parts.push(`${s.includeEmployeeIds.length} موظف`);
  if (s.excludeEmployeeIds.length) parts.push(`استثناء ${s.excludeEmployeeIds.length}`);
  return parts.length ? parts.join(" · ") : "محدد";
}

export default function PermissionTypesSettingsPage() {
  const { has } = usePermissions();
  const canEdit = has("Platform.MasterData.Edit") || has("Settings.Edit");
  const [items, setItems] = useState<MasterDataItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState<MasterDataItem | "new" | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try { setItems(await getMasterDataItems(OBJ, { includeInactive: true })); }
    catch { toast.error("تعذر تحميل أنواع الاستئذان"); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => { load(); }, [load]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold">إعدادات الاستئذان</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            عرّف أنواع الاستئذان وقواعدها: مدفوع/غير مدفوع، الحدود، السلوك عند التجاوز، والأهلية
          </p>
        </div>
        <Link href="/settings" className="inline-flex h-10 items-center gap-2 border border-border px-4 text-sm hover:bg-muted">
          <ArrowRight className="h-4 w-4" /> الإعدادات
        </Link>
      </div>

      {loading ? <Spinner /> : (
        <div className="space-y-3">
          {canEdit && (
            <button onClick={() => setEditing("new")} className="inline-flex h-10 items-center gap-2 bg-primary px-4 text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80">
              <Plus className="h-4 w-4" /> نوع استئذان جديد
            </button>
          )}
          <div className="space-y-2">
            {items.map((it) => {
              const r = parseMetadata<PermissionTypeRules>(it, DEFAULT_PERMISSION_RULES);
              const tags = [
                r.paid ? "مدفوع" : "غير مدفوع",
                limitTag(r.maxMinutesPerRequest, "د/طلب"),
                limitTag(r.maxMinutesPerDay, "د/يوم"),
                limitTag(r.maxMinutesPerMonth, "د/شهر"),
                limitTag(r.maxRequestsPerDay, "طلب/يوم"),
                limitTag(r.maxRequestsPerMonth, "طلب/شهر"),
              ].filter(Boolean) as string[];
              return (
                <div key={it.id} className={`flex items-center justify-between gap-3 border border-border bg-card px-4 py-3 ${it.isActive ? "" : "opacity-50"}`}>
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="font-medium">{it.nameAr}</span>
                      <span className="font-mono text-xs text-muted-foreground">{it.code}</span>
                      {!it.isActive && <span className="text-xs text-muted-foreground">(غير مفعّل)</span>}
                    </div>
                    <div className="mt-1 flex flex-wrap gap-1.5 text-xs text-muted-foreground">
                      {tags.map((t, i) => <Tag key={i}>{t}</Tag>)}
                      <Tag>عند التجاوز: {behaviorLabel(r.exceedBehavior)}</Tag>
                      <Tag>الأهلية: {eligibilitySummary(r.eligibility)}</Tag>
                    </div>
                  </div>
                  {canEdit && <button onClick={() => setEditing(it)} className="text-muted-foreground hover:text-foreground"><Pencil className="h-4 w-4" /></button>}
                </div>
              );
            })}
            {items.length === 0 && <p className="text-sm text-muted-foreground">لا توجد أنواع استئذان بعد</p>}
          </div>
        </div>
      )}

      {editing && (
        <PermissionTypeDialog
          item={editing === "new" ? null : editing}
          onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); load(); }}
        />
      )}
    </div>
  );
}

function PermissionTypeDialog({ item, onClose, onSaved }: { item: MasterDataItem | null; onClose: () => void; onSaved: () => void }) {
  const [code, setCode] = useState(item?.code ?? "");
  const [nameAr, setNameAr] = useState(item?.nameAr ?? "");
  const [nameEn, setNameEn] = useState(item?.nameEn ?? "");
  const [isActive, setIsActive] = useState(item?.isActive ?? true);
  const [rules, setRules] = useState<PermissionTypeRules>(item ? parseMetadata<PermissionTypeRules>(item, DEFAULT_PERMISSION_RULES) : DEFAULT_PERMISSION_RULES);
  const [saving, setSaving] = useState(false);
  const setR = (patch: Partial<PermissionTypeRules>) => setRules((p) => ({ ...p, ...patch }));

  const save = async () => {
    if (!nameAr.trim() || !nameEn.trim() || (!item && !code.trim())) { toast.error("أكمل الحقول المطلوبة"); return; }
    setSaving(true);
    try {
      const payload = { nameAr: nameAr.trim(), nameEn: nameEn.trim(), isActive, metadata: rules as unknown as Record<string, unknown> };
      if (item) await updateMasterDataItem(item.id, payload);
      else await createMasterDataItem(OBJ, { code: code.trim().toUpperCase().replace(/\s+/g, "_"), ...payload });
      toast.success("تم الحفظ");
      onSaved();
    } catch (e) { toast.error(e instanceof ApiError ? e.message : "تعذر الحفظ"); }
    finally { setSaving(false); }
  };

  const inp = "h-9 w-full border border-border bg-secondary px-3 text-sm";
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/60" onClick={onClose} />
      <div className="relative z-10 max-h-[88vh] w-full max-w-2xl overflow-auto border border-border bg-card">
        <div className="sticky top-0 flex items-center justify-between border-b border-border bg-card px-5 py-4">
          <h3 className="font-bold">{item ? "تعديل نوع استئذان" : "نوع استئذان جديد"}</h3>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
        </div>

        <div className="space-y-4 p-5">
          <div className="grid grid-cols-2 gap-3">
            <Field label="الرمز"><input value={code} disabled={!!item} onChange={(e) => setCode(e.target.value)} className={`${inp} ${item ? "opacity-60" : ""}`} dir="ltr" placeholder="GENERAL" /></Field>
            <Check2 label="نشط" checked={isActive} onChange={setIsActive} />
            <Field label="الاسم (عربي)"><input value={nameAr} onChange={(e) => setNameAr(e.target.value)} className={inp} /></Field>
            <Field label="الاسم (إنجليزي)"><input value={nameEn} onChange={(e) => setNameEn(e.target.value)} className={inp} dir="ltr" /></Field>
          </div>

          <Section title="القاعدة المالية">
            <div className="grid grid-cols-2 gap-3">
              <Check2 label="استئذان مدفوع (لا يُخصم من الراتب)" checked={rules.paid} onChange={(v) => setR({ paid: v })} />
            </div>
            {!rules.paid && (
              <p className="mt-1 text-xs text-muted-foreground">
                غير مدفوع: يُنشأ خصم في الراتب يعادل ساعات الاستئذان المعتمدة على أساس الحساب المُهيأ للشركة.
              </p>
            )}
          </Section>

          <Section title="الحدود (اتركها فارغة = بلا حد)">
            <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
              <LimitField label="دقائق / الطلب" value={rules.maxMinutesPerRequest} onChange={(v) => setR({ maxMinutesPerRequest: v })} />
              <LimitField label="دقائق / اليوم" value={rules.maxMinutesPerDay} onChange={(v) => setR({ maxMinutesPerDay: v })} />
              <LimitField label="دقائق / الشهر" value={rules.maxMinutesPerMonth} onChange={(v) => setR({ maxMinutesPerMonth: v })} />
              <LimitField label="طلبات / اليوم" value={rules.maxRequestsPerDay} onChange={(v) => setR({ maxRequestsPerDay: v })} />
              <LimitField label="طلبات / الشهر" value={rules.maxRequestsPerMonth} onChange={(v) => setR({ maxRequestsPerMonth: v })} />
            </div>
            <div className="mt-3">
              <Field label="السلوك عند تجاوز الحد">
                <select value={rules.exceedBehavior} onChange={(e) => setR({ exceedBehavior: Number(e.target.value) as PermissionExceedBehavior })} className={inp}>
                  {EXCEED_BEHAVIORS.map((b) => <option key={b.value} value={b.value}>{b.labelAr}</option>)}
                </select>
              </Field>
            </div>
          </Section>

          <Section title="الأهلية">
            <EligibilityEditor scope={rules.eligibility} onChange={(s) => setR({ eligibility: s })} />
          </Section>
        </div>

        <div className="sticky bottom-0 flex justify-end gap-2 border-t border-border bg-card px-5 py-4">
          <button onClick={onClose} className="h-10 px-4 text-sm text-muted-foreground hover:text-foreground">إلغاء</button>
          <button onClick={save} disabled={saving} className="inline-flex h-10 items-center gap-2 bg-primary px-5 text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-50">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />} حفظ
          </button>
        </div>
      </div>
    </div>
  );
}

// ── Eligibility editor: All (entire company) vs Specific (departments/branches/employees) ──
function EligibilityEditor({ scope, onChange }: { scope: SelectionScope | null; onChange: (s: SelectionScope | null) => void }) {
  const isAll = !scope || scope.mode === "All";
  const [departments, setDepartments] = useState<OrgOption[]>([]);
  const [branches, setBranches] = useState<OrgOption[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);

  useEffect(() => {
    getDepartments().then(setDepartments).catch(() => {});
    getBranches().then(setBranches).catch(() => {});
    getEmployees().then(setEmployees).catch(() => {});
  }, []);

  const deptIds = scope?.include.find((c) => c.dimension === "Department")?.valueIds ?? [];
  const branchIds = scope?.include.find((c) => c.dimension === "Branch")?.valueIds ?? [];
  const includeEmp = scope?.includeEmployeeIds ?? [];
  const excludeEmp = scope?.excludeEmployeeIds ?? [];

  const build = (patch: { deptIds?: string[]; branchIds?: string[]; includeEmp?: string[]; excludeEmp?: string[] }): SelectionScope => {
    const d = patch.deptIds ?? deptIds;
    const b = patch.branchIds ?? branchIds;
    const inc = patch.includeEmp ?? includeEmp;
    const exc = patch.excludeEmp ?? excludeEmp;
    const include: SelectionScope["include"] = [];
    if (d.length) include.push({ dimension: "Department", valueIds: d });
    if (b.length) include.push({ dimension: "Branch", valueIds: b });
    return { mode: "Criteria", include, exclude: [], includeEmployeeIds: inc, excludeEmployeeIds: exc };
  };

  return (
    <div className="space-y-3">
      <div className="flex gap-4 text-sm">
        <label className="flex items-center gap-2">
          <input type="radio" checked={isAll} onChange={() => onChange(null)} /> كامل الشركة
        </label>
        <label className="flex items-center gap-2">
          <input type="radio" checked={!isAll} onChange={() => onChange(build({}))} /> تحديد
        </label>
      </div>

      {!isAll && (
        <div className="grid gap-3 md:grid-cols-2">
          <CheckList label="الإدارات" options={departments.map((d) => ({ id: d.id, label: orgLabel(d) }))} selected={deptIds} onChange={(ids) => onChange(build({ deptIds: ids }))} />
          <CheckList label="الفروع" options={branches.map((b) => ({ id: b.id, label: orgLabel(b) }))} selected={branchIds} onChange={(ids) => onChange(build({ branchIds: ids }))} />
          <CheckList label="موظفون محددون (إضافة)" options={employees.map((e) => ({ id: e.id, label: e.name }))} selected={includeEmp} onChange={(ids) => onChange(build({ includeEmp: ids }))} />
          <CheckList label="موظفون مستثنون" options={employees.map((e) => ({ id: e.id, label: e.name }))} selected={excludeEmp} onChange={(ids) => onChange(build({ excludeEmp: ids }))} />
        </div>
      )}
    </div>
  );
}

function CheckList({ label, options, selected, onChange }: { label: string; options: { id: string; label: string }[]; selected: string[]; onChange: (ids: string[]) => void }) {
  const [q, setQ] = useState("");
  const set = new Set(selected);
  const toggle = (id: string) => {
    const next = new Set(set);
    if (next.has(id)) next.delete(id); else next.add(id);
    onChange([...next]);
  };
  const filtered = q ? options.filter((o) => o.label.toLowerCase().includes(q.toLowerCase())) : options;
  return (
    <div className="space-y-1">
      <label className="text-xs font-bold uppercase tracking-wider text-muted-foreground">{label} {selected.length > 0 && `(${selected.length})`}</label>
      <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="بحث…" className="h-8 w-full border border-border bg-secondary px-2 text-xs" />
      <div className="max-h-32 space-y-1 overflow-auto border border-border bg-secondary/50 p-2">
        {filtered.length === 0 && <p className="text-xs text-muted-foreground">لا توجد عناصر</p>}
        {filtered.map((o) => (
          <label key={o.id} className="flex items-center gap-2 text-xs">
            <input type="checkbox" checked={set.has(o.id)} onChange={() => toggle(o.id)} /> {o.label}
          </label>
        ))}
      </div>
    </div>
  );
}

// ── small UI ──
function Spinner() { return <div className="flex h-40 items-center justify-center text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" /></div>; }
function Tag({ children }: { children: ReactNode }) { return <span className="border border-border bg-secondary px-1.5 py-0.5">{children}</span>; }
function Field({ label, children }: { label: string; children: ReactNode }) { return <div className="space-y-1"><label className="text-xs font-bold uppercase tracking-wider text-muted-foreground">{label}</label>{children}</div>; }
function Check2({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return <label className="flex h-9 items-center gap-2 text-sm"><input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} /> {label}</label>;
}
function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="border-t border-border pt-3">
      <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">{title}</p>
      {children}
    </div>
  );
}
function LimitField({ label, value, onChange }: { label: string; value: number | null; onChange: (v: number | null) => void }) {
  return (
    <Field label={label}>
      <input
        type="number" min={0}
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value === "" ? null : Math.max(0, Number(e.target.value)))}
        placeholder="∞"
        className="h-9 w-full border border-border bg-secondary px-3 text-sm"
      />
    </Field>
  );
}
