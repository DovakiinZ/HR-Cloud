"use client";

import { type ElementType, useCallback, useEffect, useMemo, useState } from "react";
import { Check, Download, FileCheck, Inbox, Loader2, Mail, Printer, Send, X, Clock, Eye, FileText } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/lib/api-client";
import { requestIcon } from "@/lib/request-icons";
import {
  approveRequest, cancelRequest, downloadRequestDocument, emailRequestDocument, GeneratedDocInfo,
  getInbox, getMyRequests, getRequestDocuments, getRequestType, getRequestTypes,
  rejectRequest, RequestInstance, RequestType, RequestTypeDetail,
  requestStatusColor, requestStatusLabel, viewRequestDocument,
} from "@/lib/api/request-center";
import { RequestForm } from "@/components/requests/request-form";
import { LeaveRequestWizard } from "@/components/requests/leave-request-wizard";

type Tab = "submit" | "mine" | "inbox";

export default function RequestsPage() {
  const [tab, setTab] = useState<Tab>("submit");
  const [types, setTypes] = useState<RequestType[]>([]);
  const [mine, setMine] = useState<RequestInstance[]>([]);
  const [inbox, setInbox] = useState<RequestInstance[]>([]);
  const [loading, setLoading] = useState(true);
  const [activeType, setActiveType] = useState<RequestTypeDetail | null>(null);
  const [loadingType, setLoadingType] = useState(false);
  const [detail, setDetail] = useState<RequestInstance | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const [t, m, i] = await Promise.all([getRequestTypes(), getMyRequests(), getInbox()]);
      setTypes(t); setMine(m); setInbox(i);
    } catch (e) {
      if (!(e instanceof ApiError) || ![401, 403, 500].includes(e.status)) toast.error("تعذر تحميل الطلبات");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { refresh(); }, [refresh]);

  const openType = async (t: RequestType) => {
    setLoadingType(true);
    try {
      setActiveType(await getRequestType(t.id));
    } catch {
      toast.error("تعذر فتح النموذج");
    } finally {
      setLoadingType(false);
    }
  };

  const system = useMemo(() => types.filter((t) => t.kind === "System"), [types]);
  const dynamic = useMemo(() => types.filter((t) => t.kind !== "System"), [types]);

  const decide = async (id: string, approve: boolean) => {
    try {
      if (approve) await approveRequest(id); else await rejectRequest(id, "");
      toast.success(approve ? "تمت الموافقة" : "تم الرفض");
      setDetail(null);
      await refresh();
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "تعذر تنفيذ الإجراء");
    }
  };

  const doCancel = async (id: string) => {
    try { await cancelRequest(id); toast.success("تم الإلغاء"); setDetail(null); await refresh(); }
    catch (e) { toast.error(e instanceof ApiError ? e.message : "تعذر الإلغاء"); }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold">مركز الطلبات</h1>
          <p className="mt-1 text-sm text-muted-foreground">كل طلب ظاهر هنا قابل للاستخدام فوراً</p>
        </div>
        <button
          onClick={() => { setTab("submit"); if (typeof window !== "undefined") window.scrollTo({ top: 0, behavior: "smooth" }); }}
          className="inline-flex h-10 shrink-0 items-center gap-2 bg-primary px-4 text-sm font-bold text-primary-foreground hover:bg-primary/90"
        >
          <Send className="h-4 w-4" /> طلب جديد
        </button>
      </div>

      {/* Tabs */}
      <div className="flex flex-wrap items-center gap-1 border-b border-border">
        <TabBtn active={tab === "submit"} onClick={() => setTab("submit")} icon={Send} label="تقديم طلب" />
        <TabBtn active={tab === "mine"} onClick={() => setTab("mine")} icon={FileCheck} label={`طلباتي (${mine.length})`} />
        <TabBtn active={tab === "inbox"} onClick={() => setTab("inbox")} icon={Inbox} label={`الموافقات (${inbox.length})`} />
      </div>

      {loading ? (
        <div className="flex h-64 items-center justify-center text-muted-foreground"><Loader2 className="h-6 w-6 animate-spin" /></div>
      ) : tab === "submit" ? (
        <div className="space-y-6">
          <Catalog title="الطلبات النظامية" subtitle="جاهزة وتعمل مباشرة" items={system} onPick={openType} />
          {dynamic.length > 0 && <Catalog title="طلبات مخصصة" subtitle="أنشأتها المنشأة" items={dynamic} onPick={openType} />}
          {types.length === 0 && (
            <div className="border border-dashed border-border py-16 text-center text-muted-foreground">
              لا توجد أنواع طلبات بعد — اطلب من المسؤول تفعيل الطلبات النظامية.
            </div>
          )}
        </div>
      ) : tab === "mine" ? (
        <RequestList items={mine} emptyText="لم تقدّم أي طلبات بعد" onOpen={setDetail} />
      ) : (
        <RequestList items={inbox} emptyText="لا توجد طلبات بانتظار موافقتك" onOpen={setDetail} />
      )}

      {/* Submit modal */}
      {(activeType || loadingType) && (
        <Modal onClose={() => setActiveType(null)} title={activeType?.nameAr ?? "..."}>
          {loadingType || !activeType ? (
            <div className="flex h-32 items-center justify-center"><Loader2 className="h-5 w-5 animate-spin" /></div>
          ) : activeType.isLeaveRequest ? (
            <LeaveRequestWizard type={activeType} onCancel={() => setActiveType(null)} onSubmitted={() => { setActiveType(null); setTab("mine"); refresh(); }} />
          ) : (
            <RequestForm type={activeType} onCancel={() => setActiveType(null)} onSubmitted={() => { setActiveType(null); setTab("mine"); refresh(); }} />
          )}
        </Modal>
      )}

      {/* Detail drawer */}
      {detail && (
        <DetailDrawer
          instance={detail}
          onClose={() => setDetail(null)}
          onApprove={tab === "inbox" ? () => decide(detail.id, true) : undefined}
          onReject={tab === "inbox" ? () => decide(detail.id, false) : undefined}
          onCancel={tab === "mine" && ["Submitted", "InProgress"].includes(detail.status) ? () => doCancel(detail.id) : undefined}
        />
      )}
    </div>
  );
}

