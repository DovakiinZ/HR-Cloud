"use client";

import { createContext, useCallback, useMemo, useState } from "react";
import { catalogs } from "./catalog";
import { translate } from "./translate";
import { DIR, LOCALE_COOKIE, type Locale } from "./config";

export interface LocaleContextValue {
  locale: Locale;
  dir: "rtl" | "ltr";
  t: (key: string, params?: Record<string, string | number>) => string;
  setLocale: (next: Locale) => void;
}

export const LocaleContext = createContext<LocaleContextValue | null>(null);

export function LocaleProvider({
  initialLocale,
  children,
}: {
  initialLocale: Locale;
  children: React.ReactNode;
}) {
  const [locale, setLocaleState] = useState<Locale>(initialLocale);

  const setLocale = useCallback((next: Locale) => {
    setLocaleState(next);
    try {
      document.cookie = `${LOCALE_COOKIE}=${next};path=/;max-age=31536000;samesite=lax`;
      localStorage.setItem(LOCALE_COOKIE, next);
    } catch {
      /* storage unavailable — in-memory state still updates */
    }
    const el = document.documentElement;
    el.lang = next;
    el.dir = DIR[next];
  }, []);

  const value = useMemo<LocaleContextValue>(
    () => ({
      locale,
      dir: DIR[locale],
      t: (key, params) => translate(catalogs, locale, key, params),
      setLocale,
    }),
    [locale, setLocale],
  );

  return <LocaleContext.Provider value={value}>{children}</LocaleContext.Provider>;
}
