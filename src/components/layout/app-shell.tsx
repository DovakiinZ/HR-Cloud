"use client";

import { Sidebar } from "./sidebar";
import { Topbar } from "./topbar";

export function AppShell({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-background">
      <Sidebar />
      <div className="ms-16">
        <Topbar />
        <main className="p-6">{children}</main>
      </div>
    </div>
  );
}
