"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { Bell, CheckCheck, Loader2 } from "lucide-react";
import {
  AppNotification, getNotifications, getUnreadCount, markAllNotificationsRead, markNotificationRead,
} from "@/lib/api/notifications";
import { useT } from "@/lib/i18n/use-t";

export function NotificationBell() {
  const { t } = useT();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<AppNotification[]>([]);
  const [unread, setUnread] = useState(0);
  const [loading, setLoading] = useState(false);

  const loadCount = useCallback(() => { getUnreadCount().then(setUnread).catch(() => {}); }, []);

  useEffect(() => {
    loadCount();
    const t = setInterval(loadCount, 30_000);
    return () => clearInterval(t);
  }, [loadCount]);

  const openDropdown = async () => {
    setOpen((v) => !v);
    if (!open) {
      setLoading(true);
      try { setItems(await getNotifications()); } catch { /* handled globally */ }
      finally { setLoading(false); }
    }
  };

  const onClickItem = async (n: AppNotification) => {
    setOpen(false);
    if (!n.isRead) { markNotificationRead(n.id).then(loadCount).catch(() => {}); }
    if (n.link) router.push(n.link);
  };

  const readAll = async () => {
    await markAllNotificationsRead().catch(() => {});
    setItems((p) => p.map((n) => ({ ...n, isRead: true })));
    loadCount();
  };

  return (
    <div className="relative">
      <button onClick={openDropdown} className="relative flex h-9 w-9 items-center justify-center text-muted-foreground hover:text-foreground transition-colors">
        <Bell className="h-5 w-5" />
        {unread > 0 && (
          <span className="absolute -top-0.5 -end-0.5 flex h-4 min-w-4 items-center justify-center bg-destructive px-1 text-[10px] font-bold text-white">
            {unread > 9 ? "9+" : unread}
          </span>
        )}
      </button>

      {open && (
        <>
          <div className="fixed inset-0 z-40" onClick={() => setOpen(false)} />
          <div className="absolute end-0 z-50 mt-2 w-80 border border-border bg-card shadow-xl">
            <div className="flex items-center justify-between border-b border-border px-4 py-3">
              <span className="text-sm font-bold">{t("navigation.notifications")}</span>
              {items.some((n) => !n.isRead) && (
                <button onClick={readAll} className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
                  <CheckCheck className="h-3.5 w-3.5" /> {t("navigation.markAllRead")}
                </button>
              )}
            </div>
            <div className="max-h-96 overflow-auto">
              {loading ? (
                <div className="flex h-24 items-center justify-center text-muted-foreground"><Loader2 className="h-5 w-5 animate-spin" /></div>
              ) : items.length === 0 ? (
                <div className="flex h-24 items-center justify-center text-sm text-muted-foreground">{t("navigation.noNotifications")}</div>
              ) : (
                items.map((n) => (
                  <button key={n.id} onClick={() => onClickItem(n)} className={`flex w-full flex-col gap-0.5 border-b border-border/50 px-4 py-3 text-start hover:bg-muted/40 ${n.isRead ? "" : "bg-primary/5"}`}>
                    <div className="flex items-center gap-2">
                      {!n.isRead && <span className="h-2 w-2 shrink-0 rounded-full bg-primary" />}
                      <span className="text-sm font-medium">{n.titleAr}</span>
                    </div>
                    {n.bodyAr && <span className="text-xs text-muted-foreground line-clamp-2">{n.bodyAr}</span>}
                    <span className="text-[10px] text-muted-foreground">{new Date(n.createdAt).toLocaleString("ar")}</span>
                  </button>
                ))
              )}
            </div>
          </div>
        </>
      )}
    </div>
  );
}
