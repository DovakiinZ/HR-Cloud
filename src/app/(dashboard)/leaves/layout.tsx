"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";

const TABS = [
  { href: "/leaves", label: "الإجازات" },
  { href: "/leaves/ledger", label: "دفتر الاستحقاق" },
  { href: "/leaves/balances", label: "أرصدة الإجازات" },
];

export default function LeavesLayout({ children }: { children: React.ReactNode }) {
  const path = usePathname();
  return (
    <div className="space-y-4 p-6">
      <div className="flex items-center gap-1 border-b border-border">
        {TABS.map((t) => {
          const active = t.href === "/leaves" ? path === "/leaves" : path.startsWith(t.href);
          return (
            <Link
              key={t.href}
              href={t.href}
              className={`-mb-px border-b-2 px-4 py-2 text-sm transition-colors ${active ? "border-primary font-medium text-primary" : "border-transparent text-muted-foreground hover:text-foreground"}`}
            >
              {t.label}
            </Link>
          );
        })}
      </div>
      {children}
    </div>
  );
}