function TabBtn({ active, onClick, icon: Icon, label }: { active: boolean; onClick: () => void; icon: ElementType; label: string }) {
  return (
    <button onClick={onClick} className={`-mb-px flex items-center gap-1.5 border-b-2 px-4 py-2 text-sm ${active ? "border-primary font-bold" : "border-transparent text-muted-foreground hover:text-foreground"}`}>
      <Icon className="h-4 w-4" /> {label}
    </button>
  );
}

function Catalog({ title, subtitle, items, onPick }: { title: string; subtitle: string; items: RequestType[]; onPick: (t: RequestType) => void }) {
  if (items.length === 0) return null;
  return (
    <div>
      <div className="mb-3">
        <h2 className="text-sm font-bold uppercase tracking-wider text-muted-foreground">{title}</h2>
        <p className="text-xs text-muted-foreground">{subtitle}</p>
      </div>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {items.map((t) => {
          const Icon = requestIcon(t.icon);
          return (
            <button key={t.id} onClick={() => onPick(t)} className="flex items-start gap-3 border border-border bg-card p-4 text-right transition-colors hover:border-primary/60">
              <span className="flex h-10 w-10 shrink-0 items-center justify-center" style={{ background: `${t.color ?? "#FBBF24"}1a`, color: t.color ?? "#FBBF24" }}>
                <Icon className="h-5 w-5" />
              </span>
              <span className="min-w-0">
                <span className="block font-medium">{t.nameAr}</span>
                <span className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">
                  {t.hasWorkflow && <span className="inline-flex items-center gap-0.5"><Clock className="h-3 w-3" /> موافقة</span>}
                  {t.generatesDocument && <span className="inline-flex items-center gap-0.5"><FileText className="h-3 w-3" /> مستند</span>}
                </span>
              </span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

function RequestList({ items, emptyText, onOpen }: { items: RequestInstance[]; emptyText: string; onOpen: (r: RequestInstance) => void }) {
  if (items.length === 0) return <div className="border border-dashed border-border py-16 text-center text-muted-foreground">{emptyText}</div>;
  return (
    <div className="space-y-2">
      {items.map((r) => (
        <button key={r.id} onClick={() => onOpen(r)} className="flex w-full items-center justify-between gap-3 border border-border bg-card px-4 py-3 text-right hover:bg-muted/40">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <span className="font-medium">{r.requestTypeNameAr}</span>
              <span className="font-mono text-xs text-muted-foreground">{r.requestNumber}</span>
            </div>
            <div className="mt-0.5 text-xs text-muted-foreground">
              {new Date(r.submittedAt).toLocaleDateString("ar")} {r.daysCount ? `· ${r.daysCount} يوم` : ""}
            </div>
          </div>
          <span className={`shrink-0 border px-2 py-1 text-xs ${requestStatusColor(r.status)}`}>{requestStatusLabel(r.status)}</span>
        </button>
      ))}
    </div>
  );
}

function Modal({ children, onClose, title }: { children: React.ReactNode; onClose: () => void; title: string }) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div className="absolute inset-0 bg-black/60" onClick={onClose} />
      <div className="relative z-10 max-h-[85vh] w-full max-w-lg overflow-auto border border-border bg-card">
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <h3 className="font-bold">{title}</h3>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
        </div>
        <div className="p-5">{children}</div>
      </div>
    </div>
  );
}

function DocumentsPanel({ instanceId, requestNumber, hasPrimary, status }: { instanceId: string; requestNumber: string; hasPrimary: boolean; status: string }) {
  const [docs, setDocs] = useState<GeneratedDocInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState<string | null>(null);

  useEffect(() => {
    getRequestDocuments(instanceId).then(setDocs).catch(() => setDocs([])).finally(() => setLoading(false));
  }, [instanceId]);

  // Approved/Completed requests can always print the official document (the renderer applies
  // company branding + a structured fallback even when no template is assigned).
  const printable = status === "Approved" || status === "Completed";
  if (loading) return null;
  if (docs.length === 0 && !hasPrimary && !printable) return null;

  const run = async (key: string, fn: () => Promise<void>, errMsg: string) => {
    setBusy(key);
    try { await fn(); } catch { toast.error(errMsg); } finally { setBusy(null); }
  };

  // Fallback to the request's primary document when no per-mapping rows exist.
  const rows: GeneratedDocInfo[] = docs.length > 0 ? docs : [{ id: "", documentTemplateId: "", templateNameAr: "المستند الرسمي", fileName: `${requestNumber}.pdf` }];

  return (
    <div className="space-y-2">
      <p className="text-xs font-bold uppercase tracking-wider text-muted-foreground">المستندات</p>
      <div className="space-y-2">
        {rows.map((d, i) => {
          const docId = d.id || undefined;
          const fileName = d.fileName || `${requestNumber}.pdf`;
          const k = d.id || `primary${i}`;
          return (
            <div key={k} className="space-y-2 border border-border bg-card p-3">
              <div className="flex items-center gap-2 text-sm font-medium"><FileText className="h-4 w-4 text-green-400" /> {d.templateNameAr ?? "مستند"}</div>
              <div className="flex flex-wrap gap-1.5">
                <ActionBtn icon={Eye} label="عرض" busy={busy === `v${k}`} onClick={() => run(`v${k}`, () => viewRequestDocument(instanceId, docId, false), "تعذر العرض")} />
                <ActionBtn icon={Download} label="تحميل" busy={busy === `d${k}`} onClick={() => run(`d${k}`, () => downloadRequestDocument(instanceId, fileName, docId), "تعذر التحميل")} />
                <ActionBtn icon={Printer} label="طباعة" busy={busy === `p${k}`} onClick={() => run(`p${k}`, () => viewRequestDocument(instanceId, docId, true), "تعذر الطباعة")} />
                {docId && <ActionBtn icon={Mail} label="إرسال بريد" busy={busy === `e${k}`} onClick={() => run(`e${k}`, async () => { await emailRequestDocument(instanceId, docId); toast.success("تم إرسال البريد"); }, "تعذر الإرسال")} />}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function ActionBtn({ icon: Icon, label, onClick, busy }: { icon: ElementType; label: string; onClick: () => void; busy?: boolean }) {
  return (
    <button onClick={onClick} disabled={busy} className="inline-flex items-center gap-1 border border-border px-2 py-1 text-xs hover:bg-muted disabled:opacity-50">
      {busy ? <Loader2 className="h-3 w-3 animate-spin" /> : <Icon className="h-3 w-3" />} {label}
    </button>
  );
}

function DetailDrawer({ instance, onClose, onApprove, onReject, onCancel }: {
  instance: RequestInstance; onClose: () => void; onApprove?: () => void; onReject?: () => void; onCancel?: () => void;
}) {
  return (
    <div className="fixed inset-0 z-50 flex justify-start">
      <div className="absolute inset-0 bg-black/50" onClick={onClose} />
      <div className="relative z-10 flex h-full w-full max-w-lg flex-col border-l border-border bg-background">
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <div>
            <h3 className="font-bold">{instance.requestTypeNameAr}</h3>
            <p className="font-mono text-xs text-muted-foreground">{instance.requestNumber}</p>
          </div>
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground"><X className="h-5 w-5" /></button>
        </div>

        <div className="flex-1 space-y-5 overflow-auto p-5">
          <div className="flex flex-wrap items-center gap-2">
            <span className={`border px-2 py-1 text-xs ${requestStatusColor(instance.status)}`}>{requestStatusLabel(instance.status)}</span>
          </div>

          <DocumentsPanel instanceId={instance.id} requestNumber={instance.requestNumber} hasPrimary={!!instance.generatedDocumentId} status={instance.status} />

          {(instance.startDate || instance.daysCount) && (
            <div className="text-sm text-muted-foreground">
              {instance.startDate && <>من {new Date(instance.startDate).toLocaleDateString("ar")} </>}
              {instance.endDate && <>إلى {new Date(instance.endDate).toLocaleDateString("ar")} </>}
              {instance.daysCount && <>· {instance.daysCount} يوم</>}
            </div>
          )}

          {instance.approvals.length > 0 && (
            <div>
              <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">سلسلة الموافقات</p>
              <div className="space-y-2">
                {instance.approvals.map((a) => (
                  <div key={a.stepOrder} className="flex items-center justify-between border border-border px-3 py-2 text-sm">
                    <span>{a.stepOrder}. {a.stepNameAr}</span>
                    <span className={`border px-2 py-0.5 text-xs ${requestStatusColor(a.status)}`}>{requestStatusLabel(a.status)}</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div>
            <p className="mb-2 text-xs font-bold uppercase tracking-wider text-muted-foreground">المسار الزمني</p>
            <div className="space-y-2">
              {instance.history.map((h, i) => (
                <div key={i} className="flex items-start gap-2 text-sm">
                  <span className="mt-1 h-2 w-2 shrink-0 rounded-full bg-primary" />
                  <span>{h.noteAr ?? requestStatusLabel(h.toStatus)} <span className="text-xs text-muted-foreground">· {new Date(h.at).toLocaleString("ar")}</span></span>
                </div>
              ))}
            </div>
          </div>
        </div>

        {(onApprove || onReject || onCancel) && (
          <div className="flex items-center gap-2 border-t border-border p-4">
            {onApprove && <button onClick={onApprove} className="inline-flex h-10 flex-1 items-center justify-center gap-2 bg-primary text-sm font-bold uppercase tracking-wider text-primary-foreground hover:bg-primary/80"><Check className="h-4 w-4" /> موافقة</button>}
            {onReject && <button onClick={onReject} className="inline-flex h-10 flex-1 items-center justify-center gap-2 border border-destructive/50 text-sm text-destructive hover:bg-destructive/10"><X className="h-4 w-4" /> رفض</button>}
            {onCancel && <button onClick={onCancel} className="inline-flex h-10 flex-1 items-center justify-center gap-2 border border-border text-sm hover:bg-muted">إلغاء الطلب</button>}
          </div>
        )}
      </div>
    </div>
  );
}
