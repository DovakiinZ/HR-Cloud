"use client";

import { createElement, useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertTriangle, ArrowDown, ArrowUp, Check, Loader2, Lock, Plus, Shield, Trash2, X,
} from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { FormDefinition, FormField, getFormDefinition, getFormDefinitions } from "@/lib/api/forms";
import { getWorkflowDefinitions, WorkflowDefinition } from "@/lib/api/workflows";
import { getLookup, LookupItem } from "@/lib/api/lookups";
import { REQUEST_COLORS, REQUEST_ICON_KEYS, requestIcon } from "@/lib/request-icons";
import { EffectInputMapper } from "./effect-input-mapper";
import {
  EffectActionDescriptor, EffectConfiguration, RequestEffectDefinition, RequestTypeDetail,
  ValidationResult, addEffect, createRequestType, deleteEffect, getEffectCatalog,
  getRequestType, parseConfiguration, reorderEffects, serializeConfiguration,
  setEffectEnabled, setRequestTypeActive, updateEffect, updateRequestType, validateRequestType,
} from "@/lib/api/request-types";

const STEPS = [
  "البيانات الأساسية",
  "النموذج",
  "مسار الموافقة",
  "الإجراءات بعد الموافقة",
  "المراجعة والتفعيل",
] as const;

function notifyError(err: unknown, fallback: string) {
  toast.error(err instanceof ApiError ? err.message : fallback);
}

/**
 * The request-type builder.
 *
 * Creation happens at the end of step 1 rather than at the end of the wizard: effects, validation
 * and activation all address a persisted request type by id, and the server is the only thing that
 * can tell you whether a configuration is valid. A request type is created inactive, so a
 * half-finished one is invisible to employees until it passes validation.
 */
