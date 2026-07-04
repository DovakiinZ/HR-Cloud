"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Loader2, Upload, User } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api-client";
import {
  ApiEmployee, EmployeeInput, createEmployee, updateEmployee, getEmployees,
} from "@/lib/api/employees";
import { getLookup, lookupLabel, LookupItem } from "@/lib/api/lookups";
import { getDepartments, getBranches, OrgOption, orgLabel } from "@/lib/api/org";
import { uploadFile, fileUrl } from "@/lib/api/files";
import { transliterateArabic } from "@/lib/transliterate";
import { Employee } from "@/types";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : (err instanceof Error ? err.message : fallback));
  }
}

interface Props {
  mode?: "create" | "edit";
  employeeId?: string;
  initial?: ApiEmployee;
}

const STATUS_OPTIONS = ["نشط", "في إجازة", "موقوف", "منتهي", "مستقيل"];
const selectClass = "w-full h-9 bg-secondary border border-border px-3 text-sm text-foreground";

// Hoisted to module scope — defining these inside the component would remount inputs
// on every keystroke (focus loss).
function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="border border-border bg-card">
      <div className="px-5 py-3 border-b border-border text-xs font-bold uppercase tracking-wider text-primary">{title}</div>
      <div className="p-5 grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">{children}</div>
    </div>
  );
}
function Field({ label, children, full }: { label: string; children: React.ReactNode; full?: boolean }) {
  return (
    <div className={`space-y-2 ${full ? "sm:col-span-2 lg:col-span-3" : ""}`}>
      <Label className="text-xs font-bold uppercase tracking-wider">{label}</Label>
      {children}
    </div>
  );
}
function LookupSelect({ value, onChange, items, placeholder }: { value: string; onChange: (v: string) => void; items: LookupItem[]; placeholder: string }) {
  return (
    <select value={value} onChange={(e) => onChange(e.target.value)} className={selectClass}>
      <option value="">{placeholder}</option>
      {items.map((i) => <option key={i.id} value={i.id}>{lookupLabel(i)}</option>)}
    </select>
  );
}

function emptyInput(): EmployeeInput {
  return {
    employeeNumber: "", firstNameAr: "", lastNameAr: "", firstName: "", lastName: "",
    nationalId: "", phone: "", email: "", dateOfBirth: "", gender: "ذكر",
    nationalityId: "", jobTitleId: "", contractTypeId: "", employmentTypeId: "",
    departmentId: "", branchId: "", managerId: "", joinDate: "", status: "نشط",
    address: "", city: "", emergencyContactName: "", emergencyContactPhone: "",
    salary: 0, currency: "SAR", gosiEnabled: true, gosiRateOverride: null, paymentMethodId: "", bankId: "", bankAccountNumber: "",
    iban: "", salaryCardNumber: "", cardProvider: "", workLocationId: "",
    leavePolicyId: "", payrollGroupId: "", photoUrl: "", notes: "", allowances: [], additions: [], deductions: [],
  };
}

function fromApi(a: ApiEmployee): EmployeeInput {
  return {
    employeeNumber: a.employeeNumber,
    firstNameAr: a.firstNameAr ?? "", lastNameAr: a.lastNameAr ?? "",
    firstName: a.firstName ?? "", lastName: a.lastName ?? "",
    nationalId: a.nationalId ?? "", phone: a.phone ?? "", email: a.email,
    dateOfBirth: a.dateOfBirth ? a.dateOfBirth.slice(0, 10) : "",
    gender: a.genderAr || "ذكر",
    nationalityId: a.nationalityId ?? "", jobTitleId: a.jobTitleId ?? "",
    contractTypeId: a.contractTypeId ?? "", employmentTypeId: a.employmentTypeId ?? "",
    departmentId: a.departmentId ?? "", branchId: a.branchId ?? "", managerId: a.managerId ?? "",
    joinDate: a.hireDate ? a.hireDate.slice(0, 10) : "", status: a.statusAr || "نشط",
    address: a.address ?? "", city: a.city ?? "",
    emergencyContactName: a.emergencyContactName ?? "", emergencyContactPhone: a.emergencyContactPhone ?? "",
    salary: a.basicSalary, currency: a.currency ?? "SAR",
    gosiEnabled: a.gosiEnabled ?? true, gosiRateOverride: a.gosiRateOverride ?? null,
    paymentMethodId: a.paymentMethodId ?? "", bankId: a.bankId ?? "",
    bankAccountNumber: a.bankAccountNumber ?? "", iban: a.iban ?? "",
    salaryCardNumber: a.salaryCardNumber ?? "", cardProvider: a.cardProvider ?? "",
    workLocationId: a.workLocationId ?? "", leavePolicyId: a.leavePolicyId ?? "",
    payrollGroupId: a.payrollGroupId ?? "", photoUrl: a.photoUrl ?? "", notes: a.notes ?? "",
    allowances: (a.allowances ?? []).map((al) => ({ allowanceTypeId: al.allowanceTypeId, amount: al.amount })),
    additions: (a.additions ?? []).map((x) => ({ typeId: x.typeId, amount: x.amount })),
    deductions: (a.deductions ?? []).map((x) => ({ typeId: x.typeId, amount: x.amount })),
  };
}

