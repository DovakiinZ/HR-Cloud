"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { ArrowRight, Plus, Search, Loader2, KeyRound, Lock, Mail, Ban, CheckCircle2, LogOut, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Dialog, DialogContent, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Combobox } from "@/components/ui/combobox";
import { ApiError } from "@/lib/api-client";
import { AccessGuard } from "@/components/access/access-guard";
import { usePermissions } from "@/lib/permissions";
import { getEmployees } from "@/lib/api/employees";
import type { Employee } from "@/types";
import {
  listUsers, getUser, createUser, disableUser, enableUser, forceLogout, resetPassword, setPassword,
  changeEmail, setUserRoles, listRoles, listTemplates, assignTemplate, revokeTemplate,
  STATUS_AR, type UserListItem, type UserDetail, type RoleDto, type TemplateDto,
} from "@/lib/api/access";

function notifyError(err: unknown, fallback: string) {
  if (!(err instanceof ApiError) || ![401, 403, 500].includes(err.status)) {
    toast.error(err instanceof ApiError ? err.message : fallback);
  }
}

function StatusBadge({ status }: { status: string }) {
  const map: Record<string, string> = {
    Active: "bg-green-500/10 text-green-600 border-green-500/20",
    Suspended: "bg-zinc-500/10 text-zinc-500 border-zinc-500/20",
    Invited: "bg-amber-500/10 text-amber-600 border-amber-500/20",
  };
  return <Badge variant="outline" className={`text-xs ${map[status] ?? ""}`}>{STATUS_AR[status] ?? status}</Badge>;
}

export default function UsersPage() {
  return (
    <AccessGuard anyOf={["Settings.ManageUsers", "Identity.ViewUsers"]}>
      <UsersInner />
    </AccessGuard>
  );
}

