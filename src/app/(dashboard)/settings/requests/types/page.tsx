"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { ArrowRight, Copy, GitBranch, Loader2, Lock, Pencil, Plus, Shield, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { AccessGuard } from "@/components/access/access-guard";
import { ApiError } from "@/lib/api-client";
import { RequestBuilder } from "@/components/requests/request-builder";
import { requestIcon } from "@/lib/request-icons";
import {
  RequestTypeListItem, deleteRequestType, duplicateRequestType, listRequestTypes,
  setRequestTypeActive,
} from "@/lib/api/request-types";

function notifyError(err: unknown, fallback: string) {
  toast.error(err instanceof ApiError ? err.message : fallback);
}

/**
 * Request-type settings.
 *
 * Reads the real RequestType entity via /api/platform/request-types. It previously read
 * MasterDataItem rows with ObjectType=RequestType — rows no backend code ever consumed, while the
 * Request Center ran off engine_request_types. Editing a request type here changed nothing that an
 * actual request did.
 */
export default function RequestTypesPage() {
  return (
    <AccessGuard anyOf={["Platform.Workflows.View"]}>
      <RequestTypesInner />
    </AccessGuard>
  );
}

function RequestTypesInner() {
  const [items, setItems] = useState<RequestTypeListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  // undefined = list view · null = builder for a new type · string = builder for that id
  const [builderFor, setBuilderFor] = useState<string | null | undefined>(undefined);
  const [busy, setBusy] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(false);
    try { setItems(await listRequestTypes(true)); }
    catch { setError(true); }
    finally { setLoading(false); }
  }, []);

  useEffect(() => { queueMicrotask(() => { load(); }); }, [load]);

  const onDuplicate = async (t: RequestTypeListItem) => {
    setBusy(t.id);
    try {
      const copy = await duplicateRequestType(t.id, {
        nameAr: `${t.nameAr} (نسخة)`,
        nameEn: `${t.nameEn} (Copy)`,
      });
      toast.success(`تم إنشاء نسخة قابلة للتعديل: ${copy.code}`);
      await load();
      setBuilderFor(copy.id);
    } catch (e) { notifyError(e, "تعذر النسخ"); }
    finally { setBusy(null); }
  };

  const onDelete = async (t: RequestTypeListItem) => {
    setBusy(t.id);
    try {
      await deleteRequestType(t.id);
      toast.success("تم الحذف");
      await load();
    } catch (e) { notifyError(e, "تعذر الحذف"); }
    finally { setBusy(null); }
  };

  const onToggleActive = async (t: RequestTypeListItem) => {
    setBusy(t.id);
    try {
      await setRequestTypeActive(t.id, !t.isActive);
      await load();
    } catch (e) {
      // Activation runs full server-side validation, so an incomplete configuration surfaces here
      // rather than at submit time. Point at the builder instead of only reporting the failure.
      notifyError(e, "تعذر التغيير — راجع إعدادات الطلب");
    }
    finally { setBusy(null); }
  };

  if (builderFor !== undefined) {
    return (
      <div className="space-y-4" dir="rtl">
        <button onClick={() => { setBuilderFor(undefined); load(); }}
          className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
          <ArrowRight className="h-4 w-4" /> أنواع الطلبات
        </button>
        <RequestBuilder requestTypeId={builderFor ?? undefined} onSaved={() => load()} />
      </div>
    );
  }

  return (
    <div className="space-y-6" dir="rtl">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href="/settings/requests" className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground">
            <ArrowRight className="h-4 w-4" /> إعدادات الطلبات
          </Link>
          <h1 className="mt-2 text-2xl font-bold">أنواع الطلبات</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            النموذج ومسار الموافقة والإجراءات التي تُنفَّذ بعد الاعتماد.
          </p>
        </div>
        <button onClick={() => setBuilderFor(null)}
          className="inline-flex h-9 items-center gap-2 bg-primary px-3 text-sm text-primary-foreground hover:bg-primary/90">
          <Plus className="h-4 w-4" /> طلب جديد
        </button>
      </div>

      {loading ? (
        <div className="flex justify-center border border-border bg-card p-12">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      ) : error ? (
        <div className="flex flex-col items-center border border-border bg-card p-12 text-center">
          <p className="mb-2 text-sm text-muted-foreground">تعذر تحميل أنواع الطلبات</p>
          <button onClick={load} className="text-sm underline hover:no-underline">إعادة المحاولة</button>
        </div>
      ) : items.length === 0 ? (
        <div className="border border-dashed border-border bg-card p-12 text-center">
          <p className="text-sm text-muted-foreground">
            لا توجد أنواع طلبات. شغّل التهيئة من إعدادات الطلبات، أو أنشئ طلبًا جديدًا.
          </p>
        </div>
      ) : (
        <div className="border border-border bg-card overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border text-right text-xs uppercase tracking-wider text-muted-foreground">
                <th className="px-4 py-3 font-medium">الطلب</th>
                <th className="px-4 py-3 font-medium">الإعداد</th>
                <th className="px-4 py-3 font-medium">الإجراءات</th>
                <th className="px-4 py-3 font-medium">الحالة</th>
                <th className="px-4 py-3 font-medium">إدارة</th>
              </tr>
            </thead>
            <tbody>
              {items.map((t) => {
                const Icon = requestIcon(t.icon);
                return (
                  <tr key={t.id} className="border-b border-border/60 last:border-0 hover:bg-secondary/40">
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-2">
                        <span className="inline-flex h-8 w-8 items-center justify-center border border-border"
                          style={{ color: t.color ?? undefined }}>
                          <Icon className="h-4 w-4" />
                        </span>
                        <div>
                          <div className="flex items-center gap-1.5 font-medium">
                            {t.nameAr || t.nameEn}
                            {t.isSystem && (
                              <span title="طلب نظامي — لا يمكن حذفه"
                                className="inline-flex items-center gap-1 border border-amber-500/40 bg-amber-500/10 px-1.5 py-0.5 text-[10px] text-amber-700 dark:text-amber-400">
                                <Shield className="h-3 w-3" /> نظامي
                              </span>
                            )}
                          </div>
                          <div className="font-mono text-xs text-muted-foreground">{t.code}</div>
                        </div>
                      </div>
                    </td>
                    <td className="px-4 py-3 text-xs text-muted-foreground">
                      <div>{t.hasForm ? "نموذج ✓" : <span className="text-destructive">بدون نموذج</span>}</div>
                      <div className="mt-0.5 flex items-center gap-1">
                        <GitBranch className="h-3 w-3" />
                        {t.hasWorkflow ? "مسار موافقة ✓" : <span className="text-destructive">بدون مسار</span>}
                      </div>
                    </td>
                    <td className="px-4 py-3 text-muted-foreground">{t.effectCount}</td>
                    <td className="px-4 py-3">
                      <button onClick={() => onToggleActive(t)} disabled={busy === t.id}
                        className={`border px-2 py-0.5 text-xs disabled:opacity-50 ${
                          t.isActive
                            ? "border-green-600/30 bg-green-600/10 text-green-700 dark:text-green-400"
                            : "border-border bg-secondary text-muted-foreground"}`}>
                        {t.isActive ? "مفعّل" : "غير مفعّل"}
                      </button>
                    </td>
                    <td className="px-4 py-3">
                      <div className="flex items-center gap-1.5">
                        <button onClick={() => setBuilderFor(t.id)} title="تعديل"
                          className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70">
                          <Pencil className="h-3.5 w-3.5" /> تعديل
                        </button>
                        <button onClick={() => onDuplicate(t)} disabled={busy === t.id}
                          title="إنشاء نسخة مخصّصة قابلة للتعديل بالكامل"
                          className="inline-flex h-8 w-8 items-center justify-center border border-border bg-secondary disabled:opacity-50">
                          {busy === t.id ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Copy className="h-3.5 w-3.5" />}
                        </button>
                        {/* Disabled from canDelete, but the API refuses it regardless — the icon
                            change is a hint, not the enforcement. */}
                        <button onClick={() => onDelete(t)} disabled={!t.canDelete || busy === t.id}
                          title={t.canDelete ? "حذف" : "طلب نظامي — لا يمكن حذفه. انسخه بدلاً من ذلك."}
                          className="inline-flex h-8 w-8 items-center justify-center border border-border bg-secondary text-destructive disabled:opacity-40">
                          {t.canDelete ? <Trash2 className="h-3.5 w-3.5" /> : <Lock className="h-3.5 w-3.5" />}
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
