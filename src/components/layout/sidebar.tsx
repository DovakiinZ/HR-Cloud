"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import {
  LayoutDashboard,
  Users,
  Clock,
  CalendarDays,
  Banknote,
  FileText,
  BarChart3,
  FolderOpen,
  Settings,
  LogOut,
  CheckSquare,
  ClipboardCheck,
} from "lucide-react";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { logout } from "@/lib/api/auth";
import { getMyApprovals } from "@/lib/api/approvals";
import { PalmMark } from "@/components/brand/sanad-logo";

const navItems = [
  { label: "لوحة التحكم", href: "/dashboard", icon: LayoutDashboard },
  { label: "الموظفين", href: "/employees", icon: Users },
  { label: "الحضور", href: "/attendance", icon: Clock },
  { label: "الإجازات", href: "/leaves", icon: CalendarDays },
  { label: "الرواتب", href: "/payroll", icon: Banknote },
  { label: "المهام", href: "/tasks", icon: CheckSquare },
  { label: "الطلبات", href: "/requests", icon: FileText },
  { label: "الموافقات", href: "/approvals", icon: ClipboardCheck, badge: true },
  { label: "التقارير", href: "/reports", icon: BarChart3 },
  { label: "المستندات", href: "/documents", icon: FolderOpen },
  { label: "الإعدادات", href: "/settings", icon: Settings },
];

export function Sidebar() {
  const pathname = usePathname();
  const [pending, setPending] = useState(0);

  useEffect(() => {
    const load = () => getMyApprovals().then((a) => setPending(a.filter((x) => x.status === "Pending").length)).catch(() => {});
    load();
    const t = setInterval(load, 60_000);
    return () => clearInterval(t);
  }, [pathname]);

  const handleLogout = () => {
    logout();
  };

  return (
    <aside className="fixed top-0 right-0 z-40 h-screen w-16 border-l border-border bg-secondary flex flex-col items-center py-4">
      {/* Logo */}
      <div className="mb-6 flex h-10 w-10 items-center justify-center bg-primary">
        <PalmMark size={22} className="text-primary-foreground" />
      </div>

      {/* Nav */}
      <nav className="flex flex-1 flex-col items-center gap-1">
        {navItems.map((item) => {
          const isActive =
            pathname === item.href || pathname?.startsWith(item.href + "/");
          const Icon = item.icon;
          const showBadge = "badge" in item && item.badge && pending > 0;
          return (
            <Tooltip key={item.href}>
              <TooltipTrigger
                render={<Link href={item.href} />}
                className={`relative flex h-10 w-10 items-center justify-center transition-colors ${
                  isActive
                    ? "bg-primary text-primary-foreground"
                    : "text-muted-foreground hover:bg-card hover:text-foreground"
                }`}
              >
                <Icon className="h-5 w-5" />
                {showBadge && (
                  <span className="absolute -top-0.5 right-0.5 flex h-4 min-w-4 items-center justify-center bg-destructive px-1 text-[10px] font-bold text-white">
                    {pending > 9 ? "9+" : pending}
                  </span>
                )}
              </TooltipTrigger>
              <TooltipContent side="left" className="font-sans">
                {item.label}
              </TooltipContent>
            </Tooltip>
          );
        })}
      </nav>

      {/* Logout */}
      <Tooltip>
        <TooltipTrigger
          onClick={handleLogout}
          className="flex h-10 w-10 items-center justify-center text-muted-foreground hover:bg-card hover:text-foreground transition-colors"
        >
          <LogOut className="h-5 w-5" />
        </TooltipTrigger>
        <TooltipContent side="left" className="font-sans">
          تسجيل الخروج
        </TooltipContent>
      </Tooltip>
    </aside>
  );
}
