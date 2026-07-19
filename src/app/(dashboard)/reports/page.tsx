"use client";

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { BarChart3, FileSpreadsheet, FileText, FileType, Loader2, Pin, RefreshCw, Settings2, Share2, Sparkles, Star, Tag as TagIcon, Zap } from "lucide-react";
import { toast } from "sonner";
import { usePermission } from "@/lib/permissions";
import {
  getReports, exportReport, getReportFolders, getReportTags, getReportShares,
  toggleReportFavorite, toggleReportPin, setReportFolder, assignReportTag, unassignReportTag,
  seedSystemReports, seedReportableObjects,
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
  const [seeding, setSeeding] = useState(false);
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

  const seedStandard = async () => {
    setSeeding(true);
    try {
      // Ensure the reportable objects (incl. the master-data table) are registered first, so leave
      // types / nationalities / request types resolve to real names — then (re)generate the reports.
      await seedReportableObjects().catch(() => {});
      const res = await seedSystemReports();
      toast.success(res.created > 0 ? `تم تجهيز ${res.created} تقرير أساسي` : "لا توجد مواضيع متاحة");
      await fetchReports();
    } catch { toast.error("تعذر إنشاء التقارير الأساسية"); }
    finally { setSeeding(false); }
  };

  // "التقارير الأساسية" (standard, auto-seeded) vs. everything else.
  const standard = reports.filter((r) => (r.code ?? "").startsWith("SYS_"));
  const custom = reports.filter((r) => !(r.code ?? "").startsWith("SYS_"));
  const filtered = view || folderId || tagId; // when filtering, don't split — show the flat result
  const tableRows = filtered ? reports : custom;

  return (
    <div className="space-y-6" dir="rtl">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">التقارير</h1>
          <p className="text-sm text-muted-foreground mt-1">التقارير والإحصائيات — تنظيم وتشغيل وتصدير</p>
        </div>
        <div className="flex items-center gap-2">
          {canCreate && <Link href="/reports/new" className="inline-flex h-9 items-center gap-2 bg-primary px-3 text-sm text-primary-foreground hover:bg-primary/90"><Zap className="h-4 w-4" /> تقرير جديد</Link>}
          {canCreate && <Link href="/reports/builder" title="المنشئ المتقدم" className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70"><Settings2 className="h-4 w-4" /> متقدم</Link>}
          <button onClick={fetchReports} className="inline-flex h-9 items-center gap-2 border border-border bg-secondary px-3 text-sm hover:bg-secondary/70"><RefreshCw className="h-4 w-4" /> تحديث</button>
        </div>
      </div>

      <div className="flex flex-col gap-6 md:flex-row">
        <ReportsSidebar
          view={view} folderId={folderId} tagId={tagId} folders={folders} tags={tags} canEdit={canEdit}
          onSelectView={setView} onSelectFolder={setFolderId} onSelectTag={setTagId}
          onFoldersChanged={fetchFolders} onTagsChanged={fetchTags}
        />

        <div className="flex-1 min-w-0 space-y-6">
          {/* التقارير الأساسية — auto-seeded standard reports, one-click launch */}
          {!filtered && !loading && !error && (
            <div className="space-y-2">
              <div className="flex items-center justify-between">
                <h2 className="flex items-center gap-2 text-sm font-semibold"><Sparkles className="h-4 w-4 text-primary" /> التقارير الأساسية</h2>
                {canCreate && (
                  <button onClick={seedStandard} disabled={seeding} className="inline-flex h-8 items-center gap-1.5 border border-border bg-secondary px-2.5 text-xs hover:bg-secondary/70 disabled:opacity-50">
                    {seeding ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Sparkles className="h-3.5 w-3.5" />} {standard.length === 0 ? "إنشاء التقارير الأساسية" : "تحديث الأساسية"}
                  </button>
                )}
              </div>
              {standard.length === 0 ? (
                <div className="border border-dashed border-border bg-card p-6 text-center text-xs text-muted-foreground">
                  لا توجد تقارير أساسية بعد — اضغط «إنشاء التقارير الأساسية» لتوليد تقرير قياسي جاهز لكل موضوع.
                </div>
              ) : (
                <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
                  {standard.map((r) => (
                    <Link key={r.id} href={`/reports/${r.id}`} className="flex flex-col gap-1 border border-border bg-card p-3 hover:border-primary hover:bg-secondary/40">
                      <span className="text-sm font-medium truncate">{r.nameAr || r.nameEn}</span>
                      <span className="text-[11px] text-muted-foreground">{r.reportType}</span>
                    </Link>
                  ))}
                </div>
              )}
            </div>
          )}

          {loading ? (
            <div className="border border-border bg-card p-12 flex items-center justify-center"><Loader2 className="h-8 w-8 animate-spin text-muted-foreground" /></div>
          ) : error ? (
            <div className="border border-border bg-card p-12 flex flex-col items-center text-center">
              <BarChart3 className="h-10 w-10 text-muted-foreground mb-3" />
              <p className="text-sm text-muted-foreground mb-2">تعذر تحميل التقارير</p>
              <button onClick={fetchReports} className="text-sm underline">إعادة المحاولة</button>
            </div>
          ) : tableRows.length === 0 ? (
            <div className="border border-border bg-card p-12 flex flex-col items-center text-center">
              <BarChart3 className="h-12 w-12 text-muted-foreground mb-4" />
              <h2 className="text-lg font-semibold mb-2">{filtered ? "لا توجد تقارير" : "لا توجد تقارير مخصّصة"}</h2>
              <p className="text-sm text-muted-foreground">{filtered ? "لا توجد تقارير مطابقة للمرشّح الحالي." : "أنشئ تقريرك الأول عبر «تقرير جديد»."}</p>
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
                  {tableRows.map((r) => (
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
