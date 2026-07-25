"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Lock, Pencil, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/lib/api-client";
import {
  FormField, FormDefinition, FieldClassification,
  FIELD_TYPES, FIELD_TYPE_LABEL,
  getFormDefinition,
  addFormField, updateFormField, deleteFormField,
  AddFormFieldInput, UpdateFormFieldInput,
} from "@/lib/api/forms";

// ── Classification badge ─────────────────────────────────────────────────────────────

const CLASS_BADGE: Record<FieldClassification, { label: string; cls: string } | null> = {
  SystemRequired: {
    label: "حقل نظامي مطلوب",   // "System-required"
    cls: "bg-red-500/10 text-red-600 border-red-500/20",
  },
  BusinessRequired: null,
  Optional: null,
  Custom: null,
};

function ClassBadge({ classification }: { classification: FieldClassification }) {
  const def = CLASS_BADGE[classification];
  if (!def) return null;
  return (
    <Badge variant="outline" className={`text-[10px] ${def.cls}`} title="System-required">
      <Lock className="h-2.5 w-2.5 me-1" /> {def.label}
    </Badge>
  );
}

// ── Whether a field is system-locked (delete + required toggle disabled) ─────────────

function isLocked(f: FormField) {
  return f.classification === "SystemRequired";
}

// ── Field form state ─────────────────────────────────────────────────────────────────

interface FieldDraft {
  nameAr: string;
  nameEn: string;
  code: string;
  fieldType: string;
  isRequired: boolean;
  sectionName: string;
  placeholder: string;
}

const emptyDraft: FieldDraft = {
  nameAr: "", nameEn: "", code: "", fieldType: "Text",
  isRequired: false, sectionName: "", placeholder: "",
};

function toDraft(f: FormField): FieldDraft {
  return {
    nameAr: f.nameAr, nameEn: f.nameEn, code: f.code,
    fieldType: f.fieldType, isRequired: f.isRequired,
    sectionName: f.sectionName ?? "", placeholder: f.placeholder ?? "",
  };
}

// ── Main component ───────────────────────────────────────────────────────────────────

interface Props {
  formId: string;
  canEdit: boolean;
  onClose?: () => void;
  onChanged?: () => void;
}

