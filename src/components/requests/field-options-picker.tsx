"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, RefreshCw, Search } from "lucide-react";
import { apiFetch } from "@/lib/api-client";
import { getLookup, LookupItem } from "@/lib/api/lookups";

/**
 * The two shapes a FormField.Options descriptor can take:
 *
 *   {"lookup":"LeaveType"}                  → master data by ObjectType
 *   {"endpoint":"assets/assignable"}        → a real entity feed under /api/platform/
 *
 * The endpoint form exists because the custody picker needs actual assets. It originally pointed at
 * MasterDataObjectType.AssetType, which lists asset *categories* — so it would have handed a
 * category id to Assets.AssignCustody, and every custody request would have failed at approval time.
 */
export interface OptionsDescriptor {
  lookup?: string;
  endpoint?: string;
}

export interface PickerOption {
  id: string;
  labelAr: string;
  labelEn: string;
  hint?: string;
}

/**
 * Parses a field's Options JSON. Returns null when there is no usable descriptor — the caller then
 * renders a plain text input rather than nothing, so a malformed descriptor degrades the control
 * instead of blanking the form.
 */
export function parseOptionsDescriptor(options?: string | null): OptionsDescriptor | null {
  if (!options) return null;
  try {
    const parsed = JSON.parse(options);
    if (!parsed || typeof parsed !== "object") return null;
    const d = parsed as OptionsDescriptor;
    return d.lookup || d.endpoint ? d : null;
  } catch {
    return null;
  }
}

/** Shape returned by the platform asset feed and anything else modelled on it. */
interface EndpointRow {
  id: string;
  code?: string | null;
  nameAr?: string | null;
  nameEn?: string | null;
  categoryNameAr?: string | null;
  categoryNameEn?: string | null;
  status?: string | null;
}

async function loadFromEndpoint(endpoint: string, search: string): Promise<PickerOption[]> {
  // Descriptors store a path relative to /api/platform/, so an absolute one is accepted too rather
  // than double-prefixed.
  const path = endpoint.startsWith("/") ? endpoint : `/api/platform/${endpoint}`;
  const url = search ? `${path}${path.includes("?") ? "&" : "?"}search=${encodeURIComponent(search)}` : path;
  const rows = await apiFetch<EndpointRow[]>(url);
  return rows.map((r) => ({
    id: r.id,
    labelAr: r.nameAr || r.nameEn || r.code || r.id,
    labelEn: r.nameEn || r.nameAr || r.code || r.id,
    hint: [r.code, r.categoryNameAr ?? r.categoryNameEn].filter(Boolean).join(" · ") || undefined,
  }));
}

async function loadFromLookup(objectType: string): Promise<PickerOption[]> {
  const items: LookupItem[] = await getLookup(objectType);
  return items.map((i) => ({ id: i.id, labelAr: i.nameAr || i.nameEn, labelEn: i.nameEn || i.nameAr }));
}

/**
 * A select backed by either descriptor form, with the states a remote source actually has:
 * loading, empty, and failed. A failed load renders a retry rather than an empty dropdown, because
 * an empty dropdown is indistinguishable from "there is nothing to choose".
 */
export function FieldOptionsPicker({
  descriptor,
  value,
  onChange,
  disabled,
  placeholder = "— اختر —",
  searchable,
}: {
  descriptor: OptionsDescriptor;
  value: string;
  onChange: (value: string) => void;
  disabled?: boolean;
  placeholder?: string;
  /** Endpoint sources support server-side search; master-data lookups are small and do not. */
  searchable?: boolean;
}) {
  const [options, setOptions] = useState<PickerOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");

  useEffect(() => {
    const t = setTimeout(() => setDebounced(search.trim()), 300);
    return () => clearTimeout(t);
  }, [search]);

  const load = useCallback(async () => {
    setLoading(true);
    setFailed(false);
    try {
      const rows = descriptor.endpoint
        ? await loadFromEndpoint(descriptor.endpoint, debounced)
        : await loadFromLookup(descriptor.lookup!);
      setOptions(rows);
    } catch {
      setFailed(true);
      setOptions([]);
    } finally {
      setLoading(false);
    }
  }, [descriptor.endpoint, descriptor.lookup, debounced]);

  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  const isEndpoint = Boolean(descriptor.endpoint);

  return (
    <div className="space-y-1">
      {searchable && isEndpoint && (
        <div className="relative">
          <Search className="pointer-events-none absolute inset-y-0 right-2 my-auto h-3.5 w-3.5 text-muted-foreground" />
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="ابحث…"
            disabled={disabled}
            className="h-8 w-full border border-border bg-background pr-7 pl-2 text-xs"
          />
        </div>
      )}

      <div className="flex items-center gap-1">
        <select
          value={value}
          onChange={(e) => onChange(e.target.value)}
          disabled={disabled || loading || failed}
          className="h-9 w-full border border-border bg-background px-2 text-sm disabled:opacity-60"
        >
          <option value="">{placeholder}</option>
          {options.map((o) => (
            <option key={o.id} value={o.id}>
              {o.labelAr}{o.hint ? ` — ${o.hint}` : ""}
            </option>
          ))}
        </select>

        {/* Assets become unavailable the moment somebody else's request is approved, so the list
            needs to be refreshable without reloading the page. */}
        {isEndpoint && (
          <button
            type="button"
            onClick={load}
            disabled={disabled || loading}
            title="تحديث"
            className="inline-flex h-9 w-9 shrink-0 items-center justify-center border border-border bg-secondary hover:bg-secondary/70 disabled:opacity-50"
          >
            {loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <RefreshCw className="h-3.5 w-3.5" />}
          </button>
        )}
      </div>

      {loading && <p className="text-[11px] text-muted-foreground">جارٍ التحميل…</p>}
      {failed && (
        <p className="text-[11px] text-destructive">
          تعذر تحميل الخيارات.{" "}
          <button type="button" onClick={load} className="underline hover:no-underline">إعادة المحاولة</button>
        </p>
      )}
      {!loading && !failed && options.length === 0 && (
        <p className="text-[11px] text-muted-foreground">
          {debounced ? "لا توجد نتائج مطابقة." : "لا توجد خيارات متاحة."}
        </p>
      )}
    </div>
  );
}
