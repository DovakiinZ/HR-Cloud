# Reports Organization + Sharing UI (SP-1b) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add folders/tags/favorites/pin organization and a share dialog to the reports list, using the already-deployed backend.

**Architecture:** Frontend-only against the canonical `src/lib/api/reports.ts` client (unmodified). A reports share dialog (mirroring the dashboard one), a left sidebar (views + folders + tags), and a list-page rewrite wiring them plus per-row favorite/pin/folder/tags/share actions. No backend, no migration.

**Tech Stack:** Next.js 16.2.6 App Router + TypeScript (RTL, Thamania tokens), canonical reports client, `lucide-react`, `sonner`.

## Global Constraints

- **No backend, no migration.** All endpoints exist and are deployed.
- **Do NOT modify `src/lib/api/reports.ts`.** Consume these (already exported): `getReports({page?,pageSize?,search?,scope?,view?,folderId?,tagId?})`; `getReportFolders()`, `createReportFolder({nameEn,nameAr,parentFolderId?})`, `updateReportFolder(id,{nameEn,nameAr,parentFolderId?})`, `deleteReportFolder(id)`, `setReportFolder(reportId, folderId|null)`; `getReportTags()`, `createReportTag({name,color?})`, `deleteReportTag(id)`, `assignReportTag(reportId,tagId)`, `unassignReportTag(reportId,tagId)`; `toggleReportFavorite(reportId)`, `toggleReportPin(reportId)`; `getReportShares(reportId)`, `addReportShare(reportId,{sharedWithUserId?,sharedWithRoleId?,sharedWithDepartmentId?,canEdit})`, `removeReportShare(reportId,shareId)`. Types: `ReportDefinition` (has `folderId`, `tags: ReportTag[]`, `isFavorite`, `isPinned`), `ReportFolder = {id,nameEn,nameAr,parentFolderId?}`, `ReportTag = {id,name,color?}`, `ReportShare = {id,reportDefinitionId,sharedWithUserId?,sharedWithRoleId?,sharedWithDepartmentId?,canEdit,sharedAt}`.
- **Lookups:** `getDepartments()` + `orgLabel` from `@/lib/api/org` (returns `OrgOption[]`); `apiFetch<{id,name,nameAr?}[]>("/api/roles")`; `apiFetch<{id,fullName?,email}[]>("/api/users")` — all from `@/lib/api-client`/`@/lib/api/org` exactly as `src/components/dashboard/share-dialog.tsx` does.
- **Gating:** `usePermission("Platform.Reports.Edit")` for organize/share mutations; favorite/pin are per-user and available to viewers. Import `usePermission` from `@/lib/permissions`.
- **FE gate:** `npx next build` compiles with 0 errors (no FE test runner). Commit after each task.
- **Deploy:** push → Vercel auto-deploys the FE. No API deploy.

---

## Task 1: Reports share dialog

**Files:**
- Create: `src/components/reports/share-dialog.tsx`

**Interfaces:**
- Consumes: `getReportShares` is called by the parent (list page); this component receives `shares` and calls `addReportShare`/`removeReportShare`, `getDepartments`/`orgLabel`, `apiFetch`.
- Produces: `ReportShareDialog({ reportId, reportName, shares, onClose, onChanged })`.

- [ ] **Step 1: Create `src/components/reports/share-dialog.tsx`** (mirrors `src/components/dashboard/share-dialog.tsx`, retargeted to reports):

