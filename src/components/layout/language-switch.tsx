"use client";

import { Languages } from "lucide-react";
import { useT } from "@/lib/i18n/use-t";
import type { Locale } from "@/lib/i18n/config";

export function LanguageSwitch() {
  const { locale, setLocale } = useT();
  const next: Locale = locale === "ar" ? "en" : "ar";
  const label = locale === "ar" ? "English" : "العربية";
  return (
    <button
      type="button"
      onClick={() => setLocale(next)}
      aria-label={label}
      className="flex h-9 items-center gap-1.5 px-2 text-sm text-muted-foreground hover:text-foreground transition-colors"
    >
      <Languages className="h-4 w-4" />
      <span className="font-medium">{label}</span>
    </button>
  );
}