export function EmployeeForm({ mode = "create", employeeId, initial }: Props) {
  const router = useRouter();
  const [form, setForm] = useState<EmployeeInput>(() => (initial ? fromApi(initial) : emptyInput()));
  const [saving, setSaving] = useState(false);
  const [uploading, setUploading] = useState(false);

  const [jobTitles, setJobTitles] = useState<LookupItem[]>([]);
  const [nationalities, setNationalities] = useState<LookupItem[]>([]);
  const [contractTypes, setContractTypes] = useState<LookupItem[]>([]);
  const [employmentTypes, setEmploymentTypes] = useState<LookupItem[]>([]);
  const [paymentMethods, setPaymentMethods] = useState<LookupItem[]>([]);
  const [banks, setBanks] = useState<LookupItem[]>([]);
  const [workLocations, setWorkLocations] = useState<LookupItem[]>([]);
  const [leavePolicies, setLeavePolicies] = useState<LookupItem[]>([]);
  const [payrollGroups, setPayrollGroups] = useState<LookupItem[]>([]);
  const [allowanceTypes, setAllowanceTypes] = useState<LookupItem[]>([]);
  const [additionTypes, setAdditionTypes] = useState<LookupItem[]>([]);
  const [deductionTypes, setDeductionTypes] = useState<LookupItem[]>([]);
  const [departments, setDepartments] = useState<OrgOption[]>([]);
  const [branches, setBranches] = useState<OrgOption[]>([]);
  const [managers, setManagers] = useState<Employee[]>([]);

  useEffect(() => {
    (async () => {
      try {
        const [jt, nat, ct, et, pm, bk, wl, lp, pg, at, addt, dedt, deps, brs, emps] = await Promise.all([
          getLookup("job-titles"), getLookup("nationalities"), getLookup("contract-types"),
          getLookup("employment-types"), getLookup("payment-methods"), getLookup("banks"),
          getLookup("work-locations"), getLookup("leave-policies"), getLookup("payroll-groups"),
          getLookup("allowance-types"), getLookup("addition-types"), getLookup("deduction-types"),
          getDepartments(), getBranches(), getEmployees({ pageSize: 200 }),
        ]);
        setJobTitles(jt); setNationalities(nat); setContractTypes(ct); setEmploymentTypes(et);
        setPaymentMethods(pm); setBanks(bk); setWorkLocations(wl); setLeavePolicies(lp);
        setPayrollGroups(pg); setAllowanceTypes(at); setAdditionTypes(addt); setDeductionTypes(dedt);
        setDepartments(deps); setBranches(brs);
        setManagers(emps.filter((e) => e.id !== employeeId));
      } catch (err) { notifyError(err, "تعذر تحميل القوائم"); }
    })();
  }, [employeeId]);

  const set = <K extends keyof EmployeeInput>(k: K, v: EmployeeInput[K]) => setForm((f) => ({ ...f, [k]: v }));

  // Auto-fill the English name from the Arabic one (transliteration) until the user edits
  // the English field. In edit mode, pre-existing English names are treated as user-set so
  // we never overwrite them.
  const enTouched = useRef({
    first: !!initial?.firstName,
    last: !!initial?.lastName,
  });
  const setFirstNameAr = (v: string) =>
    setForm((f) => ({ ...f, firstNameAr: v, ...(enTouched.current.first ? {} : { firstName: transliterateArabic(v) }) }));
  const setLastNameAr = (v: string) =>
    setForm((f) => ({ ...f, lastNameAr: v, ...(enTouched.current.last ? {} : { lastName: transliterateArabic(v) }) }));
  const setFirstNameEn = (v: string) => { enTouched.current.first = true; set("firstName", v); };
  const setLastNameEn = (v: string) => { enTouched.current.last = true; set("lastName", v); };

  const paymentCode = useMemo(
    () => paymentMethods.find((p) => p.id === form.paymentMethodId)?.code,
    [paymentMethods, form.paymentMethodId]
  );

  const allowanceMap = useMemo(() => {
    const m = new Map<string, number>();
    form.allowances.forEach((a) => m.set(a.allowanceTypeId, a.amount));
    return m;
  }, [form.allowances]);

  function toggleAllowance(typeId: string, on: boolean) {
    setForm((f) => {
      const others = f.allowances.filter((a) => a.allowanceTypeId !== typeId);
      if (!on) return { ...f, allowances: others };
      const def = allowanceTypes.find((t) => t.id === typeId);
      const defaultVal = Number((def?.metadata as Record<string, unknown> | undefined)?.defaultValue ?? 0) || 0;
      return { ...f, allowances: [...others, { allowanceTypeId: typeId, amount: defaultVal }] };
    });
  }
  function setAllowanceAmount(typeId: string, amount: number) {
    setForm((f) => ({ ...f, allowances: f.allowances.map((a) => a.allowanceTypeId === typeId ? { ...a, amount } : a) }));
  }

  // Additions / Deductions share one shape ({ typeId, amount }); a `key` selects the field.
  const additionMap = useMemo(() => new Map(form.additions.map((a) => [a.typeId, a.amount])), [form.additions]);
  const deductionMap = useMemo(() => new Map(form.deductions.map((a) => [a.typeId, a.amount])), [form.deductions]);
  function toggleComp(key: "additions" | "deductions", typeId: string, on: boolean) {
    setForm((f) => {
      const others = f[key].filter((a) => a.typeId !== typeId);
      return { ...f, [key]: on ? [...others, { typeId, amount: 0 }] : others };
    });
  }
  function setCompAmount(key: "additions" | "deductions", typeId: string, amount: number) {
    setForm((f) => ({ ...f, [key]: f[key].map((a) => a.typeId === typeId ? { ...a, amount } : a) }));
  }

  async function onPhoto(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setUploading(true);
    try {
      const res = await uploadFile(file, "photo");
      set("photoUrl", res.url);
      toast.success("تم رفع الصورة");
    } catch (err) { notifyError(err, "تعذر رفع الصورة"); }
    finally { setUploading(false); e.target.value = ""; }
  }

  async function submit() {
    if (!form.firstNameAr.trim() || !form.lastNameAr.trim()) { toast.error("الاسم الأول واسم العائلة (عربي) مطلوبان"); return; }
    if (!form.email.trim()) { toast.error("البريد الإلكتروني مطلوب"); return; }
    if (!form.dateOfBirth) { toast.error("تاريخ الميلاد مطلوب"); return; }
    if (!form.joinDate) { toast.error("تاريخ الالتحاق مطلوب"); return; }
    if (paymentCode === "BANK_TRANSFER" && (!form.iban?.trim() || !form.bankId)) { toast.error("عند التحويل البنكي: البنك ورقم الآيبان مطلوبان"); return; }
    if (paymentCode === "SALARY_CARD" && !form.salaryCardNumber?.trim()) { toast.error("رقم بطاقة الراتب مطلوب"); return; }
    setSaving(true);
    try {
      if (mode === "edit" && employeeId) { await updateEmployee(employeeId, form); toast.success("تم تحديث بيانات الموظف"); }
      else { await createEmployee(form); toast.success("تمت إضافة الموظف"); }
      router.push(employeeId && mode === "edit" ? `/employees/${employeeId}` : "/employees");
    } catch (err) { notifyError(err, "تعذر حفظ الموظف"); }
    finally { setSaving(false); }
  }

  const photoSrc = fileUrl(form.photoUrl);

  return (
    <div className="space-y-5">
      <Section title="المعلومات الشخصية">
        <div className="sm:col-span-2 lg:col-span-3 flex items-center gap-4">
          <div className="h-20 w-20 border border-border bg-secondary overflow-hidden flex items-center justify-center shrink-0">
            {photoSrc ? <img src={photoSrc} alt="" className="h-full w-full object-cover" /> : <User className="h-8 w-8 text-muted-foreground" />}
          </div>
          <label className="inline-flex items-center gap-2 h-9 px-4 border border-border bg-secondary text-sm cursor-pointer hover:bg-card transition-colors">
            {uploading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
            {uploading ? "جاري الرفع..." : "صورة الموظف"}
            <input type="file" accept="image/*" className="hidden" onChange={onPhoto} disabled={uploading} />
          </label>
          {photoSrc && <button type="button" onClick={() => set("photoUrl", "")} className="text-xs text-destructive hover:underline">إزالة</button>}
        </div>
        <Field label="الاسم الأول (عربي) *"><Input value={form.firstNameAr} onChange={(e) => setFirstNameAr(e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="اسم العائلة (عربي) *"><Input value={form.lastNameAr} onChange={(e) => setLastNameAr(e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="الجنس">
          <select value={form.gender} onChange={(e) => set("gender", e.target.value)} className={selectClass}>
            <option value="ذكر">ذكر</option><option value="أنثى">أنثى</option>
          </select>
        </Field>
        <Field label="الاسم الأول (إنجليزي)"><Input value={form.firstName} onChange={(e) => setFirstNameEn(e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="اسم العائلة (إنجليزي)"><Input value={form.lastName} onChange={(e) => setLastNameEn(e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="تاريخ الميلاد *"><Input type="date" value={form.dateOfBirth} onChange={(e) => set("dateOfBirth", e.target.value)} className="bg-secondary border-border" /></Field>
      </Section>

      <Section title="معلومات الهوية">
        <Field label="رقم الهوية / الإقامة"><Input value={form.nationalId} onChange={(e) => set("nationalId", e.target.value)} className="bg-secondary border-border font-mono" /></Field>
        <Field label="الجنسية"><LookupSelect value={form.nationalityId ?? ""} onChange={(v) => set("nationalityId", v)} items={nationalities} placeholder="— اختر —" /></Field>
      </Section>

      <Section title="معلومات التواصل">
        <Field label="الجوال"><Input value={form.phone} onChange={(e) => set("phone", e.target.value)} className="bg-secondary border-border font-mono" /></Field>
        <Field label="البريد الإلكتروني *"><Input type="email" value={form.email} onChange={(e) => set("email", e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="المدينة"><Input value={form.city} onChange={(e) => set("city", e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="العنوان" full><Input value={form.address} onChange={(e) => set("address", e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="اسم جهة اتصال الطوارئ"><Input value={form.emergencyContactName} onChange={(e) => set("emergencyContactName", e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="هاتف جهة اتصال الطوارئ"><Input value={form.emergencyContactPhone} onChange={(e) => set("emergencyContactPhone", e.target.value)} className="bg-secondary border-border font-mono" /></Field>
      </Section>

      <Section title="معلومات التوظيف">
        <Field label="الرقم الوظيفي"><Input value={form.employeeNumber} onChange={(e) => set("employeeNumber", e.target.value)} disabled={mode === "edit"} className="bg-secondary border-border font-mono disabled:opacity-60" placeholder="يُولّد تلقائياً" /></Field>
        <Field label="المسمى الوظيفي"><LookupSelect value={form.jobTitleId ?? ""} onChange={(v) => set("jobTitleId", v)} items={jobTitles} placeholder="— اختر —" /></Field>
        <Field label="تاريخ الالتحاق *"><Input type="date" value={form.joinDate} onChange={(e) => set("joinDate", e.target.value)} className="bg-secondary border-border" /></Field>
        <Field label="نوع العقد"><LookupSelect value={form.contractTypeId ?? ""} onChange={(v) => set("contractTypeId", v)} items={contractTypes} placeholder="— اختر —" /></Field>
        <Field label="نوع التوظيف"><LookupSelect value={form.employmentTypeId ?? ""} onChange={(v) => set("employmentTypeId", v)} items={employmentTypes} placeholder="— اختر —" /></Field>
        {mode === "edit" && (
          <Field label="الحالة">
            <select value={form.status} onChange={(e) => set("status", e.target.value)} className={selectClass}>
              {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </Field>
        )}
      </Section>

      <Section title="الإسناد التنظيمي">
        <Field label="القسم">
          <select value={form.departmentId ?? ""} onChange={(e) => set("departmentId", e.target.value)} className={selectClass}>
            <option value="">— اختر —</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{orgLabel(d)}</option>)}
          </select>
        </Field>
        <Field label="الفرع">
          <select value={form.branchId ?? ""} onChange={(e) => set("branchId", e.target.value)} className={selectClass}>
            <option value="">— اختر —</option>
            {branches.map((b) => <option key={b.id} value={b.id}>{orgLabel(b)}</option>)}
          </select>
        </Field>
        <Field label="المدير المباشر">
          <select value={form.managerId ?? ""} onChange={(e) => set("managerId", e.target.value)} className={selectClass}>
            <option value="">— لا يوجد —</option>
            {managers.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
          </select>
        </Field>
      </Section>

      <Section title="التعويضات والدفع">
        <Field label="الراتب الأساسي"><Input type="number" min={0} value={form.salary || ""} onChange={(e) => set("salary", Number(e.target.value))} className="bg-secondary border-border" /></Field>
        <Field label="التأمينات الاجتماعية (GOSI)">
          <label className="flex items-center gap-2 text-sm h-9">
            <input type="checkbox" checked={form.gosiEnabled ?? true} onChange={(e) => set("gosiEnabled", e.target.checked)} className="h-4 w-4" />
            <span>خاضع للتأمينات الاجتماعية</span>
          </label>
        </Field>
        {(form.gosiEnabled ?? true) && (
          <Field label="نسبة GOSI مخصّصة % (اختياري)">
            <Input type="number" min={0} max={50} step="0.01" placeholder="افتراضي حسب إعدادات الرواتب"
              value={form.gosiRateOverride ?? ""}
              onChange={(e) => set("gosiRateOverride", e.target.value === "" ? null : Number(e.target.value))}
              className="bg-secondary border-border" />
          </Field>
        )}
        <Field label="العملة"><Input value={form.currency} onChange={(e) => set("currency", e.target.value)} className="bg-secondary border-border" placeholder="SAR" /></Field>
        <Field label="طريقة الدفع"><LookupSelect value={form.paymentMethodId ?? ""} onChange={(v) => set("paymentMethodId", v)} items={paymentMethods} placeholder="— اختر —" /></Field>
        {paymentCode === "BANK_TRANSFER" && (<>
          <Field label="البنك *"><LookupSelect value={form.bankId ?? ""} onChange={(v) => set("bankId", v)} items={banks} placeholder="— اختر —" /></Field>
          <Field label="رقم الآيبان (IBAN) *"><Input value={form.iban} onChange={(e) => set("iban", e.target.value)} className="bg-secondary border-border font-mono" placeholder="SA.." /></Field>
          <Field label="رقم الحساب البنكي"><Input value={form.bankAccountNumber} onChange={(e) => set("bankAccountNumber", e.target.value)} className="bg-secondary border-border font-mono" /></Field>
        </>)}
        {paymentCode === "SALARY_CARD" && (<>
          <Field label="رقم بطاقة الراتب *"><Input value={form.salaryCardNumber} onChange={(e) => set("salaryCardNumber", e.target.value)} className="bg-secondary border-border font-mono" /></Field>
          <Field label="مزود البطاقة"><Input value={form.cardProvider} onChange={(e) => set("cardProvider", e.target.value)} className="bg-secondary border-border" /></Field>
        </>)}

        <div className="sm:col-span-2 lg:col-span-3 space-y-2">
          <Label className="text-xs font-bold uppercase tracking-wider">البدلات</Label>
          <div className="border border-border divide-y divide-border">
            {allowanceTypes.length === 0 && <div className="px-3 py-3 text-xs text-muted-foreground">لا توجد بدلات معرّفة — أضفها من إعدادات الرواتب.</div>}
            {allowanceTypes.map((t) => {
              const on = allowanceMap.has(t.id);
              const meta = (t.metadata as Record<string, unknown> | undefined) ?? {};
              const pct = meta.calculationType === "Percentage";
              return (
                <div key={t.id} className="flex items-center gap-3 px-3 py-2">
                  <input type="checkbox" checked={on} onChange={(e) => toggleAllowance(t.id, e.target.checked)} />
                  <span className="text-sm flex-1">{lookupLabel(t)} {pct && <span className="text-[10px] text-muted-foreground">(نسبة)</span>}</span>
                  {on && (
                    <div className="flex items-center gap-1">
                      <Input type="number" min={0} value={allowanceMap.get(t.id) ?? 0} onChange={(e) => setAllowanceAmount(t.id, Number(e.target.value))} className="bg-secondary border-border h-8 w-32 text-sm" />
                      <span className="text-xs text-muted-foreground">{pct ? "%" : "ريال"}</span>
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        </div>

        <div className="sm:col-span-2 lg:col-span-3 grid grid-cols-1 lg:grid-cols-2 gap-4">
          <CompEditor label="الإضافات (مكافآت/عمل إضافي)" emptyHint="لا توجد أنواع إضافات معرّفة" types={additionTypes} map={additionMap} onToggle={(id, on) => toggleComp("additions", id, on)} onAmount={(id, v) => setCompAmount("additions", id, v)} />
          <CompEditor label="الاستقطاعات (عدا GOSI)" emptyHint="لا توجد أنواع استقطاعات معرّفة" types={deductionTypes} map={deductionMap} onToggle={(id, on) => toggleComp("deductions", id, on)} onAmount={(id, v) => setCompAmount("deductions", id, v)} />
        </div>
      </Section>

      <Section title="الحضور والموقع">
        <Field label="موقع الحضور"><LookupSelect value={form.workLocationId ?? ""} onChange={(v) => set("workLocationId", v)} items={workLocations} placeholder="— اختر —" /></Field>
        <Field label="سياسة الإجازات"><LookupSelect value={form.leavePolicyId ?? ""} onChange={(v) => set("leavePolicyId", v)} items={leavePolicies} placeholder="— اختر —" /></Field>
        <Field label="مجموعة الرواتب"><LookupSelect value={form.payrollGroupId ?? ""} onChange={(v) => set("payrollGroupId", v)} items={payrollGroups} placeholder="— اختر —" /></Field>
      </Section>

      <Section title="ملاحظات">
        <div className="sm:col-span-2 lg:col-span-3 space-y-2">
          <Label className="text-xs font-bold uppercase tracking-wider">ملاحظات</Label>
          <textarea value={form.notes} onChange={(e) => set("notes", e.target.value)} className="w-full bg-secondary border border-border px-3 py-2 text-sm text-foreground min-h-[80px]" />
        </div>
      </Section>

      <div className="flex items-center justify-end gap-2">
        <Button variant="outline" onClick={() => router.back()} disabled={saving}>إلغاء</Button>
        <Button onClick={submit} disabled={saving} className="font-bold gap-2 min-w-32">
          {saving && <Loader2 className="h-4 w-4 animate-spin" />}
          {saving ? "جاري الحفظ..." : (mode === "edit" ? "حفظ التعديلات" : "إضافة الموظف")}
        </Button>
      </div>
    </div>
  );
}

// Reusable editor for salary additions / deductions (toggle + amount per master-data type).
function CompEditor({ label, emptyHint, types, map, onToggle, onAmount }: {
  label: string; emptyHint: string; types: LookupItem[]; map: Map<string, number>;
  onToggle: (id: string, on: boolean) => void; onAmount: (id: string, amount: number) => void;
}) {
  return (
    <div className="space-y-2">
      <Label className="text-xs font-bold uppercase tracking-wider">{label}</Label>
      <div className="border border-border divide-y divide-border">
        {types.length === 0 && <div className="px-3 py-3 text-xs text-muted-foreground">{emptyHint}</div>}
        {types.map((t) => {
          const on = map.has(t.id);
          return (
            <div key={t.id} className="flex items-center gap-3 px-3 py-2">
              <input type="checkbox" checked={on} onChange={(e) => onToggle(t.id, e.target.checked)} />
              <span className="text-sm flex-1">{lookupLabel(t)}</span>
              {on && (
                <div className="flex items-center gap-1">
                  <Input type="number" min={0} value={map.get(t.id) ?? 0} onChange={(e) => onAmount(t.id, Number(e.target.value))} className="bg-secondary border-border h-8 w-32 text-sm" />
                  <span className="text-xs text-muted-foreground">ريال</span>
                </div>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