```tsx
"use client";

import { useCallback, useEffect, useState } from "react";
import { Loader2, Share2, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { apiFetch } from "@/lib/api-client";
import { getDepartments, orgLabel, type OrgOption } from "@/lib/api/org";
import { addReportShare, removeReportShare, ReportShare } from "@/lib/api/reports";

interface Opt { id: string; label: string }
type Target = "user" | "role" | "department";

export function ReportShareDialog({ reportId, reportName, shares, onClose, onChanged }: {
  reportId: string; reportName: string; shares: ReportShare[]; onClose: () => void; onChanged: () => void;
}) {
  const [target, setTarget] = useState<Target>("department");
  const [users, setUsers] = useState<Opt[]>([]);
  const [roles, setRoles] = useState<Opt[]>([]);
  const [departments, setDepartments] = useState<Opt[]>([]);
  const [selected, setSelected] = useState("");
  const [canEdit, setCanEdit] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    getDepartments().then((d: OrgOption[]) => setDepartments(d.map((x) => ({ id: x.id, label: orgLabel(x) })))).catch(() => {});
    apiFetch<{ id: string; name: string; nameAr?: string | null }[]>("/api/roles")
      .then((r) => setRoles((r ?? []).map((x) => ({ id: x.id, label: x.nameAr || x.name })))).catch(() => {});
    apiFetch<{ id: string; fullName?: string; email: string }[]>("/api/users")
      .then((u) => setUsers((u ?? []).map((x) => ({ id: x.id, label: x.fullName || x.email })))).catch(() => {});
  }, []);

  const options = target === "user" ? users : target === "role" ? roles : departments;
  const labelFor = useCallback((s: ReportShare): string => {
    if (s.sharedWithUserId) return users.find((u) => u.id === s.sharedWithUserId)?.label ?? "مستخدم";
    if (s.sharedWithRoleId) return roles.find((r) => r.id === s.sharedWithRoleId)?.label ?? "دور";
    if (s.sharedWithDepartmentId) return departments.find((d) => d.id === s.sharedWithDepartmentId)?.label ?? "إدارة";
    return "—";
  }, [users, roles, departments]);

  const onShare = async () => {
    if (!selected) return;
    setSaving(true);
    try {
      await addReportShare(reportId, {
        sharedWithUserId: target === "user" ? selected : null,
        sharedWithRoleId: target === "role" ? selected : null,
        sharedWithDepartmentId: target === "department" ? selected : null,
        canEdit,
      });
      toast.success("تمت المشاركة"); setSelected(""); onChanged();
    } catch { toast.error("تعذر المشاركة"); }
    finally { setSaving(false); }
  };

  const onRevoke = async (shareId: string) => {
    try { await removeReportShare(reportId, shareId); toast.success("تم إلغاء المشاركة"); onChanged(); }
    catch { toast.error("تعذر إلغاء المشاركة"); }
  };

  const tabCls = (t: Target) => `flex-1 py-2 text-sm ${target === t ? "border-b-2 border-primary font-bold" : "text-muted-foreground"}`;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" dir="rtl">
      <div className="absolute inset-0 bg-black/60" onClick={onClose} />
      <div className="relative z-10 w-full max-w-md border border-border bg-card shadow-xl">
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <h3 className="flex items-center gap-2 font-bold"><Share2 className="h-4 w-4" /> مشاركة: {reportName}</h3>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
        </div>
        <div className="space-y-3 p-5">
          <div className="flex border-b border-border">
            <button className={tabCls("department")} onClick={() => { setTarget("department"); setSelected(""); }}>إدارة</button>
            <button className={tabCls("role")} onClick={() => { setTarget("role"); setSelected(""); }}>دور</button>
            <button className={tabCls("user")} onClick={() => { setTarget("user"); setSelected(""); }}>مستخدم</button>
          </div>
          <select value={selected} onChange={(e) => setSelected(e.target.value)} className="h-10 w-full border border-border bg-secondary px-3 text-sm">
            <option value="">— اختر —</option>
            {options.map((o) => <option key={o.id} value={o.id}>{o.label}</option>)}
          </select>
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={canEdit} onChange={(e) => setCanEdit(e.target.checked)} /> السماح بالتعديل</label>
          <button onClick={onShare} disabled={!selected || saving} className="inline-flex h-10 w-full items-center justify-center gap-2 bg-primary text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80 disabled:opacity-40">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Share2 className="h-4 w-4" />} مشاركة
          </button>
          {shares.length > 0 && (
            <div className="border-t border-border pt-3">
              <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">المشاركات الحالية</p>
              <div className="space-y-1">
                {shares.map((s) => (
                  <div key={s.id} className="flex items-center justify-between border border-border px-3 py-2 text-sm">
                    <span>{labelFor(s)} {s.canEdit && <span className="text-xs text-primary">(تعديل)</span>}</span>
                    <button onClick={() => onRevoke(s.id)} className="text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
```

