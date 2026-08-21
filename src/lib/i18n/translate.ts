import { DEFAULT_LOCALE, type Locale } from "./config";

export type Messages = Record<string, unknown>;

export function lookup(messages: Messages, key: string): string | undefined {
  const node = key.split(".").reduce<unknown>((acc, part) => {
    if (acc && typeof acc === "object") {
      return (acc as Record<string, unknown>)[part];
    }
    return undefined;
  }, messages);
  return typeof node === "string" ? node : undefined;
}

function interpolate(str: string, params: Record<string, string | number>): string {
  return str.replace(/\{(\w+)\}/g, (_, k: string) =>
    k in params ? String(params[k]) : `{${k}}`,
  );
}

export function translate(
  catalogs: Record<Locale, Messages>,
  locale: Locale,
  key: string,
  params?: Record<string, string | number>,
): string {
  const raw =
    lookup(catalogs[locale], key) ??
    lookup(catalogs[DEFAULT_LOCALE], key) ??
    key;
  return params ? interpolate(raw, params) : raw;
}
