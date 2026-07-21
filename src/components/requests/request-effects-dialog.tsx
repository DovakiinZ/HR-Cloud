"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertTriangle, Check, GripVertical, Info, Loader2, Pencil, Plus, ShieldCheck, Trash2, Zap,
} from "lucide-react";
import { toast } from "sonner";
import {
  DndContext, closestCenter, PointerSensor, useSensor, useSensors, type DragEndEvent,
} from "@dnd-kit/core";
import {
  SortableContext, verticalListSortingStrategy, useSortable, arrayMove,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Dialog, DialogContent, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Combobox } from "@/components/ui/combobox";
import { ApiError } from "@/lib/api-client";
import { getFormDefinition, FormField } from "@/lib/api/forms";
import {
  RequestTypeDetail, RequestEffectDefinition, EffectAction, EffectInput, EffectValueSource,
  EffectTriggerName, EffectConfig, EffectValueMapping, ValidationError, UpsertEffectInput,
  getRequestTypeDef, getEffectCatalog, listEffects, addEffect, updateEffect, deleteEffect,
  setEffectEnabled, reorderEffects, validateEffect, validateRequestTypeDef, setRequestTypeDefActive,
  parseEffectConfig,
} from "@/lib/api/request-types";

// ── Friendly, business-first labels (never expose class names / columns / JSON) ──────

const SOURCE_LABEL: Record<EffectValueSource, string> = {
  FormField: "حقل من النموذج",
  RequestContext: "من بيانات الطلب",
  Constant: "قيمة ثابتة",
  CurrentUser: "المستخدم الحالي",
  TenantContext: "المنشأة",
};

const CONTEXT_KEYS = ["employeeId", "leaveTypeId", "startDate", "endDate", "daysCount", "requestNumber", "requestId", "requestTypeCode"] as const;
const CONTEXT_KEY_LABEL: Record<string, string> = {
  employeeId: "موظف الطلب", leaveTypeId: "نوع الإجازة", startDate: "تاريخ البداية",
  endDate: "تاريخ النهاية", daysCount: "عدد الأيام", requestNumber: "رقم الطلب",
  requestId: "معرّف الطلب", requestTypeCode: "رمز نوع الطلب",
};

const TRIGGER_LABEL: Record<EffectTriggerName, string> = {
  FinalApproval: "عند الموافقة النهائية", Rejection: "عند الرفض", Cancellation: "عند الإلغاء",
};

const SOURCE_ORDER: EffectValueSource[] = ["FormField", "RequestContext", "Constant", "CurrentUser", "TenantContext"];
const AUTO_KEY: Partial<Record<EffectValueSource, string>> = { CurrentUser: "userId", TenantContext: "tenantId" };

/** Best-effort control for a Constant, inferred from the input key (the catalog carries no type). */
function constantKind(key: string): "date" | "number" | "email" | "boolean" | "text" {
  const k = key.toLowerCase();
  if (k.includes("date")) return "date";
  if (k.includes("amount") || k.includes("count") || k.includes("months") || k.includes("installment") || k.endsWith("days")) return "number";
  if (k.includes("email")) return "email";
  if (k.startsWith("is") || k.startsWith("affects") || k.startsWith("has")) return "boolean";
  return "text";
}

function defaultSource(input: EffectInput): EffectValueSource {
  return SOURCE_ORDER.find((s) => input.allowedSources.includes(s)) ?? input.allowedSources[0];
}
function defaultKey(input: EffectInput, source: EffectValueSource): string {
  if (AUTO_KEY[source]) return AUTO_KEY[source]!;
  if (source === "RequestContext") return (CONTEXT_KEYS as readonly string[]).includes(input.key) ? input.key : "";
  return "";
}

type Draft = { effectType: string; trigger: EffectTriggerName; config: EffectConfig };

// ════════════════════════════════════════════════════════════════════════════════════

