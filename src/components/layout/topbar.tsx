"use client";

import { useEffect, useState } from "react";
import { Search } from "lucide-react";
import { Input } from "@/components/ui/input";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { getUser, AuthUser } from "@/lib/auth-storage";
import { NotificationBell } from "./notification-bell";
import { LanguageSwitch } from "./language-switch";
import { useT } from "@/lib/i18n/use-t";

export function Topbar() {
  const { t } = useT();
  const [user, setUser] = useState<AuthUser | null>(null);

  useEffect(() => {
    setUser(getUser());
  }, []);

  const name = user?.fullName || "—";
  const email = user?.email || "";
  const initials = name && name !== "—" ? name.trim().charAt(0) : "?";

  return (
    <header className="sticky top-0 z-30 h-14 border-b border-border bg-background flex items-center justify-between px-6">
      {/* Search */}
      <div className="relative w-72">
        <Search className="absolute start-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
        <Input
          placeholder={t("common.search")}
          className="ps-10 bg-secondary border-border h-9 text-sm placeholder:text-muted-foreground"
        />
      </div>

      {/* Actions */}
      <div className="flex items-center gap-4">
        <LanguageSwitch />
        <NotificationBell />

        <div className="flex items-center gap-3">
          <div className="text-start">
            <p className="text-sm font-medium leading-none">{name}</p>
            <p className="text-xs text-muted-foreground">{email}</p>
          </div>
          <Avatar className="h-8 w-8">
            <AvatarFallback className="bg-primary text-primary-foreground text-xs font-bold">
              {initials}
            </AvatarFallback>
          </Avatar>
        </div>
      </div>
    </header>
  );
}
