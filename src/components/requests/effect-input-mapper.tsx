"use client";

import { Lock } from "lucide-react";
import {
  CURRENT_USER_KEYS, EffectActionDescriptor, EffectConfiguration, EffectValueSource,
  REQUEST_CONTEXT_KEYS, SOURCE_LABELS_AR, TENANT_CONTEXT_KEYS,
} from "@/lib/api/request-types";
import { FormField } from "@/lib/api/forms";

/**
 * Maps one action's declared inputs to value sources.
 *
 * Every control is driven by the catalog descriptor rather than hardcoded per action: the source
 * dropdown offers only that input's AllowedSources, and the key control changes shape with the
 * chosen source. This is what keeps a client from ever naming a CLR type, table or column — the
 * only thing it can express is a (source, key) pair the server already declared valid.
 */
export function EffectInputMapper({
  descriptor,
  config,
  fields,
  disabled,
  onChange,
}: {
  descriptor: EffectActionDescriptor;
  config: EffectConfiguration;
  /** Fields on the request type's linked form — the FormField source's vocabulary. */
  fields: FormField[];
  disabled?: boolean;
  onChange: (next: EffectConfiguration) => void;
}) {
  const setMapping = (inputKey: string, source: EffectValueSource, key: string) =>
    onChange({ ...config, [inputKey]: { source, key } });

  const clearMapping = (inputKey: string) => {
    const next = { ...config };
    delete next[inputKey];
    onChange(next);
  };

  return (
    <div className="space-y-3">
      {descriptor.inputs.map((input) => {
        const mapping = config[input.key];
        const source = mapping?.source ?? input.allowedSources[0];
        const missing = input.isRequired && (!mapping || !mapping.key);

        return (
          <div key={input.key} className="grid gap-2 md:grid-cols-[minmax(8rem,1fr)_minmax(8rem,1fr)_2fr]">
            <div className="flex items-center gap-1.5 text-sm">
              <span>{input.labelAr}</span>
              {input.isRequired && <span className="text-destructive" title="مطلوب">*</span>}
            </div>

            <select
              value={mapping ? source : ""}
              onChange={(e) => {
                const next = e.target.value as EffectValueSource | "";
                if (!next) { clearMapping(input.key); return; }
                // Key resets on a source change: a form-field code is meaningless as a context key,
                // and carrying it over would produce a mapping that validates as "field not found".
                setMapping(input.key, next, "");
              }}
              disabled={disabled}
              className="h-9 border border-border bg-background px-2 text-sm disabled:opacity-60"
            >
              <option value="">— بدون —</option>
              {input.allowedSources.map((s) => (
                <option key={s} value={s}>{SOURCE_LABELS_AR[s]}</option>
              ))}
            </select>

            <div>
              {mapping ? (
                <KeyControl
                  source={source}
                  value={mapping.key}
                  fields={fields}
                  disabled={disabled}
                  onChange={(key) => setMapping(input.key, source, key)}
                />
              ) : (
                <p className="pt-2 text-xs text-muted-foreground">
                  {input.isRequired ? "مطلوب — اختر مصدر القيمة." : "اختياري."}
                </p>
              )}
              {missing && <p className="mt-1 text-[11px] text-destructive">هذا المدخل مطلوب ولم يتم ربطه.</p>}
            </div>
          </div>
        );
      })}
    </div>
  );
}

/** The key control's shape follows the source: a closed list, or free text for a literal. */
function KeyControl({
  source, value, fields, disabled, onChange,
}: {
  source: EffectValueSource;
  value: string;
  fields: FormField[];
  disabled?: boolean;
  onChange: (value: string) => void;
}) {
  const cls = "h-9 w-full border border-border bg-background px-2 text-sm disabled:opacity-60";

  if (source === "Constant") {
    return (
      <input value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled}
        placeholder="القيمة" className={cls} />
    );
  }

  const options =
    source === "FormField" ? fields.map((f) => ({ v: f.code, l: `${f.nameAr} (${f.code})` }))
    : source === "RequestContext" ? REQUEST_CONTEXT_KEYS.map((k) => ({ v: k, l: k }))
    : source === "CurrentUser" ? CURRENT_USER_KEYS.map((k) => ({ v: k, l: k }))
    : TENANT_CONTEXT_KEYS.map((k) => ({ v: k, l: k }));

  return (
    <div>
      <select value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled} className={cls}>
        <option value="">— اختر —</option>
        {options.map((o) => <option key={o.v} value={o.v}>{o.l}</option>)}
      </select>
      {/* A stored mapping can outlive the field it names — a rename leaves it dangling. Saying so
          here is more useful than letting activation fail with "form field does not exist". */}
      {source === "FormField" && value && !fields.some((f) => f.code === value) && (
        <p className="mt-1 text-[11px] text-destructive">الحقل «{value}» غير موجود في النموذج الحالي.</p>
      )}
    </div>
  );
}

/** Shown instead of the mapper when the caller may not configure the action. */
export function LockedEffectNotice({ reason }: { reason: string }) {
  return (
    <div className="flex items-start gap-2 border border-border bg-secondary/40 p-3 text-xs text-muted-foreground">
      <Lock className="mt-0.5 h-3.5 w-3.5 shrink-0" />
      <span>{reason}</span>
    </div>
  );
}