export function RequestEffectsDialog({
  requestTypeId, requestTypeName, requestTypeCode, canEdit, onClose, onChanged,
}: {
  requestTypeId: string;
  requestTypeName: string;
  requestTypeCode: string;
  canEdit: boolean;
  onClose: () => void;
  onChanged?: () => void;
}) {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [detail, setDetail] = useState<RequestTypeDetail | null>(null);
  const [catalog, setCatalog] = useState<EffectAction[]>([]);
  const [fields, setFields] = useState<FormField[]>([]);
  const [effects, setEffects] = useState<RequestEffectDefinition[]>([]);

  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft | null>(null);
  const [draftErrors, setDraftErrors] = useState<ValidationError[]>([]);
  const [savingDraft, setSavingDraft] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);          // per-effect action key
  const [confirmActivate, setConfirmActivate] = useState(false);
  const [typeErrors, setTypeErrors] = useState<ValidationError[] | null>(null);
  const [activating, setActivating] = useState(false);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));
  const catalogByType = useMemo(() => new Map(catalog.map((c) => [c.effectType.toLowerCase(), c])), [catalog]);
  const action = draft ? catalogByType.get(draft.effectType.toLowerCase()) : undefined;
  const dirty = adding || editingId !== null;

  const load = useCallback(async () => {
    setLoading(true); setError(false);
    try {
      const [d, cat] = await Promise.all([getRequestTypeDef(requestTypeId), getEffectCatalog()]);
      setDetail(d); setCatalog(cat);
      setEffects([...d.effects].sort((a, b) => a.sequence - b.sequence));
      if (d.formDefinitionId) {
        try { setFields((await getFormDefinition(d.formDefinitionId)).fields ?? []); } catch { setFields([]); }
      }
    } catch { setError(true); }
    finally { setLoading(false); }
  }, [requestTypeId]);

  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  const refreshEffects = useCallback(async () => {
    try { setEffects((await listEffects(requestTypeId)).sort((a, b) => a.sequence - b.sequence)); onChanged?.(); }
    catch { /* keep current */ }
  }, [requestTypeId, onChanged]);

  // ── Draft (add / edit) ──────────────────────────────────────────────────────────

  const startAdd = (a: EffectAction) => {
    const config: EffectConfig = {};
    for (const inp of a.inputs) {
      const src = defaultSource(inp);
      const key = defaultKey(inp, src);
      // Pre-map the inputs that have a single obvious binding (auto sources + matching context key).
      if (AUTO_KEY[src] || key) config[inp.key] = { source: src, key };
    }
    setDraft({ effectType: a.effectType, trigger: a.supportedTriggers[0] ?? "FinalApproval", config });
    setDraftErrors([]); setAdding(true); setEditingId(null);
  };

  const startEdit = (e: RequestEffectDefinition) => {
    setDraft({ effectType: e.effectType, trigger: e.trigger, config: parseEffectConfig(e.configurationJson) });
    setDraftErrors([]); setEditingId(e.id); setAdding(false);
  };

  const cancelDraft = () => { setDraft(null); setAdding(false); setEditingId(null); setDraftErrors([]); };

  const setMapping = (inputKey: string, mapping: EffectValueMapping | null) => {
    setDraft((d) => {
      if (!d) return d;
      const config = { ...d.config };
      if (mapping === null || (mapping.source !== "CurrentUser" && mapping.source !== "TenantContext" && mapping.key === "")) {
        // an empty non-auto mapping means "not set" — drop it so an optional input stays optional.
        delete config[inputKey];
      } else {
        config[inputKey] = mapping;
      }
      return { ...d, config };
    });
  };

  const draftUpsert = (): UpsertEffectInput | null => {
    if (!draft) return null;
    const seq = editingId
      ? effects.find((e) => e.id === editingId)?.sequence ?? effects.length
      : effects.length;
    const enabled = editingId ? effects.find((e) => e.id === editingId)?.isEnabled ?? true : true;
    return { effectType: draft.effectType, trigger: draft.trigger, sequence: seq, isEnabled: enabled, config: draft.config };
  };

  const validateDraft = async () => {
    const body = draftUpsert(); if (!body) return;
    setSavingDraft(true);
    try {
      const res = await validateEffect(requestTypeId, body);
      setDraftErrors(res.errors ?? []);
      if (res.isValid) toast.success("الإعدادات صحيحة");
    } catch (e) { toast.error(e instanceof ApiError ? e.message : "تعذّر التحقق"); }
    finally { setSavingDraft(false); }
  };

  const saveDraft = async () => {
    const body = draftUpsert(); if (!body) return;
    setSavingDraft(true);
    try {
      if (editingId) { await updateEffect(editingId, body); toast.success("تم تحديث الإجراء"); }
      else { await addEffect(requestTypeId, body); toast.success("تمت إضافة الإجراء"); }
      cancelDraft();
      await refreshEffects();
    } catch (e) {
      if (e instanceof ApiError && e.errors?.length) setDraftErrors(e.errors.map((m) => ({ field: "", messageAr: m, messageEn: m })));
      toast.error(e instanceof ApiError ? e.message : "تعذّر حفظ الإجراء");
    } finally { setSavingDraft(false); }
  };

  // ── Effect row actions ────────────────────────────────────────────────────────────

  const toggleEnabled = async (e: RequestEffectDefinition) => {
    setBusy(`en:${e.id}`);
    try { await setEffectEnabled(e.id, !e.isEnabled); await refreshEffects(); }
    catch (err) { toast.error(err instanceof ApiError ? err.message : "تعذّر التغيير"); }
    finally { setBusy(null); }
  };
  const removeEffect = async (e: RequestEffectDefinition) => {
    setBusy(`del:${e.id}`);
    try { await deleteEffect(e.id); toast.success("تم حذف الإجراء"); await refreshEffects(); }
    catch (err) { toast.error(err instanceof ApiError ? err.message : "تعذّر الحذف"); }
    finally { setBusy(null); }
  };

  const onDragEnd = async (ev: DragEndEvent) => {
    const { active, over } = ev;
    if (!over || active.id === over.id) return;
    const oldIdx = effects.findIndex((e) => e.id === active.id);
    const newIdx = effects.findIndex((e) => e.id === over.id);
    if (oldIdx < 0 || newIdx < 0) return;
    const next = arrayMove(effects, oldIdx, newIdx);
    setEffects(next); // optimistic
    try { await reorderEffects(requestTypeId, next.map((e) => e.id)); onChanged?.(); }
    catch { toast.error("تعذّر إعادة الترتيب"); await refreshEffects(); }
  };

  // ── Activation / draft ──────────────────────────────────────────────────────────

  const saveAsDraft = async () => {
    setActivating(true);
    try { await setRequestTypeDefActive(requestTypeId, false); setDetail((d) => d && { ...d, isActive: false }); toast.success("تم الحفظ كمسودة"); onChanged?.(); }
    catch (e) { toast.error(e instanceof ApiError ? e.message : "تعذّر الحفظ"); }
    finally { setActivating(false); }
  };

  const runValidate = async (intendActivate: boolean) => {
    setActivating(true);
    try {
      const res = await validateRequestTypeDef(requestTypeId);
      setTypeErrors(res.errors ?? []);
      if (res.isValid) {
        if (intendActivate) { setConfirmActivate(true); }
        else toast.success("الإعدادات صحيحة — جاهزة للتفعيل");
      } else {
        setConfirmActivate(false);
        toast.error("توجد أخطاء تمنع التفعيل");
      }
    } catch (e) { toast.error(e instanceof ApiError ? e.message : "تعذّر التحقق"); }
    finally { setActivating(false); }
  };

  const doActivate = async () => {
    setActivating(true);
    try {
      await setRequestTypeDefActive(requestTypeId, true);
      setDetail((d) => d && { ...d, isActive: true });
      setConfirmActivate(false); setTypeErrors(null);
      toast.success("تم تفعيل نوع الطلب");
      onChanged?.();
    } catch (e) {
      if (e instanceof ApiError && e.errors?.length) setTypeErrors(e.errors.map((m) => ({ field: "", messageAr: m, messageEn: m })));
      toast.error(e instanceof ApiError ? e.message : "تعذّر التفعيل");
    } finally { setActivating(false); }
  };

  const requestClose = () => {
    if (dirty && !window.confirm("لديك إجراء قيد التعديل لم يُحفظ. إغلاق النافذة سيتجاهله. متابعة؟")) return;
    onClose();
  };

  // ── Render ────────────────────────────────────────────────────────────────────────

  return (
    <Dialog open onOpenChange={(o) => { if (!o) requestClose(); }}>
      <DialogContent className="sm:max-w-3xl" dir="rtl" showCloseButton={false}>
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Zap className="h-5 w-5 text-primary" /> الإجراءات بعد الموافقة
            <span className="text-sm font-normal text-muted-foreground">— {requestTypeName}</span>
            <span className="font-mono text-[10px] text-muted-foreground/70">{requestTypeCode}</span>
            {detail && (detail.isActive
              ? <Badge variant="outline" className="ms-auto bg-green-500/10 text-green-500 border-green-500/20">نشط</Badge>
              : <Badge variant="outline" className="ms-auto bg-amber-500/10 text-amber-500 border-amber-500/20">مسودة</Badge>)}
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-1 max-h-[68vh] overflow-y-auto pl-1">
          <p className="text-xs text-muted-foreground">اختر ما يحدث تلقائياً عند اعتماد الطلب — مثل إنشاء إجازة أو مصروف أو إرسال إشعار — واربط مدخلات كل إجراء بحقول النموذج أو بيانات الطلب.</p>

          {loading ? (
            <div className="flex items-center justify-center py-16"><Loader2 className="h-7 w-7 animate-spin text-muted-foreground" /></div>
          ) : error ? (
            <div className="border border-border bg-card p-8 text-center text-sm text-muted-foreground">
              تعذّر تحميل الإجراءات. <button onClick={load} className="underline">إعادة المحاولة</button>
            </div>
          ) : (
            <>
              {/* Summary bar */}
              {detail && (
                <div className="grid grid-cols-3 gap-2 border border-border bg-secondary/30 p-3 text-xs">
                  <div><span className="text-muted-foreground">النموذج: </span>{detail.hasForm ? "مرتبط" : <span className="text-amber-500">غير مرتبط</span>}</div>
                  <div><span className="text-muted-foreground">مسار الموافقة: </span>{detail.hasWorkflow ? "مرتبط" : <span className="text-amber-500">غير مرتبط</span>}</div>
                  <div><span className="text-muted-foreground">عدد الإجراءات: </span>{effects.length}</div>
                </div>
              )}

              {/* Effect list */}
              {effects.length === 0 && !adding ? (
                <div className="border border-dashed border-border bg-card p-8 text-center text-sm text-muted-foreground">
                  لا توجد إجراءات بعد. أضف أول إجراء ليُنفَّذ تلقائياً عند اعتماد الطلب.
                </div>
              ) : (
                <DndContext sensors={sensors} collisionDetection={closestCenter} onDragEnd={onDragEnd}>
                  <SortableContext items={effects.map((e) => e.id)} strategy={verticalListSortingStrategy}>
                    <div className="space-y-2">
                      {effects.map((e, i) => (
                        <EffectRow
                          key={e.id} effect={e} order={i + 1} canEdit={canEdit} busy={busy}
                          action={catalogByType.get(e.effectType.toLowerCase())}
                          isEditing={editingId === e.id}
                          onEdit={() => startEdit(e)} onToggle={() => toggleEnabled(e)} onDelete={() => removeEffect(e)}
                        />
                      ))}
                    </div>
                  </SortableContext>
                </DndContext>
              )}

              {/* Draft editor */}
              {draft && action ? (
                <DraftEditor
                  action={action} draft={draft} fields={fields} errors={draftErrors} saving={savingDraft}
                  editing={editingId !== null}
                  onTrigger={(t) => setDraft((d) => d && { ...d, trigger: t })}
                  onMapping={setMapping}
                  onValidate={validateDraft} onSave={saveDraft} onCancel={cancelDraft}
                />
              ) : adding ? (
                <CatalogPicker catalog={catalog} onPick={startAdd} onCancel={() => setAdding(false)} />
              ) : canEdit ? (
                <Button variant="outline" onClick={() => { setAdding(true); }} className="gap-2 w-full">
                  <Plus className="h-4 w-4" /> إضافة إجراء
                </Button>
              ) : null}

              {/* Type-level validation errors */}
              {typeErrors && typeErrors.length > 0 && (
                <div className="border border-destructive/40 bg-destructive/10 p-3 text-xs space-y-1">
                  <div className="flex items-center gap-1.5 font-medium text-destructive"><AlertTriangle className="h-3.5 w-3.5" /> يجب معالجة ما يلي قبل التفعيل:</div>
                  {typeErrors.map((er, idx) => <div key={idx} className="text-destructive/90">• {er.messageAr}</div>)}
                </div>
              )}

              {/* Activation confirmation summary */}
              {confirmActivate && detail && (
                <div className="border border-primary/40 bg-primary/5 p-3 text-sm space-y-2">
                  <div className="flex items-center gap-1.5 font-medium"><ShieldCheck className="h-4 w-4 text-primary" /> تأكيد التفعيل</div>
                  <div className="text-xs text-muted-foreground">سيصبح نوع الطلب متاحاً للموظفين، وستُنفَّذ الإجراءات التالية بالترتيب عند الاعتماد النهائي:</div>
                  <ol className="text-xs list-decimal ps-5 space-y-0.5">
                    {effects.filter((e) => e.isEnabled).map((e) => (
                      <li key={e.id}>{catalogByType.get(e.effectType.toLowerCase())?.labelAr ?? e.labelAr ?? e.effectType}</li>
                    ))}
                  </ol>
                  <div className="flex gap-2 pt-1">
                    <Button size="sm" onClick={doActivate} disabled={activating} className="gap-1.5">{activating ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />} تأكيد التفعيل</Button>
                    <Button size="sm" variant="outline" onClick={() => setConfirmActivate(false)}>رجوع</Button>
                  </div>
                </div>
              )}
            </>
          )}
        </div>

        {/* Footer actions */}
        <div className="flex flex-wrap items-center justify-between gap-2 border-t border-border pt-3">
          <Button variant="ghost" onClick={requestClose}>إغلاق</Button>
          {canEdit && detail && !confirmActivate && (
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" onClick={saveAsDraft} disabled={activating || dirty}>حفظ كمسودة</Button>
              <Button variant="outline" onClick={() => runValidate(false)} disabled={activating || dirty} className="gap-1.5">
                {activating ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShieldCheck className="h-4 w-4" />} تحقق من الإعدادات
              </Button>
              <Button onClick={() => runValidate(true)} disabled={activating || dirty} className="gap-1.5 font-bold">
                <Zap className="h-4 w-4" /> تفعيل نوع الطلب
              </Button>
            </div>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

// ── One effect row (sortable) ────────────────────────────────────────────────────────

function EffectRow({
  effect, order, action, canEdit, busy, isEditing, onEdit, onToggle, onDelete,
}: {
  effect: RequestEffectDefinition; order: number; action?: EffectAction; canEdit: boolean;
  busy: string | null; isEditing: boolean;
  onEdit: () => void; onToggle: () => void; onDelete: () => void;
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: effect.id });
  const style = { transform: CSS.Transform.toString(transform), transition, opacity: isDragging ? 0.5 : 1 };
  const label = action?.labelAr ?? effect.labelAr;
  const unknown = !action || !effect.executorAvailable;

  return (
    <div ref={setNodeRef} style={style}
      className={`flex items-center gap-2 border bg-card p-2.5 ${isEditing ? "border-primary" : "border-border"} ${!effect.isEnabled ? "opacity-60" : ""}`}>
      {canEdit && <button {...attributes} {...listeners} className="cursor-grab text-muted-foreground hover:text-foreground" title="اسحب لإعادة الترتيب"><GripVertical className="h-4 w-4" /></button>}
      <span className="flex h-6 w-6 items-center justify-center bg-secondary text-xs shrink-0">{order}</span>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5 flex-wrap">
          <span className="font-medium text-sm truncate" title={action?.descriptionAr}>{label ?? "إجراء غير معروف"}</span>
          {unknown && <Badge variant="outline" className="bg-amber-500/10 text-amber-600 border-amber-500/20" title="لم يعد هذا الإجراء متاحاً — يحتاج مراجعة"><AlertTriangle className="h-3 w-3" /> يحتاج مراجعة</Badge>}
          {effect.isRequired && <Badge variant="outline" className="bg-blue-500/10 text-blue-500 border-blue-500/20">إلزامي</Badge>}
          <Badge variant="outline" className="text-[10px]">{TRIGGER_LABEL[effect.trigger]}</Badge>
          {effect.executionMode === "Asynchronous" && <Badge variant="outline" className="text-[10px] text-muted-foreground" title="يُنفَّذ لاحقاً؛ لا يؤثر فشله على الطلب">غير متزامن</Badge>}
        </div>
        {action?.descriptionAr && <div className="text-[11px] text-muted-foreground truncate">{action.descriptionAr}</div>}
      </div>
      {/* enabled toggle */}
      <button
        onClick={onToggle}
        disabled={!canEdit || !effect.canDisable || busy === `en:${effect.id}`}
        title={effect.canDisable ? (effect.isEnabled ? "تعطيل" : "تفعيل") : "إجراء إلزامي — لا يمكن تعطيله"}
        className={`relative h-5 w-9 rounded-full transition-colors disabled:opacity-50 ${effect.isEnabled ? "bg-primary" : "bg-muted"}`}>
        <span className={`absolute top-0.5 h-4 w-4 rounded-full bg-white transition-all ${effect.isEnabled ? "left-0.5" : "right-0.5"}`} />
      </button>
      {canEdit && <button onClick={onEdit} className="text-muted-foreground hover:text-foreground" title="تعديل"><Pencil className="h-4 w-4" /></button>}
      {canEdit && (effect.canDelete
        ? <button onClick={onDelete} disabled={busy === `del:${effect.id}`} className="text-muted-foreground hover:text-destructive disabled:opacity-50" title="حذف">{busy === `del:${effect.id}` ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}</button>
        : <span className="text-muted-foreground/40" title="إجراء إلزامي — لا يمكن حذفه"><Trash2 className="h-4 w-4" /></span>)}
    </div>
  );
}

// ── Catalog picker (add an effect) ───────────────────────────────────────────────────

function CatalogPicker({ catalog, onPick, onCancel }: { catalog: EffectAction[]; onPick: (a: EffectAction) => void; onCancel: () => void }) {
  const byModule = useMemo(() => {
    const m = new Map<string, EffectAction[]>();
    for (const a of catalog) { const arr = m.get(a.module) ?? []; arr.push(a); m.set(a.module, arr); }
    return [...m.entries()];
  }, [catalog]);

  return (
    <div className="border border-primary/40 bg-card p-3 space-y-3">
      <div className="flex items-center justify-between">
        <div className="text-sm font-medium">اختر إجراءً</div>
        <button onClick={onCancel} className="text-xs text-muted-foreground underline">إلغاء</button>
      </div>
      <div className="space-y-3">
        {byModule.map(([mod, actions]) => (
          <div key={mod} className="space-y-1.5">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-1.5">
              {actions.map((a) => (
                <button key={a.effectType} disabled={!a.executorAvailable} onClick={() => onPick(a)}
                  title={a.executorAvailable ? a.descriptionAr : "هذا الإجراء غير متاح حالياً"}
                  className="flex flex-col items-start gap-0.5 border border-border p-2.5 text-right hover:border-primary hover:bg-secondary/40 disabled:opacity-40 disabled:cursor-not-allowed">
                  <span className="text-sm font-medium flex items-center gap-1"><Zap className="h-3.5 w-3.5 text-primary" /> {a.labelAr}</span>
                  <span className="text-[11px] text-muted-foreground line-clamp-2">{a.descriptionAr}</span>
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── Draft editor (configure inputs of a chosen action) ──────────────────────────────

function DraftEditor({
  action, draft, fields, errors, saving, editing, onTrigger, onMapping, onValidate, onSave, onCancel,
}: {
  action: EffectAction; draft: Draft; fields: FormField[]; errors: ValidationError[]; saving: boolean; editing: boolean;
  onTrigger: (t: EffectTriggerName) => void;
  onMapping: (inputKey: string, mapping: EffectValueMapping | null) => void;
  onValidate: () => void; onSave: () => void; onCancel: () => void;
}) {
  const fieldOptions = useMemo(() => fields.map((f) => ({ value: f.code, label: f.nameAr || f.nameEn || f.code, hint: f.fieldType })), [fields]);
  const errorFor = (inputKey: string) => errors.find((e) => e.field?.toLowerCase() === inputKey.toLowerCase());
  const generalErrors = errors.filter((e) => !e.field || !action.inputs.some((i) => i.key.toLowerCase() === e.field.toLowerCase()));

  return (
    <div className="border border-primary bg-card p-3 space-y-3">
      <div className="flex items-center gap-2">
        <Zap className="h-4 w-4 text-primary" />
        <span className="font-medium text-sm">{editing ? "تعديل إجراء" : "إعداد إجراء"} — {action.labelAr}</span>
        <Badge variant="outline" className="text-[10px] text-muted-foreground ms-auto" title="طريقة التنفيذ يحددها الإجراء">
          {action.executionMode === "Asynchronous" ? "غير متزامن (لاحقاً)" : "متزامن"}
        </Badge>
      </div>
      <p className="text-[11px] text-muted-foreground flex items-start gap-1"><Info className="h-3 w-3 mt-0.5 shrink-0" /> {action.descriptionAr}</p>

      {/* Trigger (only when the action supports more than one) */}
      {action.supportedTriggers.length > 1 && (
        <div className="space-y-1">
          <Label className="text-xs">متى يُنفَّذ؟</Label>
          <select value={draft.trigger} onChange={(e) => onTrigger(e.target.value as EffectTriggerName)}
            className="w-full h-9 bg-secondary border border-border px-3 text-sm">
            {action.supportedTriggers.map((t) => <option key={t} value={t}>{TRIGGER_LABEL[t]}</option>)}
          </select>
        </div>
      )}

      {/* Inputs */}
      <div className="space-y-2.5">
        {action.inputs.map((inp) => (
          <InputMappingRow key={inp.key} input={inp} value={draft.config[inp.key]} fieldOptions={fieldOptions}
            error={errorFor(inp.key)} onChange={(m) => onMapping(inp.key, m)} />
        ))}
      </div>

      {generalErrors.length > 0 && (
        <div className="border border-destructive/40 bg-destructive/10 p-2 text-xs space-y-0.5">
          {generalErrors.map((e, i) => <div key={i} className="text-destructive/90">• {e.messageAr}</div>)}
        </div>
      )}

      <div className="flex gap-2 pt-1">
        <Button size="sm" onClick={onSave} disabled={saving} className="gap-1.5 font-bold">{saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />} حفظ الإجراء</Button>
        <Button size="sm" variant="outline" onClick={onValidate} disabled={saving}>تحقق من الإعدادات</Button>
        <Button size="sm" variant="ghost" onClick={onCancel} disabled={saving}>إلغاء</Button>
      </div>
    </div>
  );
}

// ── One input → its source + value control ──────────────────────────────────────────

function InputMappingRow({
  input, value, fieldOptions, error, onChange,
}: {
  input: EffectInput; value?: EffectValueMapping;
  fieldOptions: { value: string; label: string; hint?: string }[];
  error?: ValidationError; onChange: (m: EffectValueMapping | null) => void;
}) {
  const source = value?.source ?? defaultSource(input);
  const setSource = (s: EffectValueSource) => onChange({ source: s, key: AUTO_KEY[s] ?? defaultKey(input, s) });
  const setKey = (k: string) => onChange({ source, key: k });

  return (
    <div className="grid grid-cols-1 sm:grid-cols-[9rem_1fr] gap-2 items-start">
      <Label className="text-xs pt-2">
        {input.labelAr}{input.isRequired && <span className="text-destructive"> *</span>}
      </Label>
      <div className="space-y-1">
        <div className="flex gap-2">
          {/* source selector — only sources the backend allows for this input */}
          <select value={source} onChange={(e) => setSource(e.target.value as EffectValueSource)}
            className="h-9 bg-secondary border border-border px-2 text-sm shrink-0 w-36">
            {input.allowedSources.map((s) => <option key={s} value={s}>{SOURCE_LABEL[s]}</option>)}
          </select>

          {/* value control depends on the source */}
          <div className="flex-1 min-w-0">
            {source === "FormField" ? (
              <Combobox value={value?.key || null} onChange={(v) => setKey(v ?? "")} options={fieldOptions}
                placeholder="اختر حقلاً من النموذج…" emptyText={fieldOptions.length === 0 ? "لا توجد حقول في النموذج" : "لا نتائج"} />
            ) : source === "RequestContext" ? (
              <select value={value?.key || ""} onChange={(e) => setKey(e.target.value)} className="w-full h-9 bg-secondary border border-border px-2 text-sm">
                <option value="">— اختر —</option>
                {CONTEXT_KEYS.map((k) => <option key={k} value={k}>{CONTEXT_KEY_LABEL[k]}</option>)}
              </select>
            ) : source === "Constant" ? (
              <ConstantControl inputKey={input.key} value={value?.key ?? ""} onChange={setKey} />
            ) : (
              <div className="h-9 flex items-center px-2 text-sm text-muted-foreground bg-secondary/40 border border-border">
                {source === "CurrentUser" ? "المستخدم الذي اعتمد الطلب" : "المنشأة الحالية"}
              </div>
            )}
          </div>
        </div>
        {error && <div className="text-[11px] text-destructive">{error.messageAr}</div>}
      </div>
    </div>
  );
}

function ConstantControl({ inputKey, value, onChange }: { inputKey: string; value: string; onChange: (v: string) => void }) {
  const kind = constantKind(inputKey);
  if (kind === "boolean") {
    const on = value === "true";
    return (
      <button type="button" onClick={() => onChange(on ? "false" : "true")}
        className={`relative h-9 w-16 rounded-full border border-border transition-colors ${on ? "bg-primary" : "bg-secondary"}`}>
        <span className={`absolute top-1 h-6 w-6 rounded-full bg-white transition-all ${on ? "left-1" : "right-1"}`} />
        <span className="sr-only">{on ? "نعم" : "لا"}</span>
      </button>
    );
  }
  const type = kind === "date" ? "date" : kind === "number" ? "number" : kind === "email" ? "email" : "text";
  return <Input type={type} value={value} onChange={(e) => onChange(e.target.value)} className="bg-secondary border-border" placeholder="القيمة الثابتة" />;
}
