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