function UsersInner() {
  const { has } = usePermissions();
  const canManage = has("Settings.ManageUsers");

  const [users, setUsers] = useState<UserListItem[]>([]);
  const [roles, setRoles] = useState<RoleDto[]>([]);
  const [templates, setTemplates] = useState<TemplateDto[]>([]);
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState("");

  const [createOpen, setCreateOpen] = useState(false);
  const [accessUser, setAccessUser] = useState<UserDetail | null>(null);
  const [pwdUser, setPwdUser] = useState<UserListItem | null>(null);

  const load = useCallback(async () => {
    try { setUsers(await listUsers()); } catch (err) { notifyError(err, "تعذر تحميل المستخدمين"); }
  }, []);

  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        const [u, r, t, e] = await Promise.all([listUsers(), listRoles(), listTemplates(), getEmployees()]);
        setUsers(u); setRoles(r); setTemplates(t); setEmployees(e);
      } catch (err) { notifyError(err, "تعذر تحميل البيانات"); } finally { setLoading(false); }
    })();
  }, []);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return users;
    return users.filter((u) =>
      u.fullName.toLowerCase().includes(q) || u.email.toLowerCase().includes(q) ||
      (u.employeeName ?? "").toLowerCase().includes(q) || u.roles.some((r) => r.toLowerCase().includes(q)));
  }, [users, search]);

  async function openAccess(id: string) {
    try { setAccessUser(await getUser(id)); } catch (err) { notifyError(err, "تعذر تحميل المستخدم"); }
  }

  async function doAction(fn: () => Promise<unknown>, ok: string) {
    try { await fn(); toast.success(ok); await load(); } catch (err) { notifyError(err, "تعذر تنفيذ العملية"); }
  }

  async function doReset(u: UserListItem) {
    try {
      const r = await resetPassword(u.id);
      await navigator.clipboard?.writeText(r.resetLink).catch(() => {});
      toast.success("تم إنشاء رابط إعادة التعيين ونسخه");
    } catch (err) { notifyError(err, "تعذر إعادة التعيين"); }
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center gap-2 text-sm">
        <Link href="/settings/access" className="text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1">
          <ArrowRight className="h-4 w-4" /> المستخدمون والصلاحيات
        </Link>
        <span className="text-muted-foreground">/</span>
        <span>المستخدمون والوصول</span>
      </div>

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">المستخدمون والوصول</h1>
          <p className="text-sm text-muted-foreground mt-1">حسابات الدخول في النظام وحالتها وأدوارها</p>
        </div>
        {canManage && (
          <Button onClick={() => setCreateOpen(true)} className="h-10 gap-2 font-bold text-sm">
            <Plus className="h-4 w-4" /> مستخدم جديد
          </Button>
        )}
      </div>

      <div className="relative max-w-sm">
        <Search className="absolute right-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="بحث بالاسم أو البريد أو الدور…" className="pr-9 bg-secondary border-border" />
      </div>

      <div className="border border-border">
        <Table>
          <TableHeader>
            <TableRow className="border-border hover:bg-transparent">
              {["المستخدم", "البريد", "الموظف المرتبط", "الأدوار", "الحالة", ""].map((h, i) => (
                <TableHead key={i} className="text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">{h}</TableHead>
              ))}
            </TableRow>
          </TableHeader>
          <TableBody>
            {loading ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground"><Loader2 className="h-4 w-4 animate-spin inline" /> جاري التحميل…</TableCell></TableRow>
            ) : filtered.length === 0 ? (
              <TableRow className="hover:bg-transparent"><TableCell colSpan={6} className="py-12 text-center text-sm text-muted-foreground">لا يوجد مستخدمون</TableCell></TableRow>
            ) : filtered.map((u) => (
              <TableRow key={u.id} className="border-border hover:bg-card/50">
                <TableCell className="font-medium">{u.fullName}</TableCell>
                <TableCell className="text-sm text-muted-foreground" dir="ltr">{u.email}</TableCell>
                <TableCell className="text-sm text-muted-foreground">{u.employeeName ? `${u.employeeName} (${u.employeeNumber})` : "—"}</TableCell>
                <TableCell className="text-sm">{u.roles.length ? u.roles.join("، ") : "—"}</TableCell>
                <TableCell><StatusBadge status={u.status} /></TableCell>
                <TableCell>
                  <div className="flex items-center justify-end gap-1">
                    <button title="إدارة الوصول" onClick={() => openAccess(u.id)} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-primary"><ShieldCheck className="h-4 w-4" /></button>
                    {canManage && <button title="تغيير كلمة المرور" onClick={() => setPwdUser(u)} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-primary"><Lock className="h-4 w-4" /></button>}
                    {canManage && <button title="إنشاء رابط إعادة التعيين" onClick={() => doReset(u)} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-foreground"><KeyRound className="h-4 w-4" /></button>}
                    {canManage && (u.isActive
                      ? <button title="تعطيل" onClick={() => doAction(() => disableUser(u.id), "تم التعطيل")} className="h-8 w-8 inline-flex items-center justify-center text-destructive hover:text-destructive/80"><Ban className="h-4 w-4" /></button>
                      : <button title="تفعيل" onClick={() => doAction(() => enableUser(u.id), "تم التفعيل")} className="h-8 w-8 inline-flex items-center justify-center text-green-600 hover:text-green-700"><CheckCircle2 className="h-4 w-4" /></button>)}
                    {canManage && <button title="إنهاء الجلسات" onClick={() => doAction(() => forceLogout(u.id), "تم إنهاء الجلسات")} className="h-8 w-8 inline-flex items-center justify-center text-muted-foreground hover:text-foreground"><LogOut className="h-4 w-4" /></button>}
                  </div>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {createOpen && (
        <CreateUserDialog roles={roles} employees={employees} onClose={() => setCreateOpen(false)} onCreated={load} />
      )}
      {accessUser && (
        <AccessDialog
          user={accessUser} roles={roles} templates={templates} canManage={canManage}
          onClose={() => setAccessUser(null)} onRefresh={async () => { setAccessUser(await getUser(accessUser.id)); await load(); }}
        />
      )}
      {pwdUser && <ChangePasswordDialog user={pwdUser} onClose={() => setPwdUser(null)} />}
    </div>
  );
}

function ChangePasswordDialog({ user, onClose }: { user: UserListItem; onClose: () => void }) {
  const [pwd, setPwd] = useState("");
  const [confirm, setConfirm] = useState("");
  const [saving, setSaving] = useState(false);

  async function submit() {
    if (pwd.length < 6) { toast.error("كلمة المرور يجب ألا تقل عن 6 أحرف"); return; }
    if (pwd !== confirm) { toast.error("كلمتا المرور غير متطابقتين"); return; }
    setSaving(true);
    try { await setPassword(user.id, pwd); toast.success("تم تغيير كلمة المرور بنجاح"); onClose(); }
    catch (err) { notifyError(err, "تعذر تغيير كلمة المرور"); }
    finally { setSaving(false); }
  }

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !saving) onClose(); }}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader><DialogTitle>تغيير كلمة المرور — {user.fullName || user.email}</DialogTitle></DialogHeader>
        <div className="space-y-3">
          <div className="space-y-1">
            <label className="text-sm text-muted-foreground">كلمة المرور الجديدة</label>
            <Input type="password" value={pwd} onChange={(e) => setPwd(e.target.value)} className="bg-secondary border-border" />
          </div>
          <div className="space-y-1">
            <label className="text-sm text-muted-foreground">تأكيد كلمة المرور</label>
            <Input type="password" value={confirm} onChange={(e) => setConfirm(e.target.value)} className="bg-secondary border-border" />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>إلغاء</Button>
          <Button onClick={submit} disabled={saving}>{saving && <Loader2 className="h-4 w-4 animate-spin" />} حفظ</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function CreateUserDialog({ roles, employees, onClose, onCreated }: {
  roles: RoleDto[]; employees: Employee[]; onClose: () => void; onCreated: () => void;
}) {
  const [email, setEmail] = useState("");
  const [fullName, setFullName] = useState("");
  const [phone, setPhone] = useState("");
  const [roleIds, setRoleIds] = useState<string[]>([]);
  const [employeeId, setEmployeeId] = useState<string | null>(null);
  const [sendInvite, setSendInvite] = useState(true);
  const [password, setPassword] = useState("");
  const [saving, setSaving] = useState(false);

  const empOptions = useMemo(() => employees.map((e) => ({
    value: e.id, label: e.name, hint: e.employeeId,
  })), [employees]);

  async function save() {
    if (!email.trim() || !fullName.trim()) { toast.error("الاسم والبريد مطلوبان"); return; }
    setSaving(true);
    try {
      const r = await createUser({
        email: email.trim(), fullName: fullName.trim(), phone: phone || undefined,
        roleIds: roleIds.length ? roleIds : undefined, employeeId: employeeId || undefined,
        sendInvite, password: sendInvite ? undefined : password || undefined,
      });
      if (r && "effectivePermissions" in r) toast.success("تم إنشاء المستخدم");
      onClose(); onCreated();
    } catch (err) { notifyError(err, "تعذر إنشاء المستخدم"); } finally { setSaving(false); }
  }

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !saving) onClose(); }}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader><DialogTitle>مستخدم جديد</DialogTitle></DialogHeader>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 py-2 max-h-[60vh] overflow-y-auto">
          <div className="space-y-2"><Label className="text-xs font-bold uppercase tracking-wider">الاسم الكامل</Label>
            <Input value={fullName} onChange={(e) => setFullName(e.target.value)} className="bg-secondary border-border" /></div>
          <div className="space-y-2"><Label className="text-xs font-bold uppercase tracking-wider">البريد الإلكتروني</Label>
            <Input value={email} onChange={(e) => setEmail(e.target.value)} dir="ltr" className="bg-secondary border-border" /></div>
          <div className="space-y-2"><Label className="text-xs font-bold uppercase tracking-wider">الجوال</Label>
            <Input value={phone} onChange={(e) => setPhone(e.target.value)} dir="ltr" className="bg-secondary border-border" /></div>
          <div className="space-y-2"><Label className="text-xs font-bold uppercase tracking-wider">ربط بموظف (اختياري)</Label>
            <Combobox value={employeeId} onChange={setEmployeeId} options={empOptions} placeholder="اختر موظفاً…" allowClear /></div>
          <div className="space-y-2 sm:col-span-2">
            <Label className="text-xs font-bold uppercase tracking-wider">الأدوار</Label>
            <div className="flex flex-wrap gap-2">
              {roles.map((r) => {
                const on = roleIds.includes(r.id);
                return (
                  <button key={r.id} type="button" onClick={() => setRoleIds((p) => on ? p.filter((x) => x !== r.id) : [...p, r.id])}
                    className={`px-3 h-8 text-sm border ${on ? "bg-primary text-primary-foreground border-primary" : "border-border hover:bg-muted"}`}>
                    {r.nameAr || r.name}
                  </button>
                );
              })}
            </div>
          </div>
          <label className="flex items-center gap-2 text-sm cursor-pointer sm:col-span-2">
            <input type="checkbox" checked={sendInvite} onChange={(e) => setSendInvite(e.target.checked)} />
            إرسال دعوة لتعيين كلمة المرور (بدلاً من تعيينها يدوياً)
          </label>
          {!sendInvite && (
            <div className="space-y-2 sm:col-span-2"><Label className="text-xs font-bold uppercase tracking-wider">كلمة المرور</Label>
              <Input type="password" value={password} onChange={(e) => setPassword(e.target.value)} dir="ltr" className="bg-secondary border-border" /></div>
          )}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={saving}>إلغاء</Button>
          <Button onClick={save} disabled={saving} className="font-bold">{saving ? "جاري الحفظ…" : "إنشاء"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function AccessDialog({ user, roles, templates, canManage, onClose, onRefresh }: {
  user: UserDetail; roles: RoleDto[]; templates: TemplateDto[]; canManage: boolean; onClose: () => void; onRefresh: () => Promise<void>;
}) {
  const [roleIds, setRoleIds] = useState<string[]>(user.roleIds);
  const [busy, setBusy] = useState(false);
  const assignedTemplateIds = useMemo(() => new Set(user.templates.map((t) => t.id)), [user.templates]);

  async function saveRoles() {
    setBusy(true);
    try { await setUserRoles(user.id, roleIds); toast.success("تم تحديث الأدوار"); await onRefresh(); }
    catch (err) { notifyError(err, "تعذر تحديث الأدوار"); } finally { setBusy(false); }
  }

  async function toggleTemplate(t: TemplateDto) {
    setBusy(true);
    try {
      if (assignedTemplateIds.has(t.id)) await revokeTemplate(user.id, t.id);
      else await assignTemplate(user.id, t.id);
      await onRefresh();
    } catch (err) { notifyError(err, "تعذر تحديث القالب"); } finally { setBusy(false); }
  }

  async function doChangeEmail() {
    const next = prompt("البريد الإلكتروني الجديد", user.email);
    if (!next || next === user.email) return;
    setBusy(true);
    try { await changeEmail(user.id, next.trim()); toast.success("تم تغيير البريد"); await onRefresh(); }
    catch (err) { notifyError(err, "تعذر تغيير البريد"); } finally { setBusy(false); }
  }

  return (
    <Dialog open onOpenChange={(o) => { if (!o && !busy) onClose(); }}>
      <DialogContent className="sm:max-w-2xl">
        <DialogHeader><DialogTitle>إدارة وصول — {user.fullName}</DialogTitle></DialogHeader>
        <div className="space-y-5 py-2 max-h-[70vh] overflow-y-auto">
          <div className="flex items-center justify-between border border-border bg-card p-3 text-sm">
            <span dir="ltr" className="text-muted-foreground">{user.email}</span>
            {canManage && <button onClick={doChangeEmail} className="inline-flex items-center gap-1 text-primary hover:underline"><Mail className="h-3.5 w-3.5" /> تغيير البريد</button>}
          </div>

          <section>
            <h3 className="text-xs font-bold uppercase tracking-wider text-muted-foreground mb-2">الأدوار</h3>
            <div className="flex flex-wrap gap-2">
              {roles.map((r) => {
                const on = roleIds.includes(r.id);
                return (
                  <button key={r.id} type="button" disabled={!canManage}
                    onClick={() => setRoleIds((p) => on ? p.filter((x) => x !== r.id) : [...p, r.id])}
                    className={`px-3 h-8 text-sm border disabled:opacity-60 ${on ? "bg-primary text-primary-foreground border-primary" : "border-border hover:bg-muted"}`}>
                    {r.nameAr || r.name}
                  </button>
                );
              })}
            </div>
            {canManage && <Button onClick={saveRoles} disabled={busy} variant="outline" className="mt-3 h-8 text-xs">حفظ الأدوار</Button>}
          </section>

          <section>
            <h3 className="text-xs font-bold uppercase tracking-wider text-muted-foreground mb-2">قوالب الصلاحيات</h3>
            <div className="flex flex-wrap gap-2">
              {templates.map((t) => {
                const on = assignedTemplateIds.has(t.id);
                return (
                  <button key={t.id} type="button" disabled={!canManage || busy} onClick={() => toggleTemplate(t)}
                    className={`px-3 h-8 text-sm border disabled:opacity-60 ${on ? "bg-accent text-accent-foreground border-primary/40" : "border-border hover:bg-muted"}`}>
                    {t.nameAr || t.nameEn}
                  </button>
                );
              })}
              {templates.length === 0 && <span className="text-sm text-muted-foreground">لا توجد قوالب — أنشئها من صفحة القوالب</span>}
            </div>
          </section>

          <section>
            <h3 className="text-xs font-bold uppercase tracking-wider text-muted-foreground mb-2">الصلاحيات الفعلية ({user.effectivePermissions.length})</h3>
            <div className="flex flex-wrap gap-1.5 max-h-40 overflow-y-auto border border-border p-3">
              {user.effectivePermissions.map((p) => (
                <span key={p} dir="ltr" className="text-[11px] bg-secondary px-2 py-0.5 text-muted-foreground">{p}</span>
              ))}
            </div>
            <p className="mt-2 text-xs text-muted-foreground">لإدارة الاستثناءات (السماح/المنع الصريح) استخدم الأدوار والقوالب؛ المنع الصريح يتقدّم دائماً.</p>
          </section>
        </div>
        <DialogFooter><Button variant="outline" onClick={onClose} disabled={busy}>إغلاق</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
