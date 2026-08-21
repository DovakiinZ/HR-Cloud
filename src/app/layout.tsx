import type { Metadata } from "next";
import { cookies } from "next/headers";
import { TooltipProvider } from "@/components/ui/tooltip";
import { LocaleProvider } from "@/lib/i18n/locale-provider";
import { LocalizedToaster } from "@/components/layout/localized-toaster";
import { DEFAULT_LOCALE, DIR, LOCALE_COOKIE, isLocale, type Locale } from "@/lib/i18n/config";
import "./globals.css";

const TITLES: Record<Locale, string> = {
  ar: "سند — نظام إدارة الموارد البشرية",
  en: "Sanad — HR Management System",
};
const DESCRIPTIONS: Record<Locale, string> = {
  ar: "نظام متكامل لإدارة الموارد البشرية",
  en: "An integrated human resources management system",
};

async function resolveLocale(): Promise<Locale> {
  const cookieStore = await cookies();
  const raw = cookieStore.get(LOCALE_COOKIE)?.value;
  return isLocale(raw) ? raw : DEFAULT_LOCALE;
}

export async function generateMetadata(): Promise<Metadata> {
  const locale = await resolveLocale();
  return { title: TITLES[locale], description: DESCRIPTIONS[locale] };
}

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const locale = await resolveLocale();
  return (
    <html lang={locale} dir={DIR[locale]} className="h-full antialiased">
      <body className="min-h-full flex flex-col font-sans">
        <LocaleProvider initialLocale={locale}>
          <TooltipProvider>{children}</TooltipProvider>
          {/* Global credit — shown on every page */}
          <div className="pointer-events-none fixed bottom-2 start-2 z-50 select-none text-[10px] tracking-wide text-muted-foreground/60">
            Designed by Dovakin
          </div>
          <LocalizedToaster />
        </LocaleProvider>
      </body>
    </html>
  );
}