- [ ] **Step 2: Build.** Run: `npx next build`
Expected: compiles with 0 TypeScript errors.

> Confirm `orgLabel` and `OrgOption` are exported from `@/lib/api/org` (the dashboard share dialog imports them the same way). If `ReportShare` is not exported from `@/lib/api/reports`, STOP and report (it is — `export interface ReportShare {...}`).

- [ ] **Step 3: Commit**

```bash
git add src/components/reports/share-dialog.tsx
git commit -m "feat(reports): share dialog (user/role/department + canEdit) mirroring dashboard"
```

---

## Task 2: Reports sidebar (views + folders + tags)

**Files:**
- Create: `src/components/reports/reports-sidebar.tsx`

**Interfaces:**
- Consumes: `createReportFolder`, `updateReportFolder`, `deleteReportFolder`, `createReportTag`, `deleteReportTag`, `ReportFolder`, `ReportTag`.
- Produces: `ReportsSidebar({ view, folderId, tagId, folders, tags, canEdit, onSelectView, onSelectFolder, onSelectTag, onFoldersChanged, onTagsChanged })` where `view: "" | "favorites" | "recent" | "pinned"`, `folderId: string`, `tagId: string`.

- [ ] **Step 1: Create `src/components/reports/reports-sidebar.tsx`:**