export function RequestBuilder({ requestTypeId, onSaved }: { requestTypeId?: string; onSaved?: (id: string) => void }) {
  const [step, setStep] = useState(0);
  const [id, setId] = useState<string | undefined>(requestTypeId);
  const [detail, setDetail] = useState<RequestTypeDetail | null>(null);
  const [loading, setLoading] = useState(Boolean(requestTypeId));
  const [saving, setSaving] = useState(false);

  const [forms, setForms] = useState<FormDefinition[]>([]);
  const [workflows, setWorkflows] = useState<WorkflowDefinition[]>([]);
  const [categories, setCategories] = useState<LookupItem[]>([]);
  const [catalog, setCatalog] = useState<EffectActionDescriptor[]>([]);
  const [formFields, setFormFields] = useState<FormField[]>([]);

  const [basics, setBasics] = useState({
    code: "", nameAr: "", nameEn: "", descriptionAr: "", categoryId: "",
    icon: REQUEST_ICON_KEYS[0], color: REQUEST_COLORS[0],
  });
  const [formId, setFormId] = useState("");
  const [workflowId, setWorkflowId] = useState("");
  const [validation, setValidation] = useState<ValidationResult | null>(null);
  const [validating, setValidating] = useState(false);

  const isSystem = detail?.isSystem ?? false;

  // ── Load ──────────────────────────────────────────────────────────────────

  const loadDetail = useCallback(async (typeId: string) => {
    const d = await getRequestType(typeId);
    setDetail(d);
    setBasics({
      code: d.code, nameAr: d.nameAr, nameEn: d.nameEn,
      descriptionAr: d.descriptionAr ?? "", categoryId: d.categoryId ?? "",
      icon: d.icon ?? REQUEST_ICON_KEYS[0], color: d.color ?? REQUEST_COLORS[0],
    });
    setFormId(d.formDefinitionId);
    setWorkflowId(d.workflowDefinitionId ?? "");
    return d;
  }, []);

  useEffect(() => {
    queueMicrotask(async () => {
      try {
        // Reference data in parallel; a failure in any one must not blank the whole builder, so each
        // settles independently.
        const [f, w, c, cat] = await Promise.allSettled([
          getFormDefinitions(), getWorkflowDefinitions(), getLookup("request-categories"), getEffectCatalog(),
        ]);
        if (f.status === "fulfilled") setForms(f.value);
        if (w.status === "fulfilled") setWorkflows(w.value);
        if (c.status === "fulfilled") setCategories(c.value);
        if (cat.status === "fulfilled") setCatalog(cat.value);
        if (requestTypeId) await loadDetail(requestTypeId);
      } catch (e) {
        notifyError(e, "تعذر تحميل البيانات");
      } finally {
        setLoading(false);
      }
    });
  }, [requestTypeId, loadDetail]);

  // The FormField source's vocabulary comes from the linked form, so it reloads when that changes.
  useEffect(() => {
    // The clear-on-empty path goes through the microtask too, so nothing sets state synchronously
    // in the effect body.
    queueMicrotask(async () => {
      if (!formId) { setFormFields([]); return; }
      try { setFormFields((await getFormDefinition(formId)).fields ?? []); }
      catch { setFormFields([]); }
    });
  }, [formId]);

  // ── Step 1 ────────────────────────────────────────────────────────────────

  const saveBasics = async () => {
    if (!basics.nameAr.trim() || !basics.nameEn.trim()) { toast.error("الاسم مطلوب"); return; }
    setSaving(true);
    try {
      if (id) {
        await updateRequestType(id, {
          nameAr: basics.nameAr, nameEn: basics.nameEn,
          descriptionAr: basics.descriptionAr || null,
          categoryId: basics.categoryId || null,
          workflowDefinitionId: workflowId || null,
          icon: basics.icon, color: basics.color,
        });
        await loadDetail(id);
      } else {
        if (!basics.code.trim()) { toast.error("الكود مطلوب"); setSaving(false); return; }
        if (!formId) { toast.error("اختر نموذجًا أولاً"); setStep(1); setSaving(false); return; }
        const created = await createRequestType({
          code: basics.code, nameAr: basics.nameAr, nameEn: basics.nameEn,
          descriptionAr: basics.descriptionAr || null,
          categoryId: basics.categoryId || null,
          formDefinitionId: formId,
          workflowDefinitionId: workflowId || null,
          icon: basics.icon, color: basics.color,
        });
        setId(created.id);
        setDetail(created);
        onSaved?.(created.id);
      }
      setStep((s) => Math.min(s + 1, STEPS.length - 1));
    } catch (e) { notifyError(e, "تعذر الحفظ"); }
    finally { setSaving(false); }
  };

  // ── Step 4: effects ───────────────────────────────────────────────────────

  const refreshEffects = useCallback(async () => { if (id) await loadDetail(id); }, [id, loadDetail]);

  const onAddEffect = async (effectType: string) => {
    if (!id) return;
    try {
      await addEffect(id, { effectType, configurationJson: "{}" });
      await refreshEffects();
    } catch (e) { notifyError(e, "تعذر إضافة الإجراء"); }
  };

  const onMove = async (index: number, delta: number) => {
    if (!id || !detail) return;
    const ordered = [...detail.effects];
    const target = index + delta;
    if (target < 0 || target >= ordered.length) return;
    [ordered[index], ordered[target]] = [ordered[target], ordered[index]];
    try {
      await reorderEffects(id, ordered.map((e) => e.id));
      await refreshEffects();
    } catch (e) { notifyError(e, "تعذر إعادة الترتيب"); }
  };

  // ── Step 5 ────────────────────────────────────────────────────────────────

  const runValidation = useCallback(async () => {
    if (!id) return;
    setValidating(true);
    try { setValidation(await validateRequestType(id)); }
    catch (e) { notifyError(e, "تعذر التحقق"); }
    finally { setValidating(false); }
  }, [id]);

  useEffect(() => { if (step === 4 && id) queueMicrotask(() => { runValidation(); }); }, [step, id, runValidation]);

  const activate = async () => {
    if (!id) return;
    setSaving(true);
    try {
      await setRequestTypeActive(id, true);
      toast.success("تم تفعيل الطلب");
      await loadDetail(id);
    } catch (e) { notifyError(e, "تعذر التفعيل"); }
    finally { setSaving(false); }
  };

  if (loading) {
    return <div className="flex justify-center p-12"><Loader2 className="h-8 w-8 animate-spin text-muted-foreground" /></div>;
  }

  return (
    <div className="space-y-6" dir="rtl">
      {isSystem && (
        <div className="flex items-start gap-2 border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-amber-800 dark:text-amber-300">
          <Shield className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            هذا طلب نظامي. يمكنك تعديل الاسم والأيقونة والمسار والإجراءات الاختيارية، ولا يمكن حذفه أو
            تغيير كوده أو إزالة إجراءاته الإلزامية. لنسخة قابلة للتعديل بالكامل، استخدم «نسخ».
          </span>
        </div>
      )}

      <ol className="flex flex-wrap gap-2 text-sm">
        {STEPS.map((s, i) => (
          <li key={s}>
            <button
              type="button"
              disabled={!id && i > 1}
              onClick={() => setStep(i)}
              className={`border px-3 py-1 ${i === step ? "border-primary bg-primary/10 text-primary" : "border-border bg-secondary"} disabled:opacity-40`}
            >
              {i + 1}. {s}
            </button>
          </li>
        ))}
      </ol>

      {/* ── Step 1: basics ── */}
      {step === 0 && (
        <div className="max-w-2xl space-y-4 border border-border bg-card p-6">
          <Field label="الكود">
            <input
              value={basics.code}
              disabled={Boolean(id)}
              onChange={(e) => setBasics({ ...basics, code: e.target.value.toUpperCase() })}
              className="h-9 w-full border border-border bg-background px-3 text-sm disabled:opacity-60"
            />
            {/* Immutable server-side too: provisioning, seeding and the required-effect declarations
                all key on Code. */}
            <p className="mt-1 text-xs text-muted-foreground">
              {id ? "لا يمكن تغيير الكود بعد الإنشاء." : "معرّف ثابت، بحروف إنجليزية كبيرة."}
            </p>
          </Field>
          <Field label="الاسم (عربي)">
            <input value={basics.nameAr} onChange={(e) => setBasics({ ...basics, nameAr: e.target.value })}
              className="h-9 w-full border border-border bg-background px-3 text-sm" />
          </Field>
          <Field label="الاسم (إنجليزي)">
            <input value={basics.nameEn} onChange={(e) => setBasics({ ...basics, nameEn: e.target.value })}
              className="h-9 w-full border border-border bg-background px-3 text-sm" />
          </Field>
          <Field label="الوصف">
            <input value={basics.descriptionAr} onChange={(e) => setBasics({ ...basics, descriptionAr: e.target.value })}
              className="h-9 w-full border border-border bg-background px-3 text-sm" />
          </Field>
          <Field label="التصنيف">
            <select value={basics.categoryId} onChange={(e) => setBasics({ ...basics, categoryId: e.target.value })}
              className="h-9 w-full border border-border bg-background px-3 text-sm">
              <option value="">— بدون —</option>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.nameAr || c.nameEn}</option>)}
            </select>
          </Field>
          <Field label="الأيقونة واللون">
            <div className="flex flex-wrap items-center gap-2">
              <select value={basics.icon} onChange={(e) => setBasics({ ...basics, icon: e.target.value })}
                className="h-9 border border-border bg-background px-2 text-sm">
                {REQUEST_ICON_KEYS.map((k) => <option key={k} value={k}>{k}</option>)}
              </select>
              <div className="flex flex-wrap gap-1">
                {REQUEST_COLORS.map((c) => (
                  <button key={c} type="button" onClick={() => setBasics({ ...basics, color: c })}
                    title={c} style={{ background: c }}
                    className={`h-7 w-7 border ${basics.color === c ? "border-foreground" : "border-transparent"}`} />
                ))}
              </div>
              <IconPreview iconKey={basics.icon} color={basics.color} />
            </div>
          </Field>
          <button onClick={saveBasics} disabled={saving}
            className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground disabled:opacity-50">
            {saving && <Loader2 className="h-4 w-4 animate-spin" />} حفظ ومتابعة
          </button>
        </div>
      )}

      {/* ── Step 2: form ── */}
      {step === 1 && (
        <div className="max-w-3xl space-y-4 border border-border bg-card p-6">
          <Field label="النموذج">
            <select value={formId} onChange={(e) => setFormId(e.target.value)} disabled={Boolean(id)}
              className="h-9 w-full border border-border bg-background px-3 text-sm disabled:opacity-60">
              <option value="">— اختر نموذجًا —</option>
              {forms.map((f) => <option key={f.id} value={f.id}>{f.nameAr || f.nameEn} ({f.code})</option>)}
            </select>
            <p className="mt-1 text-xs text-muted-foreground">
              {id ? "النموذج مرتبط بالطلب ولا يمكن تغييره من هنا." : "الحقول أدناه هي ما يمكن ربط الإجراءات به."}
            </p>
          </Field>

          {formFields.length > 0 ? (
            <div className="border border-border">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-right text-xs text-muted-foreground">
                    <th className="px-3 py-2 font-medium">الحقل</th>
                    <th className="px-3 py-2 font-medium">الكود</th>
                    <th className="px-3 py-2 font-medium">النوع</th>
                    <th className="px-3 py-2 font-medium">مطلوب</th>
                    <th className="px-3 py-2 font-medium">مصدر الخيارات</th>
                  </tr>
                </thead>
                <tbody>
                  {formFields.map((f) => {
                    const d = parseOptions(f.options);
                    return (
                      <tr key={f.id} className="border-b border-border/60 last:border-0">
                        <td className="px-3 py-2">{f.nameAr}</td>
                        <td className="px-3 py-2 font-mono text-xs">{f.code}</td>
                        <td className="px-3 py-2 text-muted-foreground">{f.fieldType}</td>
                        <td className="px-3 py-2">{f.isRequired ? "نعم" : "لا"}</td>
                        <td className="px-3 py-2 text-xs text-muted-foreground">
                          {d?.endpoint ? `مصدر مباشر: ${d.endpoint}` : d?.lookup ? `قائمة: ${d.lookup}` : "—"}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="text-sm text-muted-foreground">لا توجد حقول — اختر نموذجًا يحتوي على حقول.</p>
          )}
        </div>
      )}

      {/* ── Step 3: workflow ── */}
      {step === 2 && (
        <div className="max-w-2xl space-y-4 border border-border bg-card p-6">
          <Field label="مسار الموافقة">
            <select value={workflowId} onChange={(e) => setWorkflowId(e.target.value)}
              className="h-9 w-full border border-border bg-background px-3 text-sm">
              <option value="">— اختر مسارًا —</option>
              {workflows.map((w) => <option key={w.id} value={w.id}>{w.nameAr || w.nameEn} ({w.code})</option>)}
            </select>
          </Field>
          <p className="text-sm text-muted-foreground">
            {workflowId
              ? "سيمر الطلب على خطوات هذا المسار قبل تنفيذ الإجراءات."
              : "بدون مسار موافقة لا يمكن تفعيل الطلب."}
          </p>
          <button onClick={saveBasics} disabled={saving || !id}
            className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground disabled:opacity-50">
            {saving && <Loader2 className="h-4 w-4 animate-spin" />} حفظ ومتابعة
          </button>
        </div>
      )}

      {/* ── Step 4: effects ── */}
      {step === 3 && id && detail && (
        <EffectsStep
          detail={detail}
          catalog={catalog}
          fields={formFields}
          onAdd={onAddEffect}
          onMove={onMove}
          onChanged={refreshEffects}
        />
      )}

      {/* ── Step 5: review + activate ── */}
      {step === 4 && id && detail && (
        <ReviewStep
          detail={detail}
          forms={forms}
          workflows={workflows}
          validation={validation}
          validating={validating}
          saving={saving}
          onRevalidate={runValidation}
          onActivate={activate}
        />
      )}
    </div>
  );
}

// ── Step 4 ──────────────────────────────────────────────────────────────────

function EffectsStep({
  detail, catalog, fields, onAdd, onMove, onChanged,
}: {
  detail: RequestTypeDetail;
  catalog: EffectActionDescriptor[];
  fields: FormField[];
  onAdd: (effectType: string) => Promise<void>;
  onMove: (index: number, delta: number) => Promise<void>;
  onChanged: () => Promise<void>;
}) {
  const [picking, setPicking] = useState(false);
  const byType = useMemo(
    () => new Map(catalog.map((d) => [d.effectType.toLowerCase(), d])), [catalog]);

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h3 className="text-sm font-semibold">الإجراءات بعد الموافقة</h3>
        <button onClick={() => setPicking((p) => !p)}
          className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-3 text-xs hover:bg-secondary/70">
          <Plus className="h-3.5 w-3.5" /> إضافة إجراء
        </button>
      </div>

      {picking && (
        <div className="border border-border bg-card p-3">
          {catalog.length === 0 ? (
            <p className="text-xs text-muted-foreground">
              لا توجد إجراءات متاحة لك. تتطلب الإجراءات صلاحيات إضافية حسب الوحدة.
            </p>
          ) : (
            <div className="grid gap-2 sm:grid-cols-2">
              {catalog.map((d) => (
                <button key={d.effectType} type="button" disabled={!d.executorAvailable}
                  onClick={async () => { await onAdd(d.effectType); setPicking(false); }}
                  className="border border-border p-2 text-right hover:border-primary disabled:opacity-40">
                  <div className="text-sm font-medium">{d.labelAr}</div>
                  <div className="text-[11px] text-muted-foreground">{d.descriptionAr}</div>
                  <div className="mt-1 text-[11px] text-muted-foreground">
                    {d.module} · {d.executionMode === "Asynchronous" ? "غير متزامن" : "متزامن"}
                    {!d.executorAvailable && " · غير متاح"}
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      )}

      {detail.effects.length === 0 && (
        <p className="border border-dashed border-border p-6 text-center text-sm text-muted-foreground">
          لا توجد إجراءات. الطلب سيمر بالموافقة دون أي أثر على البيانات.
        </p>
      )}

      {detail.effects.map((effect, i) => (
        <EffectCard
          key={effect.id}
          effect={effect}
          descriptor={byType.get(effect.effectType.toLowerCase())}
          fields={fields}
          index={i}
          total={detail.effects.length}
          onMove={onMove}
          onChanged={onChanged}
        />
      ))}
    </div>
  );
}

function EffectCard({
  effect, descriptor, fields, index, total, onMove, onChanged,
}: {
  effect: RequestEffectDefinition;
  descriptor?: EffectActionDescriptor;
  fields: FormField[];
  index: number;
  total: number;
  onMove: (index: number, delta: number) => Promise<void>;
  onChanged: () => Promise<void>;
}) {
  const [config, setConfig] = useState<EffectConfiguration>(() => parseConfiguration(effect.configurationJson));
  const [busy, setBusy] = useState(false);
  const [dirty, setDirty] = useState(false);

  // An action absent from the catalog means one of two things, and the difference matters: the
  // caller may not configure it, or it has been retired. Either way it is shown rather than hidden —
  // hiding it would make the effect list silently incomplete.
  const locked = !descriptor;

  const save = async () => {
    setBusy(true);
    try {
      await updateEffect(effect.id, {
        effectType: effect.effectType,
        trigger: effect.trigger,
        isEnabled: effect.isEnabled,
        configurationJson: serializeConfiguration(config),
      });
      setDirty(false);
      await onChanged();
      toast.success("تم الحفظ");
    } catch (e) { notifyError(e, "تعذر الحفظ"); }
    finally { setBusy(false); }
  };

  const toggle = async () => {
    setBusy(true);
    try { await setEffectEnabled(effect.id, !effect.isEnabled); await onChanged(); }
    catch (e) { notifyError(e, "تعذر التغيير"); }
    finally { setBusy(false); }
  };

  const remove = async () => {
    setBusy(true);
    try { await deleteEffect(effect.id); await onChanged(); }
    catch (e) { notifyError(e, "تعذر الحذف"); }
    finally { setBusy(false); }
  };

  return (
    <div className={`border border-border bg-card p-4 ${effect.isEnabled ? "" : "opacity-60"}`}>
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="flex items-center gap-2 text-sm font-medium">
            {effect.isRequired && <Lock className="h-3.5 w-3.5 text-amber-600" />}
            <span>{effect.labelAr ?? descriptor?.labelAr ?? effect.effectType}</span>
            {effect.isRequired && (
              <span className="border border-amber-500/40 bg-amber-500/10 px-1.5 py-0.5 text-[10px] text-amber-700 dark:text-amber-400">
                إلزامي
              </span>
            )}
            {effect.executionMode === "Asynchronous" && (
              <span className="border border-border bg-secondary px-1.5 py-0.5 text-[10px] text-muted-foreground">
                غير متزامن
              </span>
            )}
          </div>
          <p className="mt-0.5 font-mono text-[11px] text-muted-foreground">{effect.effectType}</p>
        </div>

        <div className="flex items-center gap-1">
          <button onClick={() => onMove(index, -1)} disabled={index === 0 || busy} title="تحريك لأعلى"
            className="inline-flex h-8 w-8 items-center justify-center border border-border bg-secondary disabled:opacity-40">
            <ArrowUp className="h-3.5 w-3.5" />
          </button>
          <button onClick={() => onMove(index, 1)} disabled={index === total - 1 || busy} title="تحريك لأسفل"
            className="inline-flex h-8 w-8 items-center justify-center border border-border bg-secondary disabled:opacity-40">
            <ArrowDown className="h-3.5 w-3.5" />
          </button>
          <button onClick={toggle} disabled={!effect.canDisable || busy}
            title={effect.canDisable ? (effect.isEnabled ? "تعطيل" : "تفعيل") : "إجراء إلزامي — لا يمكن تعطيله"}
            className="inline-flex h-8 items-center gap-1 border border-border bg-secondary px-2 text-xs disabled:opacity-40">
            {effect.isEnabled ? <X className="h-3.5 w-3.5" /> : <Check className="h-3.5 w-3.5" />}
            {effect.isEnabled ? "تعطيل" : "تفعيل"}
          </button>
          <button onClick={remove} disabled={!effect.canDelete || busy}
            title={effect.canDelete ? "حذف" : "إجراء إلزامي — لا يمكن حذفه"}
            className="inline-flex h-8 w-8 items-center justify-center border border-border bg-secondary text-destructive disabled:opacity-40">
            <Trash2 className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {!effect.executorAvailable && (
        <div className="mt-2 flex items-center gap-1.5 border border-destructive/40 bg-destructive/10 p-2 text-xs text-destructive">
          <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
          لا يوجد منفّذ مسجَّل لهذا الإجراء — لن يمكن تفعيل الطلب.
        </div>
      )}

      <div className="mt-3">
        {locked ? (
          <LockedNotice />
        ) : (
          <>
            <EffectInputMapper
              descriptor={descriptor!}
              config={config}
              fields={fields}
              onChange={(next) => { setConfig(next); setDirty(true); }}
            />
            {dirty && (
              <button onClick={save} disabled={busy}
                className="mt-3 inline-flex h-8 items-center gap-1.5 bg-primary px-3 text-xs text-primary-foreground disabled:opacity-50">
                {busy && <Loader2 className="h-3.5 w-3.5 animate-spin" />} حفظ الربط
              </button>
            )}
          </>
        )}
      </div>
    </div>
  );
}

function LockedNotice() {
  return (
    <div className="flex items-start gap-2 border border-border bg-secondary/40 p-3 text-xs text-muted-foreground">
      <Lock className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <span>
        هذا الإجراء غير متاح للتعديل — إمّا لأنك لا تملك صلاحية إعداده، أو لأنه لم يعد ضمن الإجراءات
        المدعومة. سيظل يعمل كما هو، ويمكن لمن يملك الصلاحية تعديله.
      </span>
    </div>
  );
}

// ── Step 5 ──────────────────────────────────────────────────────────────────

function ReviewStep({
  detail, forms, workflows, validation, validating, saving, onRevalidate, onActivate,
}: {
  detail: RequestTypeDetail;
  forms: FormDefinition[];
  workflows: WorkflowDefinition[];
  validation: ValidationResult | null;
  validating: boolean;
  saving: boolean;
  onRevalidate: () => void;
  onActivate: () => void;
}) {
  const form = forms.find((f) => f.id === detail.formDefinitionId);
  const workflow = workflows.find((w) => w.id === detail.workflowDefinitionId);

  // Grouped by section so a user sees "the form is wrong" rather than a flat list of field names.
  const groups = useMemo(() => {
    const g = new Map<string, ValidationResult["errors"]>();
    for (const e of validation?.errors ?? []) {
      const key = e.effectType ?? (e.field === "form" || e.field === "workflow" ? "الإعداد الأساسي" : "أخرى");
      if (!g.has(key)) g.set(key, []);
      g.get(key)!.push(e);
    }
    return [...g.entries()];
  }, [validation]);

  return (
    <div className="space-y-4">
      <div className="grid gap-3 md:grid-cols-3">
        <Summary title="النموذج" value={form ? `${form.nameAr || form.nameEn}` : "غير مرتبط"} ok={Boolean(form)} />
        <Summary title="مسار الموافقة" value={workflow ? `${workflow.nameAr || workflow.nameEn}` : "غير مرتبط"} ok={Boolean(workflow)} />
        <Summary title="الإجراءات" value={`${detail.effects.filter((e) => e.isEnabled).length} مفعّل من ${detail.effects.length}`} ok />
      </div>

      <div className="border border-border bg-card p-4">
        <div className="flex items-center justify-between">
          <h3 className="text-sm font-semibold">نتيجة التحقق</h3>
          <button onClick={onRevalidate} disabled={validating}
            className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-3 text-xs disabled:opacity-50">
            {validating && <Loader2 className="h-3.5 w-3.5 animate-spin" />} إعادة التحقق
          </button>
        </div>

        {validating && <p className="mt-3 text-sm text-muted-foreground">جارٍ التحقق…</p>}

        {!validating && validation?.isValid && (
          <p className="mt-3 flex items-center gap-1.5 text-sm text-green-700 dark:text-green-400">
            <Check className="h-4 w-4" /> الإعداد صالح ويمكن التفعيل.
          </p>
        )}

        {!validating && validation && !validation.isValid && (
          <div className="mt-3 space-y-3">
            {groups.map(([section, errors]) => (
              <div key={section}>
                <p className="text-xs font-semibold">{section}</p>
                <ul className="mt-1 space-y-1">
                  {errors.map((e, i) => (
                    <li key={i} className="flex items-start gap-1.5 text-xs text-destructive">
                      <AlertTriangle className="mt-0.5 h-3 w-3 shrink-0" />
                      <span>{e.messageAr} <span className="text-muted-foreground">({e.field})</span></span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        )}
      </div>

      <button
        onClick={onActivate}
        disabled={saving || validating || !validation?.isValid || detail.isActive}
        className="inline-flex h-9 items-center gap-2 bg-primary px-4 text-sm text-primary-foreground disabled:opacity-50"
      >
        {saving && <Loader2 className="h-4 w-4 animate-spin" />}
        {detail.isActive ? "الطلب مفعّل" : "تفعيل الطلب"}
      </button>
      {!validation?.isValid && !validating && (
        <p className="text-xs text-muted-foreground">التفعيل متاح فقط بعد اجتياز التحقق.</p>
      )}
    </div>
  );
}

// ── Shared bits ─────────────────────────────────────────────────────────────

/**
 * requestIcon returns a component rather than an element. createElement is used instead of binding
 * it to a capitalised local, which the React lint rules read as defining a component during render.
 */
function IconPreview({ iconKey, color }: { iconKey: string; color: string }) {
  return (
    <span className="inline-flex h-9 w-9 items-center justify-center border border-border" style={{ color }}>
      {createElement(requestIcon(iconKey), { className: "h-4 w-4" })}
    </span>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1 block text-sm font-medium">{label}</label>
      {children}
    </div>
  );
}

function Summary({ title, value, ok }: { title: string; value: string; ok: boolean }) {
  return (
    <div className="border border-border bg-card p-3">
      <p className="text-xs text-muted-foreground">{title}</p>
      <p className={`mt-1 text-sm ${ok ? "" : "text-destructive"}`}>{value}</p>
    </div>
  );
}

function parseOptions(options?: string | null): { lookup?: string; endpoint?: string } | null {
  if (!options) return null;
  try {
    const d = JSON.parse(options);
    return d && typeof d === "object" && (d.lookup || d.endpoint) ? d : null;
  } catch { return null; }
}
