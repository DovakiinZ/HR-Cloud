"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { ArrowRight, Plus, Pencil, Trash2, Loader2, Lock, FileSignature, GitBranch, Zap, Copy, Power } from "lucide-react";
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
import { getRequestTypeMeta, listRequestTypes } from "@/lib/api/requests";
import {
  listRequestTypeDefs, getRequestTypeDef, createRequestTypeDef, updateRequestTypeDef,
  deleteRequestTypeDef, duplicateRequestTypeDef, setRequestTypeDefActive, provisionSystemRequestTypes,
  RequestTypeListItem,
} from "@/lib/api/request-types";
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

interface FormState {
  code: string;
  nameAr: string;
  nameEn: string;
  description: string;
  categoryId: string;
  formDefinitionId: string;
  workflowDefinitionId: string;
  printTemplateId: string;
  icon: string;
  color: string;
}

const emptyForm: FormState = {
  code: "", nameAr: "", nameEn: "", description: "", categoryId: "",
  formDefinitionId: "", workflowDefinitionId: "", printTemplateId: "",
  icon: "file-text", color: REQUEST_COLORS[0],
};

export default function RequestTypesPage() {
  const { allowed: canWfEdit } = usePermission("Platform.Workflows.Edit");

  const [defs, setDefs] = useState<RequestTypeListItem[]>([]);
  const [legacy, setLegacy] = useState<MasterDataItem[]>([]);   // master-data types with no engine entity
  const [categories, setCategories] = useState<LookupItem[]>([]);
  const [forms, setForms] = useState<FormDefinition[]>([]);
  const [workflows, setWorkflows] = useState<WorkflowDefinition[]>([]);
  const [documents, setDocuments] = useState<DocumentTemplate[]>([]);
  const [loading, setLoading] = useState(true);

  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<RequestTypeListItem | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<RequestTypeListItem | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [effectsFor, setEffectsFor] = useState<RequestTypeListItem | null>(null);
  const [provisioning, setProvisioning] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);   // per-row action key

  const load = useCallback(async () => {
    const [rd, md, cats] = await Promise.allSettled([listRequestTypeDefs(), listRequestTypes(), getLookup("request-categories")]);
    const defList = rd.status === "fulfilled" ? rd.value : [];
    setDefs(defList);
    if (cats.status === "fulfilled") setCategories(cats.value);
    if (md.status === "fulfilled") {
      const codes = new Set(defList.map((d) => d.code.toUpperCase()));
      setLegacy(md.value.filter((m) => !codes.has(m.code.toUpperCase())));
    }
  }, []);

  const reload = useCallback(() => { load().catch((e) => notifyError(e, "تعذر التحديث")); }, [load]);

  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        await load();
        const [fs, wf, dt] = await Promise.allSettled([getFormDefinitions(), getWorkflowDefinitions(), getDocumentTemplates()]);
        if (fs.status === "fulfilled") setForms(fs.value);
        if (wf.status === "fulfilled") setWorkflows(wf.value);
        if (dt.status === "fulfilled") setDocuments(dt.value);
      } catch (err) { notifyError(err, "تعذر تحميل البيانات"); }
      finally { setLoading(false); }
    })();
  }, [load]);

  const catName = useMemo(() => {
    const m = new Map(categories.map((c) => [c.id, lookupLabel(c)]));
    return (id?: string | null) => (id ? m.get(id) ?? "—" : "—");
  }, [categories]);

  function openCreate() { setEditing(null); setForm(emptyForm); setDialogOpen(true); }

  async function openEdit(d: RequestTypeListItem) {
    setEditing(d); setForm(emptyForm); setDialogOpen(true);
    try {
      const full = await getRequestTypeDef(d.id);
      setForm({
        code: full.code, nameAr: full.nameAr, nameEn: full.nameEn,
        description: full.descriptionAr ?? "", categoryId: full.categoryId ?? "",
        formDefinitionId: full.formDefinitionId, workflowDefinitionId: full.workflowDefinitionId ?? "",
        printTemplateId: full.printTemplateId ?? "", icon: full.icon ?? "file-text", color: full.color ?? REQUEST_COLORS[0],
      });
    } catch (err) { notifyError(err, "تعذر تحميل النوع"); setDialogOpen(false); }
  }

  async function save() {
    if (!form.nameAr.trim() || !form.nameEn.trim()) { toast.error("الاسم بالعربية والإنجليزية مطلوبان"); return; }
    if (!editing) {
      if (!form.code.trim()) { toast.error("الرمز مطلوب"); return; }
      if (!form.formDefinitionId) { toast.error("النموذج مطلوب — لكل نوع طلب نموذج يملؤه الموظف"); return; }
    }
    setSaving(true);
    try {
      if (editing) {
        await updateRequestTypeDef(editing.id, {
          nameAr: form.nameAr.trim(), nameEn: form.nameEn.trim(),
          descriptionAr: form.description.trim() || null,
          categoryId: form.categoryId || null,
          workflowDefinitionId: form.workflowDefinitionId || null,
          printTemplateId: form.printTemplateId || null,
          icon: form.icon, color: form.color,
        });
        toast.success("تم تحديث نوع الطلب");
      } else {
        await createRequestTypeDef({
          code: form.code.trim().toUpperCase(), nameAr: form.nameAr.trim(), nameEn: form.nameEn.trim(),
          descriptionAr: form.description.trim() || null,
          categoryId: form.categoryId || null,
          formDefinitionId: form.formDefinitionId,
          workflowDefinitionId: form.workflowDefinitionId || null,
          icon: form.icon, color: form.color,
        });
        toast.success("تمت إضافة نوع الطلب");
      }
      setDialogOpen(false); reload();
    } catch (err) { notifyError(err, "تعذر حفظ نوع الطلب"); } finally { setSaving(false); }
  }

  async function toggleActive(d: RequestTypeListItem) {
    setBusy(`act:${d.id}`);
    try { await setRequestTypeDefActive(d.id, !d.isActive); toast.success(d.isActive ? "تم التعطيل" : "تم التفعيل"); reload(); }
    catch (err) { notifyError(err, "تعذر تغيير الحالة"); } finally { setBusy(null); }
  }

  async function duplicate(d: RequestTypeListItem) {
    setBusy(`dup:${d.id}`);
    try {
      const copy = await duplicateRequestTypeDef(d.id, { nameAr: `${d.nameAr} — نسخة`, nameEn: `${d.nameEn} (Copy)` });
      toast.success("تم إنشاء نسخة");
      reload();
      setEffectsFor(copy);
    } catch (err) { notifyError(err, "تعذر النسخ"); } finally { setBusy(null); }
  }

  async function confirmDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    try { await deleteRequestTypeDef(deleteTarget.id); toast.success("تم الحذف"); setDeleteTarget(null); reload(); }
    catch (err) { notifyError(err, "تعذر الحذف (قد يكون مستخدماً)"); } finally { setDeleting(false); }
  }

  async function registerLegacy(item: MasterDataItem) {
    const meta = getRequestTypeMeta(item);
    if (!meta.formDefinitionId) { toast.error("هذا النوع القديم بلا نموذج — أنشئه من جديد مع نموذج."); return; }
    setBusy(`reg:${item.id}`);
    try {
      const created = await createRequestTypeDef({
        code: item.code, nameAr: item.nameAr, nameEn: item.nameEn,
        descriptionAr: item.description || null,
        formDefinitionId: meta.formDefinitionId,
        workflowDefinitionId: meta.workflowDefinitionId || null,
        icon: item.icon || "file-text", color: item.color || REQUEST_COLORS[0],
      });
      toast.success("تم تفعيل النوع في محرك الطلبات");
      reload();
      setEffectsFor(created);
    } catch (err) { notifyError(err, "تعذر التفعيل"); } finally { setBusy(null); }
  }

  async function provision() {
    setProvisioning(true);
    try {
      const res = await provisionSystemRequestTypes();
      const n = (res.created ?? 0) + (res.upgraded ?? 0);
      toast.success(n > 0 ? `تم تسجيل/تحديث ${n} نوع طلب نظامي` : "الأنواع النظامية مُسجّلة بالفعل");
      reload();
    } catch (err) { notifyError(err, "تعذّر التسجيل (تحتاج صلاحية)"); } finally { setProvisioning(false); }
  }

  const selectClass = "w-full h-9 bg-secondary border border-border px-3 text-sm text-foreground disabled:opacity-60";
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

      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">أنواع الطلبات</h1>
          <p className="text-sm text-muted-foreground mt-1">مُنشئ الطلبات — لكل نوع نموذج ومسار موافقة وإجراءات تلقائية بعد الاعتماد، دون كتابة كود</p>
        </div>
        <div className="flex items-center gap-2">
          {canWfEdit && (
            <Button variant="outline" onClick={provision} disabled={provisioning} className="h-10 gap-2 text-sm" title="تسجيل/تحديث أنواع الطلبات النظامية">
              {provisioning ? <Loader2 className="h-4 w-4 animate-spin" /> : <Zap className="h-4 w-4" />} الأنواع النظامية
            </Button>
          )}
          {canWfEdit && <Button onClick={openCreate} className="h-10 gap-2 font-bold uppercase tracking-wider text-sm"><Plus className="h-4 w-4" /> نوع طلب</Button>}
        </div>
      </div>

      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">النوع</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">الفئة</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">النموذج / المسار</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">الإجراءات</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">الحالة</TableHead>
              <TableHead className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground w-32"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل...</TableCell></TableRow>
            ) : defs.length === 0 ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground">
                لا توجد أنواع طلبات — اضغط «الأنواع النظامية» لتسجيل الطلبات الجاهزة، أو «نوع طلب» لإنشاء نوع جديد.
              </TableCell></TableRow>
            ) : defs.map((d) => {
              const Icon = requestIcon(d.icon);
              return (
                <TableRow key={d.id} className="border-border hover:bg-card/50">
                  <TableCell>
                    <div className="flex items-center gap-2">
                      <span className="flex h-8 w-8 items-center justify-center shrink-0" style={{ backgroundColor: `${d.color ?? "#3b82f6"}1a`, color: d.color ?? "#3b82f6" }}>
                        <Icon className="h-4 w-4" />
                      </span>
                      <div>
                        <div className="font-medium flex items-center gap-1.5">{d.nameAr}{d.isSystem && <Badge variant="outline" className="text-[9px] text-muted-foreground">نظامي</Badge>}</div>
                        <div className="font-mono text-[10px] text-muted-foreground">{d.code}</div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="text-sm text-muted-foreground">{catName(d.categoryId)}</TableCell>
                  <TableCell>
                    <div className="flex items-center gap-2 text-xs text-muted-foreground">
                      <span title="النموذج"><FileSignature className={`h-3.5 w-3.5 ${d.hasForm ? "text-green-500" : "text-muted-foreground/40"}`} /></span>
                      <span title="مسار الموافقة"><GitBranch className={`h-3.5 w-3.5 ${d.hasWorkflow ? "text-green-500" : "text-muted-foreground/40"}`} /></span>
                    </div>
                  </TableCell>
                  <TableCell>
                    <button onClick={() => setEffectsFor(d)} className="relative inline-flex items-center gap-1 border border-border px-2 h-7 text-xs hover:border-primary hover:text-primary" title="الإجراءات بعد الموافقة">
                      <Zap className="h-3.5 w-3.5" /> {d.effectCount > 0 ? `${d.effectCount} إجراء` : "إضافة"}
                    </button>
                  </TableCell>
                  <TableCell>
                    {d.isActive
                      ? <Badge variant="outline" className="text-xs bg-green-500/10 text-green-500 border-green-500/20">نشط</Badge>
                      : <Badge variant="outline" className="text-xs bg-amber-500/10 text-amber-500 border-amber-500/20">مسودة</Badge>}
                  </TableCell>
                  <TableCell>
                    <div className="flex items-center gap-1 justify-end">
                      {canWfEdit && <button onClick={() => toggleActive(d)} disabled={busy === `act:${d.id}`} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-foreground disabled:opacity-50" title={d.isActive ? "تعطيل" : "تفعيل"}>{busy === `act:${d.id}` ? <Loader2 className="h-4 w-4 animate-spin" /> : <Power className="h-4 w-4" />}</button>}
                      {canWfEdit && <button onClick={() => openEdit(d)} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-foreground" title="تعديل"><Pencil className="h-4 w-4" /></button>}
                      {canWfEdit && <button onClick={() => duplicate(d)} disabled={busy === `dup:${d.id}`} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-foreground disabled:opacity-50" title="نسخ وتخصيص">{busy === `dup:${d.id}` ? <Loader2 className="h-4 w-4 animate-spin" /> : <Copy className="h-4 w-4" />}</button>}
                      {d.canDelete
                        ? (canWfEdit && <button onClick={() => setDeleteTarget(d)} className="h-8 w-8 inline-flex items-center justify-center text-destructive hover:text-destructive/80" title="حذف"><Trash2 className="h-4 w-4" /></button>)
                        : <span className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground/40" title="نظامي — لا يُحذف"><Lock className="h-4 w-4" /></span>}
                    </div>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </div>

      {/* Legacy master-data types with no engine entity — offer one-click migration, hide nothing. */}
      {!loading && legacy.length > 0 && (
        <div className="space-y-2">
          <div className="text-xs font-bold uppercase tracking-wider text-amber-500">أنواع قديمة غير مفعّلة ({legacy.length})</div>
          <p className="text-xs text-muted-foreground">هذه الأنواع من النظام القديم ولا يراها الموظفون. فعّلها لتنتقل إلى محرك الطلبات وتدعم الإجراءات.</p>
          <div className="border border-dashed border-border divide-y divide-border">
            {legacy.map((m) => (
              <div key={m.id} className="flex items-center justify-between gap-3 px-3 py-2">
                <div className="min-w-0">
                  <span className="text-sm font-medium">{m.nameAr}</span>
                  <span className="font-mono text-[10px] text-muted-foreground ms-2">{m.code}</span>
                </div>
                {canWfEdit && (
                  <Button variant="outline" size="sm" onClick={() => registerLegacy(m)} disabled={busy === `reg:${m.id}`} className="gap-1.5 shrink-0">
                    {busy === `reg:${m.id}` ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Zap className="h-3.5 w-3.5" />} تفعيل
                  </Button>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Create / edit */}
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
                  <Input value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value })} disabled={!!editing} className="bg-secondary border-border font-mono disabled:opacity-60" placeholder="LEAVE_REQUEST" />
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">الفئة</Label>
                  <select value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })} className={selectClass}>
                    <option value="">— بدون فئة —</option>
                    {categories.map((c) => <option key={c.id} value={c.id}>{lookupLabel(c)}</option>)}
                  </select>
                </div>
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
                      const Ic = REQUEST_ICONS[k]; const sel = form.icon === k;
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
                        className={`h-8 w-8 border-2 transition-transform ${form.color === c ? "border-foreground scale-110" : "border-transparent"}`} style={{ backgroundColor: c }} aria-label={c} />
                    ))}
                  </div>
                </div>
              </div>
            </div>

            {/* Form + workflow + document */}
            <div className="space-y-4">
              <div className={sectionTitle}>النموذج والمعالجة</div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">النموذج المرتبط {!editing && <span className="text-destructive">*</span>}</Label>
                  <select value={form.formDefinitionId} onChange={(e) => setForm({ ...form, formDefinitionId: e.target.value })} disabled={!!editing} className={selectClass}>
                    <option value="">— اختر نموذجاً —</option>
                    {forms.map((f) => <option key={f.id} value={f.id}>{formLabel(f)}</option>)}
                  </select>
                  {editing && <p className="text-[10px] text-muted-foreground">لا يمكن تغيير النموذج بعد الإنشاء.</p>}
                  {!editing && forms.length === 0 && <p className="text-[10px] text-amber-500">لا توجد نماذج — أنشئ نموذجاً أولاً.</p>}
                </div>
                <div className="space-y-2">
                  <Label className="text-xs font-bold uppercase tracking-wider">مسار الموافقة</Label>
                  <select value={form.workflowDefinitionId} onChange={(e) => setForm({ ...form, workflowDefinitionId: e.target.value })} className={selectClass}>
                    <option value="">— بدون مسار —</option>
                    {workflows.map((w) => <option key={w.id} value={w.id}>{workflowLabel(w)}</option>)}
                  </select>
                </div>
                {editing && (
                  <div className="space-y-2 sm:col-span-2">
                    <Label className="text-xs font-bold uppercase tracking-wider">قالب المستند (اختياري)</Label>
                    <select value={form.printTemplateId} onChange={(e) => setForm({ ...form, printTemplateId: e.target.value })} className={selectClass}>
                      <option value="">— بدون مستند —</option>
                      {documents.map((d) => <option key={d.id} value={d.id}>{documentLabel(d)}</option>)}
                    </select>
                  </div>
                )}
              </div>
            </div>

            {/* Effects hint */}
            <div className="border border-border bg-secondary/30 p-3 text-xs text-muted-foreground flex items-start gap-2">
              <Zap className="h-4 w-4 text-primary shrink-0 mt-0.5" />
              <span>ما يحدث تلقائياً عند الموافقة (إنشاء إجازة/مصروف، إرسال إشعار…) يُضبط من زر <span className="text-foreground font-medium">«الإجراءات»</span> في صف النوع بعد الحفظ.</span>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDialogOpen(false)} disabled={saving}>إلغاء</Button>
            <Button onClick={save} disabled={saving} className="font-bold">{saving ? "جاري الحفظ..." : "حفظ"}</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete confirm */}
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
          requestTypeId={effectsFor.id}
          requestTypeName={effectsFor.nameAr || effectsFor.nameEn}
          requestTypeCode={effectsFor.code}
          canEdit={canWfEdit}
          onClose={() => setEffectsFor(null)}
          onChanged={reload}
        />
      )}
    </div>
  );
}