export function FormFieldEditor({ formId, canEdit, onClose, onChanged }: Props) {
  const [form, setForm] = useState<FormDefinition | null>(null);
  const [loading, setLoading] = useState(true);
  const [editingField, setEditingField] = useState<FormField | "new" | null>(null);
  const [draft, setDraft] = useState<FieldDraft>(emptyDraft);
  const [saving, setSaving] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<FormField | null>(null);
  const [deleting, setDeleting] = useState(false);

  const reload = useCallback(async () => {
    try {
      setLoading(true);
      setForm(await getFormDefinition(formId));
    } catch {
      toast.error("تعذر تحميل حقول النموذج");
    } finally {
      setLoading(false);
    }
  }, [formId]);

  useEffect(() => { reload(); }, [reload]);

  function openAdd() {
    setDraft(emptyDraft);
    setEditingField("new");
  }

  function openEdit(f: FormField) {
    setDraft(toDraft(f));
    setEditingField(f);
  }

  function closeDialog() {
    setEditingField(null);
  }

  async function save() {
    if (!draft.nameAr.trim()) { toast.error("الاسم بالعربية مطلوب"); return; }
    if (editingField === "new" && !draft.code.trim()) { toast.error("المُعرّف مطلوب"); return; }

    setSaving(true);
    try {
      const fields = form?.fields ?? [];
      const nextOrder = Math.max(0, ...fields.map((f) => f.sortOrder)) + 1;

      if (editingField === "new") {
        const input: AddFormFieldInput = {
          code: draft.code.trim().toUpperCase(),
          nameAr: draft.nameAr.trim(),
          nameEn: draft.nameEn.trim() || draft.nameAr.trim(),
          fieldType: draft.fieldType,
          isRequired: draft.isRequired,
          sortOrder: nextOrder,
          sectionName: draft.sectionName.trim() || null,
          placeholder: draft.placeholder.trim() || null,
        };
        await addFormField(formId, input);
        toast.success("تمت إضافة الحقل");
      } else if (editingField) {
        const isCustom = editingField.classification === "Custom";
        const input: UpdateFormFieldInput = {
          // Only pass code for Custom fields (server guards the rest).
          ...(isCustom ? { code: draft.code.trim() || editingField.code } : {}),
          nameAr: draft.nameAr.trim(),
          nameEn: draft.nameEn.trim() || draft.nameAr.trim(),
          fieldType: draft.fieldType,
          isRequired: draft.isRequired,
          sortOrder: editingField.sortOrder,
          sectionName: draft.sectionName.trim() || null,
          placeholder: draft.placeholder.trim() || null,
        };
        await updateFormField(formId, editingField.id, input);
        toast.success("تم تحديث الحقل");
      }
      closeDialog();
      await reload();
      onChanged?.();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "تعذر حفظ الحقل");
    } finally {
      setSaving(false);
    }
  }

  async function confirmDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await deleteFormField(formId, deleteTarget.id);
      toast.success("تم حذف الحقل");
      setDeleteTarget(null);
      await reload();
      onChanged?.();
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "تعذر الحذف");
    } finally {
      setDeleting(false);
    }
  }

  const selectCls = "w-full h-9 bg-secondary border border-border px-3 text-sm text-foreground disabled:opacity-60";

  return (
    <div className="space-y-4" dir="rtl">
      <div className="flex items-center justify-between gap-3">
        <div className="text-sm font-medium">
          {form ? (form.nameAr || form.nameEn) : "حقول النموذج"}
          {form && (
            <span className="ms-2 font-mono text-[10px] text-muted-foreground">{form.code}</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          {canEdit && (
            <Button size="sm" onClick={openAdd} className="gap-1.5 h-8 text-xs">
              <Plus className="h-3.5 w-3.5" /> حقل جديد
            </Button>
          )}
          {onClose && (
            <Button size="sm" variant="outline" onClick={onClose} className="h-8 text-xs">إغلاق</Button>
          )}
        </div>
      </div>

      {loading ? (
        <div className="flex items-center justify-center py-8">
          <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
        </div>
      ) : !form || form.fields.length === 0 ? (
        <div className="border border-dashed border-border p-8 text-center text-sm text-muted-foreground">
          لا توجد حقول — أضف أول حقل للنموذج.
        </div>
      ) : (
        <div className="divide-y divide-border border border-border">
          {[...form.fields].sort((a, b) => a.sortOrder - b.sortOrder).map((f) => {
            const locked = isLocked(f);
            return (
              <div key={f.id} className="flex items-center gap-2 px-3 py-2.5 bg-card hover:bg-secondary/20">
                {/* Field info */}
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-1.5">
                    <span className="font-medium text-sm">{f.nameAr || f.nameEn}</span>
                    <ClassBadge classification={f.classification} />
                    {f.isRequired && !locked && (
                      <Badge variant="outline" className="text-[10px] bg-blue-500/10 text-blue-500 border-blue-500/20">إلزامي</Badge>
                    )}
                    <Badge variant="outline" className="text-[10px] text-muted-foreground">
                      {FIELD_TYPE_LABEL[f.fieldType] ?? f.fieldType}
                    </Badge>
                  </div>
                  {/* Internal key — shown as read-only hint */}
                  <div className="font-mono text-[10px] text-muted-foreground mt-0.5">{f.code}</div>
                </div>

                {/* Actions */}
                {canEdit && (
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      onClick={() => openEdit(f)}
                      className="h-7 w-7 inline-flex items-center justify-center text-muted-foreground hover:text-foreground"
                      title="تعديل"
                    >
                      <Pencil className="h-3.5 w-3.5" />
                    </button>
                    {locked ? (
                      <span
                        className="h-7 w-7 inline-flex items-center justify-center text-muted-foreground/40"
                        title="حقل نظامي — لا يُحذف"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </span>
                    ) : (
                      <button
                        onClick={() => setDeleteTarget(f)}
                        className="h-7 w-7 inline-flex items-center justify-center text-muted-foreground hover:text-destructive"
                        title="حذف"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Add / Edit dialog */}
      <Dialog open={editingField !== null} onOpenChange={(o) => { if (!o && !saving) closeDialog(); }}>
        <DialogContent className="sm:max-w-lg" dir="rtl">
          <DialogHeader>
            <DialogTitle>
              {editingField === "new" ? "إضافة حقل" : "تعديل حقل"}
              {editingField !== "new" && editingField && (
                <ClassBadge classification={editingField.classification} />
              )}
            </DialogTitle>
          </DialogHeader>

          <div className="space-y-4 py-1">
            {/* Label inputs — always editable */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label className="text-xs font-bold uppercase tracking-wider">الاسم (عربي) <span className="text-destructive">*</span></Label>
                <Input
                  value={draft.nameAr}
                  onChange={(e) => setDraft((d) => ({ ...d, nameAr: e.target.value }))}
                  className="bg-secondary border-border"
                  dir="rtl"
                />
              </div>
              <div className="space-y-1.5">
                <Label className="text-xs font-bold uppercase tracking-wider">الاسم (إنجليزي)</Label>
                <Input
                  value={draft.nameEn}
                  onChange={(e) => setDraft((d) => ({ ...d, nameEn: e.target.value }))}
                  className="bg-secondary border-border"
                  dir="ltr"
                />
              </div>
            </div>

            {/* Internal key — read-only for non-Custom; editable only for new/Custom */}
            <div className="space-y-1.5">
              <Label className="text-xs font-bold uppercase tracking-wider">
                المُعرّف الداخلي (Code)
                {editingField !== "new" && editingField?.classification !== "Custom" && (
                  <span className="ms-2 text-[10px] font-normal text-muted-foreground normal-case tracking-normal">
                    — للقراءة فقط (غير مخصص)
                  </span>
                )}
                {editingField === "new" && <span className="text-destructive ms-1">*</span>}
              </Label>
              <Input
                value={draft.code}
                onChange={(e) => setDraft((d) => ({ ...d, code: e.target.value }))}
                className="bg-secondary border-border font-mono"
                dir="ltr"
                placeholder="FIELD_CODE"
                // Read-only for existing non-Custom fields
                readOnly={editingField !== "new" && editingField?.classification !== "Custom"}
                disabled={editingField !== "new" && editingField?.classification !== "Custom"}
              />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {/* Field type */}
              <div className="space-y-1.5">
                <Label className="text-xs font-bold uppercase tracking-wider">نوع الحقل</Label>
                <select
                  value={draft.fieldType}
                  onChange={(e) => setDraft((d) => ({ ...d, fieldType: e.target.value }))}
                  className={selectCls}
                >
                  {FIELD_TYPES.map((t) => (
                    <option key={t} value={t}>{FIELD_TYPE_LABEL[t] ?? t}</option>
                  ))}
                </select>
              </div>

              {/* Required toggle — disabled for SystemRequired */}
              <div className="space-y-1.5">
                <Label className="text-xs font-bold uppercase tracking-wider">إلزامي</Label>
                <div className="flex items-center h-9 gap-2">
                  <button
                    type="button"
                    role="switch"
                    aria-checked={draft.isRequired}
                    disabled={editingField !== "new" && isLocked(editingField as FormField)}
                    onClick={() => setDraft((d) => ({ ...d, isRequired: !d.isRequired }))}
                    title={
                      editingField !== "new" && isLocked(editingField as FormField)
                        ? "حقل نظامي مطلوب — لا يمكن تغيير هذا الإعداد"
                        : undefined
                    }
                    className={`relative h-5 w-9 rounded-full transition-colors disabled:opacity-40 disabled:cursor-not-allowed ${
                      draft.isRequired ? "bg-primary" : "bg-muted"
                    }`}
                  >
                    <span
                      className={`absolute top-0.5 h-4 w-4 rounded-full bg-white transition-all ${
                        draft.isRequired ? "left-0.5" : "right-0.5"
                      }`}
                    />
                  </button>
                  <span className="text-sm text-muted-foreground">
                    {draft.isRequired ? "نعم" : "لا"}
                  </span>
                  {editingField !== "new" && isLocked(editingField as FormField) && (
                    <span title="مقفل — حقل نظامي">
                      <Lock className="h-3.5 w-3.5 text-muted-foreground/60" />
                    </span>
                  )}
                </div>
              </div>
            </div>

            {/* Section / placeholder — always editable */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label className="text-xs font-bold uppercase tracking-wider">القسم (اختياري)</Label>
                <Input
                  value={draft.sectionName}
                  onChange={(e) => setDraft((d) => ({ ...d, sectionName: e.target.value }))}
                  className="bg-secondary border-border"
                  placeholder="معلومات الإجازة"
                />
              </div>
              <div className="space-y-1.5">
                <Label className="text-xs font-bold uppercase tracking-wider">النص التوضيحي (Placeholder)</Label>
                <Input
                  value={draft.placeholder}
                  onChange={(e) => setDraft((d) => ({ ...d, placeholder: e.target.value }))}
                  className="bg-secondary border-border"
                />
              </div>
            </div>
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={closeDialog} disabled={saving}>إلغاء</Button>
            <Button onClick={save} disabled={saving} className="font-bold">
              {saving ? <><Loader2 className="h-4 w-4 animate-spin me-1" /> جاري الحفظ...</> : "حفظ"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* Delete confirm */}
      <Dialog open={!!deleteTarget} onOpenChange={(o) => { if (!o && !deleting) setDeleteTarget(null); }}>
        <DialogContent dir="rtl">
          <DialogHeader>
            <DialogTitle>حذف الحقل</DialogTitle>
          </DialogHeader>
          <p className="text-sm text-muted-foreground">
            هل أنت متأكد من حذف الحقل <span className="font-bold text-foreground">{deleteTarget?.nameAr || deleteTarget?.nameEn}</span>؟
          </p>
          <DialogFooter>
            <Button variant="outline" onClick={() => setDeleteTarget(null)} disabled={deleting}>إلغاء</Button>
            <Button onClick={confirmDelete} disabled={deleting} className="bg-destructive text-white hover:bg-destructive/90">
              {deleting ? <Loader2 className="h-4 w-4 animate-spin me-1" /> : null} حذف
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
