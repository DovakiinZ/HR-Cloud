"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { ArrowRight, Plus, Pencil, Trash2, Loader2, Lock, Clock, FileSignature, GitBranch, Zap } from "lucide-react";
import { toast } from "sonner";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Badge } from "@/components/ui/badge";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { ApiError } from "@/lib/api-client";
import { usePermission } from "@/lib/permissions";
import { MasterDataItem } from "@/lib/api/master-data";
import {
  RequestTypeMeta, emptyRequestTypeMeta, getRequestTypeMeta,
  listRequestTypes, createRequestType, updateRequestType, deleteRequestType,
} from "@/lib/api/requests";
import { listRequestTypeDefs, provisionSystemRequestTypes, RequestTypeListItem } from "@/lib/api/request-types";
import { RequestEffectsDialog } from "@/components/requests/request-effects-dialog";
import { getLookup, lookupLabel, LookupItem } from "@/lib/api/lookups";
import { getFormDefinitions, formLabel, FormDefinition } from "@/lib/api/forms";
import { getWorkflowDefinitions, workflowLabel, WorkflowDefinition } from "@/lib/api/workflows";
import { getDocumentTemplates, documentLabel, DocumentTemplate } from "@/lib/api/documents";
import { REQUEST_ICONS, REQUEST_ICON_KEYS, REQUEST_COLORS, requestIcon } from "@/lib/request-icons";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

interface BuilderForm extends RequestTypeMeta {
  code: string;
  nameAr: string;
  nameEn: string;
  description: string;
  icon: string;
  color: string;
  isActive: boolean;
}

const emptyForm: BuilderForm = {
  ...emptyRequestTypeMeta,
  code: "", nameAr: "", nameEn: "", description: "",
  icon: "file-text", color: REQUEST_COLORS[0], isActive: true,
};