```tsx
"use client";

import { useState } from "react";
import { Folder, Pencil, Plus, Star, Clock, Pin, Trash2, Tag } from "lucide-react";
import { toast } from "sonner";
import { createReportFolder, updateReportFolder, deleteReportFolder, createReportTag, deleteReportTag, ReportFolder, ReportTag } from "@/lib/api/reports";

type View = "" | "favorites" | "recent" | "pinned";

export function ReportsSidebar({
  view, folderId, tagId, folders, tags, canEdit,
  onSelectView, onSelectFolder, onSelectTag, onFoldersChanged, onTagsChanged,
}: {
  view: View; folderId: string; tagId: string; folders: ReportFolder[]; tags: ReportTag[]; canEdit: boolean;
  onSelectView: (v: View) => void; onSelectFolder: (id: string) => void; onSelectTag: (id: string) => void;
  onFoldersChanged: () => void; onTagsChanged: () => void;
}) {
  const [newFolder, setNewFolder] = useState("");
  const [newTag, setNewTag] = useState("");
  const [renaming, setRenaming] = useState<string | null>(null);
  const [renameVal, setRenameVal] = useState("");

  const VIEWS: { v: View; l: string; icon: typeof Star }[] = [
    { v: "", l: "الكل", icon: Folder }, { v: "favorites", l: "المفضلة", icon: Star },
    { v: "recent", l: "الأخيرة", icon: Clock }, { v: "pinned", l: "المثبّتة", icon: Pin },
  ];

  const addFolder = async () => {
    if (!newFolder.trim()) return;
    try { await createReportFolder({ nameAr: newFolder, nameEn: newFolder }); setNewFolder(""); onFoldersChanged(); }
    catch { toast.error("تعذر إنشاء المجلد"); }
  };
  const saveRename = async (f: ReportFolder) => {
    try { await updateReportFolder(f.id, { nameAr: renameVal, nameEn: renameVal, parentFolderId: f.parentFolderId ?? null }); setRenaming(null); onFoldersChanged(); }
    catch { toast.error("تعذر إعادة التسمية"); }
  };
  const removeFolder = async (id: string) => {
    try { await deleteReportFolder(id); if (folderId === id) onSelectFolder(""); onFoldersChanged(); }
    catch { toast.error("تعذر حذف المجلد"); }
  };
  const addTag = async () => {
    if (!newTag.trim()) return;
    try { await createReportTag({ name: newTag }); setNewTag(""); onTagsChanged(); }
    catch { toast.error("تعذر إنشاء الوسم"); }
  };
  const removeTag = async (id: string) => {
    try { await deleteReportTag(id); if (tagId === id) onSelectTag(""); onTagsChanged(); }
    catch { toast.error("تعذر حذف الوسم"); }
  };

  const rowCls = (active: boolean) => `flex w-full items-center gap-2 px-2 py-1.5 text-sm text-right ${active ? "bg-primary/10 text-primary" : "hover:bg-secondary/60"}`;

  return (
    <aside className="w-full md:w-56 shrink-0 space-y-5">
      <div>
        <p className="mb-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">العروض</p>
        {VIEWS.map((v) => {
          const Icon = v.icon;
          return (
            <button key={v.v} onClick={() => { onSelectFolder(""); onSelectTag(""); onSelectView(v.v); }}
              className={rowCls(view === v.v && !folderId && !tagId)}>
              <Icon className="h-4 w-4" /> {v.l}
            </button>
          );
        })}
      </div>

      <div>
        <p className="mb-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">المجلدات</p>
        {folders.map((f) => (
          <div key={f.id} className="group flex items-center">
            {renaming === f.id ? (
              <input autoFocus value={renameVal} onChange={(e) => setRenameVal(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") saveRename(f); if (e.key === "Escape") setRenaming(null); }}
                onBlur={() => saveRename(f)} className="h-8 flex-1 border border-border bg-background px-2 text-sm" />
            ) : (
              <>
                <button onClick={() => { onSelectView(""); onSelectTag(""); onSelectFolder(f.id); }} className={`${rowCls(folderId === f.id)} flex-1`}>
                  <Folder className="h-4 w-4" /> {f.nameAr || f.nameEn}
                </button>
                {canEdit && (
                  <span className="flex opacity-0 group-hover:opacity-100">
                    <button title="إعادة تسمية" onClick={() => { setRenaming(f.id); setRenameVal(f.nameAr || f.nameEn); }} className="p-1 text-muted-foreground hover:text-foreground"><Pencil className="h-3.5 w-3.5" /></button>
                    <button title="حذف" onClick={() => removeFolder(f.id)} className="p-1 text-muted-foreground hover:text-destructive"><Trash2 className="h-3.5 w-3.5" /></button>
                  </span>
                )}
              </>
            )}
          </div>
        ))}
        {canEdit && (
          <div className="mt-1 flex items-center gap-1">
            <input value={newFolder} onChange={(e) => setNewFolder(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") addFolder(); }} placeholder="مجلد جديد" className="h-8 flex-1 border border-border bg-background px-2 text-xs" />
            <button onClick={addFolder} className="p-1 text-primary"><Plus className="h-4 w-4" /></button>
          </div>
        )}
      </div>

      <div>
        <p className="mb-1 text-xs font-bold uppercase tracking-wider text-muted-foreground">الوسوم</p>
        <div className="flex flex-wrap gap-1">
          {tags.map((t) => (
            <span key={t.id} className={`inline-flex items-center gap-1 border px-2 py-0.5 text-xs ${tagId === t.id ? "border-primary bg-primary/10 text-primary" : "border-border bg-secondary"}`}>
              <button onClick={() => { onSelectView(""); onSelectFolder(""); onSelectTag(tagId === t.id ? "" : t.id); }} className="inline-flex items-center gap-1"><Tag className="h-3 w-3" /> {t.name}</button>
              {canEdit && <button onClick={() => removeTag(t.id)} className="text-muted-foreground hover:text-destructive"><Trash2 className="h-3 w-3" /></button>}
            </span>
          ))}
        </div>
        {canEdit && (
          <div className="mt-1 flex items-center gap-1">
            <input value={newTag} onChange={(e) => setNewTag(e.target.value)} onKeyDown={(e) => { if (e.key === "Enter") addTag(); }} placeholder="وسم جديد" className="h-8 flex-1 border border-border bg-background px-2 text-xs" />
            <button onClick={addTag} className="p-1 text-primary"><Plus className="h-4 w-4" /></button>
          </div>
        )}
      </div>
    </aside>
  );
}
```

- [ ] **Step 2: Build.** Run: `npx next build`
Expected: compiles with 0 TypeScript errors (the component is unused so far — fine).

- [ ] **Step 3: Commit**

```bash
git add src/components/reports/reports-sidebar.tsx
git commit -m "feat(reports): sidebar (views + folder CRUD + tag CRUD/filter)"
```

---

## Task 3: List page — sidebar layout + row organization actions + sharing

**Files:**
- Modify (full replace): `src/app/(dashboard)/reports/page.tsx`

