import { LOCALE_TAG, type Locale } from "./config";

export function formatNumber(
  value: number,
  locale: Locale,
  options?: Intl.NumberFormatOptions,
): string {
  return new Intl.NumberFormat(LOCALE_TAG[locale], {
    numberingSystem: "latn",
    ...options,
  }).format(value);
}

export function formatCurrency(value: number, locale: Locale): string {
  return new Intl.NumberFormat(LOCALE_TAG[locale], {
    numberingSystem: "latn",
    style: "currency",
    currency: "SAR",
  }).format(value);
}

export function formatDate(
  value: Date | string | number,
  locale: Locale,
  options?: Intl.DateTimeFormatOptions,
): string {
  const date = value instanceof Date ? value : new Date(value);
  return new Intl.DateTimeFormat(LOCALE_TAG[locale], {
    numberingSystem: "latn",
    ...(options ?? { dateStyle: "medium" }),
  }).format(date);
}