export default function RequestTypesPage() {
  const { allowed: canWfView } = usePermission("Platform.Workflows.View");
  const { allowed: canWfEdit } = usePermission("Platform.Workflows.Edit");

  const [items, setItems] = useState<MasterDataItem[]>([]);
  const [categories, setCategories] = useState<LookupItem[]>([]);
  const [forms, setForms] = useState<FormDefinition[]>([]);
  const [workflows, setWorkflows] = useState<WorkflowDefinition[]>([]);
  const [documents, setDocuments] = useState<DocumentTemplate[]>([]);
  const [defs, setDefs] = useState<RequestTypeListItem[]>([]);   // RequestType entities (effect engine), by code
  const [loading, setLoading] = useState(true);

  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<MasterDataItem | null>(null);
  const [form, setForm] = useState<BuilderForm>(emptyForm);
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<MasterDataItem | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [effectsFor, setEffectsFor] = useState<{ def: RequestTypeListItem; item: MasterDataItem } | null>(null);
  const [provisioning, setProvisioning] = useState(false);

  const loadItems = useCallback(async () => {
    try { setItems(await listRequestTypes()); }
    catch (err) { notifyError(err, "تعذر تحميل أنواع الطلبات"); }
  }, []);

  const reloadDefs = useCallback(() => { listRequestTypeDefs().then(setDefs).catch(() => {}); }, []);

  // Register/upgrade the built-in system request types in the effect engine so their ⚡ lights up.
  const provision = useCallback(async () => {
    setProvisioning(true);
    try {
      const res = await provisionSystemRequestTypes();
      const n = (res.created ?? 0) + (res.upgraded ?? 0);
      toast.success(n > 0 ? `تم تسجيل/تحديث ${n} نوع طلب في محرك الإجراءات` : "أنواع الطلبات النظامية مُسجّلة بالفعل");
      reloadDefs();
    } catch (err) { notifyError(err, "تعذّر تسجيل الأنواع (تحتاج صلاحية)"); }
    finally { setProvisioning(false); }
  }, [reloadDefs]);

  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        // Forms/workflows/documents/effect-defs may 403 for limited roles — degrade gracefully.
        const [its, cats] = await Promise.all([listRequestTypes(), getLookup("request-categories")]);
        setItems(its); setCategories(cats);
        const [fs, wf, dt, rd] = await Promise.allSettled([getFormDefinitions(), getWorkflowDefinitions(), getDocumentTemplates(), listRequestTypeDefs()]);
        if (fs.status === "fulfilled") setForms(fs.value);
        if (wf.status === "fulfilled") setWorkflows(wf.value);
        if (dt.status === "fulfilled") setDocuments(dt.value);
        if (rd.status === "fulfilled") setDefs(rd.value);
      } catch (err) { notifyError(err, "تعذر تحميل البيانات"); }
      finally { setLoading(false); }
    })();
  }, []);

  // Match master-data request types to their RequestType entity (effect engine) by code.
  const defByCode = useMemo(() => new Map(defs.map((d) => [d.code.toUpperCase(), d])), [defs]);

  const catName = useMemo(() => {
    const m = new Map(categories.map((c) => [c.id, lookupLabel(c)]));
    return (id?: string) => (id ? m.get(id) ?? "—" : "—");
  }, [categories]);

  function openCreate() { setEditing(null); setForm(emptyForm); setDialogOpen(true); }
  function openEdit(i: MasterDataItem) {
    setEditing(i);
    const meta = getRequestTypeMeta(i);
    setForm({
      ...meta,
      code: i.code, nameAr: i.nameAr, nameEn: i.nameEn, description: i.description ?? "",
      icon: i.icon ?? "file-text", color: i.color ?? REQUEST_COLORS[0], isActive: i.isActive,
    });
    setDialogOpen(true);
  }

  async function save() {
    if (!form.nameAr.trim() || !form.nameEn.trim()) { toast.error("الاسم بالعربية والإنجليزية مطلوبان"); return; }
    if (!editing && !form.code.trim()) { toast.error("الرمز مطلوب"); return; }
    setSaving(true);
    try {
      const wf = workflows.find((w) => w.id === form.workflowDefinitionId);
      const meta: RequestTypeMeta = {
        categoryId: form.categoryId,
        formDefinitionId: form.formDefinitionId,
        workflowDefinitionId: form.workflowDefinitionId,
        workflowCode: wf?.code ?? "",
        slaHours: form.slaHours === null || Number.isNaN(form.slaHours) ? null : Number(form.slaHours),
        generatesDocument: form.generatesDocument,
        documentTemplateId: form.generatesDocument ? form.documentTemplateId : "",
        updatesEmployee: form.updatesEmployee,
        updatesAttendance: form.updatesAttendance,
        updatesPayroll: form.updatesPayroll,
      };
      const payload = {
        code: form.code.trim().toUpperCase(),
        nameAr: form.nameAr.trim(),
        nameEn: form.nameEn.trim(),
        description: form.description.trim() || undefined,
        icon: form.icon,
        color: form.color,
        isActive: form.isActive,
        metadata: meta as unknown as Record<string, unknown>,
      };
      if (editing) { await updateRequestType(editing.id, payload); toast.success("تم تحديث نوع الطلب"); }
      else { await createRequestType(payload); toast.success("تمت إضافة نوع الطلب"); }
      setDialogOpen(false); await loadItems();
    } catch (err) { notifyError(err, "تعذر حفظ نوع الطلب"); } finally { setSaving(false); }
  }

  async function confirmDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    try { await deleteRequestType(deleteTarget.id); toast.success("تم الحذف"); setDeleteTarget(null); await loadItems(); }
    catch (err) { notifyError(err, "تعذر الحذف (قد يكون مستخدماً)"); } finally { setDeleting(false); }
  }

  const selectClass = "w-full h-9 bg-secondary border border-border px-3 text-sm text-foreground";
  const sectionTitle = "text-xs font-bold uppercase tracking-wider text-primary border-b border-border pb-2";

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-2 text-sm">
        <Link href="/settings/requests" className="text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1">
          <ArrowRight className="h-4 w-4" /> إعدادات الطلبات
        </Link>
        <span className="text-muted-foreground">/</span>
        <span>أنواع الطلبات</span>
      </div>

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">أنواع الطلبات</h1>
          <p className="text-sm text-muted-foreground mt-1">مُنشئ الطلبات — اربط كل نوع بنموذج ومسار موافقة وأثر دون كتابة كود</p>
        </div>
        <div className="flex items-center gap-2">
          {canWfEdit && (
            <Button variant="outline" onClick={provision} disabled={provisioning} className="h-10 gap-2 text-sm" title="تسجيل أنواع الطلبات النظامية في محرك الإجراءات لتفعيل زر «الإجراءات»">
              {provisioning ? <Loader2 className="h-4 w-4 animate-spin" /> : <Zap className="h-4 w-4" />} تفعيل محرك الإجراءات
            </Button>
          )}
          <Button onClick={openCreate} className="h-10 gap-2 font-bold uppercase tracking-wider text-sm"><Plus className="h-4 w-4" /> نوع طلب</Button>
        </div>
      </div>

      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">النوع</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">الفئة</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">النموذج / المسار</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">SLA</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">الحالة</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground w-24"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...</TableCell></TableRow>
            ) : items.length === 0 ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground">لا توجد أنواع طلبات</TableCell></TableRow>
            ) : items.map((i) => {
              const meta = getRequestTypeMeta(i);
              const Icon = requestIcon(i.icon);
              return (
                <TableRow key={i.id} className="border-border hover:bg-card/50">
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <span className="flex h-8 w-8 items-center justify-center shrink-0" style={{ backgroundColor: `${i.color ?? "#3b82f6"}1a`, color: i.color ?? "#3b82f6" }}>
                        <Icon className="h-4 w-4" />
                      </span>
                      <div>
                        <div className="font-medium">{i.nameAr}</div>
                        <div className="font-mono text-[10px] text-muted-foreground">{i.code}</div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">{catName(meta.categoryId)}</TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2 text-xs text-muted-foreground">
                      <span className="inline-flex items-center gap-1" title="النموذج"><FileSignature className={`h-3.5 w-3.5 ${meta.formDefinitionId ? "text-green-500" : "text-muted-foreground/40"}`} /></span>
                      <span className="inline-flex items-center gap-1" title="مسار الموافقة"><GitBranch className={`h-3.5 w-3.5 ${meta.workflowDefinitionId ? "text-green-500" : "text-muted-foreground/40"}`} /></span>
                    </div>
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">
                    {meta.slaHours ? <span className="inline-flex items-center gap-1"><Clock className="h-3.5 w-3.5" /> {meta.slaHours}س</span> : "—"}
                  </TableCell>
                  <TableCell>
                    {i.isActive
                      ? <Badge variant="outline" className="text-xs bg-green-500/10 text-green-500 border-green-500/20">نشط</Badge>
                      : <Badge variant="outline" className="text-xs bg-zinc-500/10 text-zinc-400 border-zinc-500/20">غير نشط</Badge>}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1 justify-end">
                      {canWfView && (() => {
                        const def = defByCode.get(i.code.toUpperCase());
                        return def ? (
                          <button onClick={() => setEffectsFor({ def, item: i })} className="relative h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-primary" title="الإجراءات بعد الموافقة">
                            <Zap className="h-4 w-4" />
                            {def.effectCount > 0 && <span className="absolute -top-0.5 -left-0.5 h-3.5 min-w-3.5 px-0.5 rounded-full bg-primary text-[9px] font-bold text-primary-foreground inline-flex items-center justify-center">{def.effectCount}</span>}
                          </button>
                        ) : (
                          <span className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground/25" title="غير مُسجّل في محرك الإجراءات — اضغط «تفعيل محرك الإجراءات» أعلى الصفحة لتسجيل الأنواع النظامية"><Zap className="h-4 w-4" /></span>
                        );
                      })()}
                      <button onClick={() => openEdit(i)} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-foreground" title="تعديل"><Pencil className="h-4 w-4" /></button>
                      {i.isSystemDefault
                        ? <span className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground/40" title="افتراضي"><Lock className="h-4 w-4" /></span>
                        : <button onClick={() => setDeleteTarget(i)} className="h-8 w-8 inline-flex items-center justify-center text-destructive hover:text-destructive/80" title="حذف"><Trash2 className="h-4 w-4" /></button>}
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>

      <Dialog open={dialogOpen} onOpenChange={(o) => { if (!o && !saving) setDialogOpen(false); }}>
        <DialogContent className="sm:max-w-3xl">
          <DialogHeader><DialogTitle>{editing ? "تعديل نوع طلب" : "نوع طلب جديد"}</DialogTitle></DialogHeader>
          <div className="space-y-6 py-2 max-h-[70vh] overflow-y-auto pl-1">

            {/* Identity */}
            <div className="space-y-4">
              <div className={sectionTitle}>الهوية</div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الرمز</Label>
                  <Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} disabled={!!editing} className="bg-secondary border-border font-mono disabled:opacity-60" placeholder="LEAVE" />
                </div>
                <label className="flex items-center gap-2 text-sm cursor-pointer pt-6">
                  <input type="checkbox" checked={form.isActive} onChange={(e) => setForm({ ...form, isActive: e.target.checked })} /> نشط
                </label>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الاسم (عربي)</Label>
                  <Input value={form.nameAr} onChange={(e) => setForm({ ...form, nameAr: e.target.value })} className="bg-secondary border-border" />
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الاسم (إنجليزي)</Label>
                  <Input value={form.nameEn} onChange={(e) => setForm({ ...form, nameEn: e.target.value })} className="bg-secondary border-border" />
                </div>
                <div className="space-y-2 sm:col-span-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الوصف</Label>
                  <Input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} className="bg-secondary border-border" />
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الأيقونة</Label>
                  <div className="flex flex-wrap gap-1.5">
                    {REQUEST_ICON_KEYS.map((k) => {
                      const Ic = REQUEST_ICONS[k];
                      const sel = form.icon === k;
                      return (
                        <button key={k} type="button" onClick={() => setForm({ ...form, icon: k })}
                          className={`h-8 w-8 inline-flex items-center justify-center border transition-colors ${sel ? "border-primary text-primary bg-primary/10" : "border-border text-muted-foreground hover:text-foreground"}`}>
                          <Ic className="h-4 w-4" />
                        </button>
                      );
                    })}
                  </div>
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">اللون</Label>
                  <div className="flex flex-wrap gap-1.5">
                    {REQUEST_COLORS.map((c) => (
                      <button key={c} type="button" onClick={() => setForm({ ...form, color: c })}
                        className={`h-8 w-8 border-2 transition-transform ${form.color === c ? "border-foreground scale-110" : "border-transparent"}`}
                        style={{ backgroundColor: c }} aria-label={c} />
                    ))}
                  </div>
                </div>
              </div>
            </div>

            {/* Classification + form + workflow */}
            <div className="space-y-4">
              <div className={sectionTitle}>التصنيف والمعالجة</div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الفئة</Label>
                  <select value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })} className={selectClass}>
                    <option value="">— بدون فئة —</option>
                    {categories.map((c) => <option key={c.id} value={c.id}>{lookupLabel(c)}</option>)}
                  </select>
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">مدة الإنجاز SLA (ساعات)</Label>
                  <Input type="number" min={0} value={form.slaHours ?? ""} onChange={(e) => setForm({ ...form, slaHours: e.target.value === "" ? null : Number(e.target.value) })} className="bg-secondary border-border" placeholder="48" />
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">النموذج المرتبط</Label>
                  <select value={form.formDefinitionId} onChange={(e) => setForm({ ...form, formDefinitionId: e.target.value })} className={selectClass}>
                    <option value="">— بدون نموذج —</option>
                    {forms.map((f) => <option key={f.id} value={f.id}>{formLabel(f)}</option>)}
                  </select>
                  {forms.length === 0 && <p className="text-[10px] text-muted-foreground">لا توجد نماذج معرّفة بعد.</p>}
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">مسار الموافقة</Label>
                  <select value={form.workflowDefinitionId} onChange={(e) => setForm({ ...form, workflowDefinitionId: e.target.value })} className={selectClass}>
                    <option value="">— بدون مسار —</option>
                    {workflows.map((w) => <option key={w.id} value={w.id}>{workflowLabel(w)}</option>)}
                  </select>
                  {workflows.length === 0 && <p className="text-[10px] text-muted-foreground">لا توجد مسارات معرّفة بعد.</p>}
                </div>
              </div>
            </div>

            {/* Document */}
            <div className="space-y-4">
              <div className={sectionTitle}>المستند الناتج</div>
              <label className="flex items-center gap-2 text-sm cursor-pointer">
                <input type="checkbox" checked={form.generatesDocument} onChange={(e) => setForm({ ...form, generatesDocument: e.target.checked })} />
                يُنشئ مستنداً عند الاكتمال
              </label>
              {form.generatesDocument && (
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">قالب المستند</Label>
                  <select value={form.documentTemplateId} onChange={(e) => setForm({ ...form, documentTemplateId: e.target.value })} className={selectClass}>
                    <option value="">— اختر قالباً —</option>
                    {documents.map((d) => <option key={d.id} value={d.id}>{documentLabel(d)}</option>)}
                  </select>
                  {documents.length === 0 && <p className="text-[10px] text-muted-foreground">لا توجد قوالب مستندات معرّفة بعد.</p>}
                </div>
              )}
            </div>

            {/* Impact */}
            <div className="space-y-4">
              <div className={sectionTitle}>الأثر عند الموافقة</div>
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                <label className="flex items-center gap-2 text-sm cursor-pointer border border-border px-3 py-2">
                  <input type="checkbox" checked={form.updatesEmployee} onChange={(e) => setForm({ ...form, updatesEmployee: e.target.checked })} /> يُحدّث ملف الموظف
                </label>
                <label className="flex items-center gap-2 text-sm cursor-pointer border border-border px-3 py-2">
                  <input type="checkbox" checked={form.updatesAttendance} onChange={(e) => setForm({ ...form, updatesAttendance: e.target.checked })} /> يؤثر على الحضور
                </label>
                <label className="flex items-center gap-2 text-sm cursor-pointer border border-border px-3 py-2">
                  <input type="checkbox" checked={form.updatesPayroll} onChange={(e) => setForm({ ...form, updatesPayroll: e.target.checked })} /> يؤثر على الرواتب
                </label>
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setDialogOpen(false)} disabled={saving}>إلغاء</Button>
            <Button onClick={save} disabled={saving} className="font-bold">{saving ? "جاري الحفظ..." : "حفظ"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <Dialog open={!!deleteTarget} onOpenChange={(o) => { if (!o && !deleting) setDeleteTarget(null); }}>
        <DialogContent showCloseButton={false}>
          <DialogHeader>
            <DialogTitle>حذف نوع طلب</DialogTitle>
            <DialogDescription>هل أنت متأكد من حذف <span className="font-bold text-foreground">{deleteTarget?.nameAr}</span>؟</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteTarget(null)} disabled={deleting}>إلغاء</Button>
            <Button onClick={confirmDelete} disabled={deleting} className="bg-destructive text-white hover:bg-destructive/90">{deleting ? "جاري الحذف..." : "حذف"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {effectsFor && (
        <RequestEffectsDialog
          requestTypeId={effectsFor.def.id}
          requestTypeName={effectsFor.item.nameAr || effectsFor.item.nameEn}
          requestTypeCode={effectsFor.def.code}
          canEdit={canWfEdit}
          onClose={() => setEffectsFor(null)}
          onChanged={reloadDefs}
        />
      )}
    </div>
  );
}