**Interfaces:**
- Consumes: `ReportsSidebar` (Task 2), `ReportShareDialog` (Task 1), and canonical client calls (`getReports`, `getReportFolders`, `getReportTags`, `getReportShares`, `toggleReportFavorite`, `toggleReportPin`, `setReportFolder`, `assignReportTag`, `unassignReportTag`, `exportReport`).

- [ ] **Step 1: Replace `src/app/(dashboard)/reports/page.tsx`** with:

```tsx
"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { BarChart3, FileSpreadsheet, FileText, FileType, Loader2, Pin, RefreshCw, Share2, Star, Tag as TagIcon } from "lucide-react";
import { toast } from "sonner";
import { usePermission } from "@/lib/permissions";
import {
  getReports, exportReport, getReportFolders, getReportTags, getReportShares,
  toggleReportFavorite, toggleReportPin, setReportFolder, assignReportTag, unassignReportTag,
  ReportDefinition, ExportFormat, ReportFolder, ReportTag, ReportShare,
} from "@/lib/api/reports";
import { ReportsSidebar } from "@/components/reports/reports-sidebar";
import { ReportShareDialog } from "@/components/reports/share-dialog";

type View = "" | "favorites" | "recent" | "pinned";

const FORMATS: { key: ExportFormat; label: string; icon: typeof FileText }[] = [
  { key: "excel", label: "Excel", icon: FileSpreadsheet },
  { key: "csv", label: "CSV", icon: FileText },
  { key: "pdf", label: "PDF", icon: FileType },
  { key: "sif", label: "WPS/SIF", icon: FileText },
];

export default function ReportsPage() {
  const { allowed: canExport } = usePermission("Platform.Reports.Export");
  const { allowed: canCreate } = usePermission("Platform.Reports.Create");
  const { allowed: canEdit } = usePermission("Platform.Reports.Edit");

  const [reports, setReports] = useState<ReportDefinition[]>([]);
  const [folders, setFolders] = useState<ReportFolder[]>([]);
  const [tags, setTags] = useState<ReportTag[]>([]);
  const [view, setView] = useState<View>("");
  const [folderId, setFolderId] = useState("");
  const [tagId, setTagId] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const [tagPanel, setTagPanel] = useState<string | null>(null); // reportId whose tag panel is open
  const [share, setShare] = useState<{ report: ReportDefinition; shares: ReportShare[] } | null>(null);

  const fetchReports = useCallback(async () => {
    setLoading(true); setError(false);
    try {
      const res = await getReports({ pageSize: 100, view: view || undefined, folderId: folderId || undefined, tagId: tagId || undefined });
      setReports(res.items ?? []);
    } catch { setError(true); }
    finally { setLoading(false); }
  }, [view, folderId, tagId]);

  const fetchFolders = useCallback(() => { getReportFolders().then(setFolders).catch(() => {}); }, []);
  const fetchTags = useCallback(() => { getReportTags().then(setTags).catch(() => {}); }, []);

  useEffect(() => { queueMicrotask(() => { fetchReports(); }); }, [fetchReports]);
  useEffect(() => { queueMicrotask(() => { fetchFolders(); fetchTags(); }); }, [fetchFolders, fetchTags]);

  const doExport = async (report: ReportDefinition, format: ExportFormat) => {
    const key = `${report.id}:${format}`; setBusy(key);
    try { await exportReport(report.id, format, report.code || "report"); toast.success("تم بدء تنزيل التقرير"); }
    catch { /* toast surfaced in exportReport */ }
    finally { setBusy(null); }
  };

  const toggleFav = async (r: ReportDefinition) => { try { await toggleReportFavorite(r.id); await fetchReports(); } catch { toast.error("تعذر التحديث"); } };
  const togglePin = async (r: ReportDefinition) => { try { await toggleReportPin(r.id); await fetchReports(); } catch { toast.error("تعذر التحديث"); } };
  const moveFolder = async (r: ReportDefinition, fid: string) => { try { await setReportFolder(r.id, fid || null); await fetchReports(); toast.success("تم النقل"); } catch { toast.error("تعذر النقل"); } };
  const toggleTag = async (r: ReportDefinition, tag: ReportTag, has: boolean) => {
    try { has ? await unassignReportTag(r.id, tag.id) : await assignReportTag(r.id, tag.id); await fetchReports(); }
    catch { toast.error("تعذر تحديث الوسوم"); }
  };
  const openShare = async (r: ReportDefinition) => {
    try { setShare({ report: r, shares: await getReportShares(r.id) }); }
    catch { toast.error("تعذر تحميل المشاركات"); }
  };
  const refreshShares = async () => {
    if (!share) return;
    try { setShare({ report: share.report, shares: await getReportShares(share.report.id) }); } catch { /* ignore */ }
  };

  return (
    <div className="space-y-6" dir="rtl">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">التقارير</h1>
          <p className="text-sm text-muted-foreground mt-1">التقارير والإحصائيات — تنظيم وتشغيل وتصدير</p>
        </div>
        <div className="flex items-center gap-2">
          {canCreate && <Link href="/reports/builder" className="inline-flex h-9 items-center gap-2 bg-primary px-3 text-sm text-primary-foreground hover:bg-primary/90">+ تقرير جديد</Link>}
          <button onClick={fetchReports} className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70"><RefreshCw className="h-4 w-4" /> تحديث</button>
        </div>
      </div>

      <div className="flex flex-col gap-6 md:flex-row">
        <ReportsSidebar
          view={view} folderId={folderId} tagId={tagId} folders={folders} tags={tags} canEdit={canEdit}
          onSelectView={setView} onSelectFolder={setFolderId} onSelectTag={setTagId}
          onFoldersChanged={fetchFolders} onTagsChanged={fetchTags}
        />

        <div className="flex-1 min-w-0">
          {loading ? (
            <div className="border border-border bg-card p-12 flex items-center justify-center"><Loader2 className="h-8 w-8 animate-spin text-muted-foreground" /></div>
          ) : error ? (
            <div className="border border-border bg-card p-12 flex flex-col items-center text-center">
              <BarChart3 className="h-10 w-10 text-muted-foreground mb-3" />
              <p className="text-sm text-muted-foreground mb-2">تعذر تحميل التقارير</p>
              <button onClick={fetchReports} className="text-sm underline">إعادة المحاولة</button>
            </div>
          ) : reports.length === 0 ? (
            <div className="border border-border bg-card p-12 flex flex-col items-center text-center">
              <BarChart3 className="h-12 w-12 text-muted-foreground mb-4" />
              <h2 className="text-lg font-semibold mb-2">لا توجد تقارير</h2>
              <p className="text-sm text-muted-foreground">لا توجد تقارير مطابقة للمرشّح الحالي.</p>
            </div>
          ) : (
            <div className="border border-border bg-card overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border text-right text-xs uppercase tracking-wider text-muted-foreground">
                    <th className="px-4 py-3 font-medium">التقرير</th>
                    <th className="px-4 py-3 font-medium">النوع</th>
                    <th className="px-4 py-3 font-medium">تنظيم</th>
                    <th className="px-4 py-3 font-medium">إجراءات</th>
                  </tr>
                </thead>
                <tbody>
                  {reports.map((r) => (
                    <tr key={r.id} className="border-b border-border/60 last:border-0 hover:bg-secondary/40 align-top">
                      <td className="px-4 py-3">
                        <Link href={`/reports/${r.id}`} className="font-medium hover:underline">{r.nameAr || r.nameEn}</Link>
                        <div className="text-xs text-muted-foreground">{r.nameEn}{r.code ? ` · ${r.code}` : ""}</div>
                        {r.tags?.length > 0 && (
                          <div className="mt-1 flex flex-wrap gap-1">
                            {r.tags.map((t) => <span key={t.id} className="inline-block border border-border bg-secondary px-1.5 py-0.5 text-[10px] text-muted-foreground">{t.name}</span>)}
                          </div>
                        )}
                      </td>
                      <td className="px-4 py-3 text-muted-foreground">{r.reportType}</td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-1.5">
                          <button title="مفضّلة" onClick={() => toggleFav(r)} className={r.isFavorite ? "text-amber-500" : "text-muted-foreground hover:text-foreground"}><Star className="h-4 w-4" fill={r.isFavorite ? "currentColor" : "none"} /></button>
                          <button title="تثبيت" onClick={() => togglePin(r)} className={r.isPinned ? "text-primary" : "text-muted-foreground hover:text-foreground"}><Pin className="h-4 w-4" fill={r.isPinned ? "currentColor" : "none"} /></button>
                          {canEdit && (
                            <>
                              <select value={r.folderId ?? ""} onChange={(e) => moveFolder(r, e.target.value)} title="المجلد" className="h-7 border border-border bg-background px-1 text-xs">
                                <option value="">بدون مجلد</option>
                                {folders.map((f) => <option key={f.id} value={f.id}>{f.nameAr || f.nameEn}</option>)}
                              </select>
                              <div className="relative">
                                <button title="الوسوم" onClick={() => setTagPanel(tagPanel === r.id ? null : r.id)} className="text-muted-foreground hover:text-foreground"><TagIcon className="h-4 w-4" /></button>
                                {tagPanel === r.id && (
                                  <div className="absolute z-20 mt-1 w-40 border border-border bg-card p-2 shadow-lg">
                                    {tags.length === 0 && <p className="text-xs text-muted-foreground">لا توجد وسوم</p>}
                                    {tags.map((t) => {
                                      const has = (r.tags ?? []).some((x) => x.id === t.id);
                                      return (
                                        <label key={t.id} className="flex items-center gap-2 py-0.5 text-xs">
                                          <input type="checkbox" checked={has} onChange={() => toggleTag(r, t, has)} /> {t.name}
                                        </label>
                                      );
                                    })}
                                  </div>
                                )}
                              </div>
                              <button title="مشاركة" onClick={() => openShare(r)} className="text-muted-foreground hover:text-foreground"><Share2 className="h-4 w-4" /></button>
                            </>
                          )}
                        </div>
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center gap-1.5">
                          {canEdit && <Link href={`/reports/builder/${r.id}`} className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70">تعديل</Link>}
                          {canExport && FORMATS.map((f) => {
                            const key = `${r.id}:${f.key}`; const Icon = f.icon;
                            return (
                              <button key={f.key} onClick={() => doExport(r, f.key)} disabled={busy !== null} title={`تصدير ${f.label}`}
                                className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70 disabled:opacity-50">
                                {busy === key ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Icon className="h-3.5 w-3.5" />} {f.label}
                              </button>
                            );
                          })}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>

      {share && (
        <ReportShareDialog reportId={share.report.id} reportName={share.report.nameAr || share.report.nameEn} shares={share.shares}
          onClose={() => setShare(null)} onChanged={refreshShares} />
      )}
    </div>
  );
}
```

