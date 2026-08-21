export const LOCALES = ["ar", "en"] as const;
export type Locale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: Locale = "ar";

export const DIR: Record<Locale, "rtl" | "ltr"> = { ar: "rtl", en: "ltr" };

export const LOCALE_COOKIE = "NEXT_LOCALE";

export const LOCALE_TAG: Record<Locale, string> = { ar: "ar-SA", en: "en" };

export function isLocale(value: string | undefined | null): value is Locale {
  return value === "ar" || value === "en";
}