- [ ] **Step 2: Build.** Run: `npx next build`
Expected: compiles with 0 TypeScript errors; `/reports` route builds.

- [ ] **Step 3: Commit**

```bash
git add "src/app/(dashboard)/reports/page.tsx"
git commit -m "feat(reports): list sidebar (views/folders/tags) + row favorite/pin/folder/tags/share"
```

---

## Final verification & deploy
- [ ] `npx next build` → 0 errors.
- [ ] Push → Vercel auto-deploys the FE. No API deploy, no migration.
- [ ] Live-verify: create a folder + tag; assign a report to the folder (row select) and toggle a tag; filter by folder/tag/favorites/pinned; favorite + pin a report; share a report with a role/department then revoke.

## Self-review notes (author)
- Spec Component 1 (share dialog) → Task 1; Component 2 (sidebar) → Task 2; Component 3 (list layout + row actions + sharing) → Task 3. All covered.
- No backend, no migration; canonical client + dashboard share pattern reused, unmodified.
- Type consistency: `View = "" | "favorites" | "recent" | "pinned"` used in sidebar + page; `ReportFolder`/`ReportTag`/`ReportShare`/`ReportDefinition` from the client; `setReportFolder(id, folderId|null)`, `toggleReportFavorite/Pin`, `assign/unassignReportTag(reportId, tagId)`, `getReportShares/removeReportShare(reportId, shareId)` signatures match the client.
- Known limits (carried): flat folder list (no nesting UI), neutral tag chips (color not surfaced), recent ordering backend-driven.
