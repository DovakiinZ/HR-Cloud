# Localization Foundation Slice — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the client-side i18n machinery (Arabic default/RTL, English/LTR), wire it into the app shell, add the header language switch, and fully localize the global chrome + navigation — establishing the pattern every later module slice follows.

**Architecture:** A custom React Context (`LocaleProvider` + `useT()`) resolves dotted keys against per-module JSON catalogs with an Arabic fallback. The root layout reads a cookie server-side to set `<html lang/dir>` for a correct first paint; `setLocale` then updates the cookie/localStorage and `document.documentElement` client-side for an instant switch with no refresh. Pure logic (translate/format/localized-name) is unit-tested with vitest; React wiring and RTL/LTR chrome are verified via build + a key-parity script + a hardcoded-string guard.

**Tech Stack:** Next.js 16 (App Router), React 19, TypeScript, Tailwind 4 (logical utilities), vitest (new dev dependency, for pure-logic units only), `Intl` for formatting.

## Global Constraints

- Default locale is **Arabic (`ar`)**, RTL. English (`en`) is LTR. Copy verbatim: `LOCALES=['ar','en']`, `DEFAULT_LOCALE='ar'`, `DIR={ar:'rtl',en:'ltr'}`, cookie name `NEXT_LOCALE`, locale tags `{ar:'ar-SA',en:'en'}`.
- **No hard-coded Arabic/English UI strings** in components touched by this plan — all through `t('<key>')`.
- Missing-key resolution order: **current locale → Arabic (`DEFAULT_LOCALE`) → raw key**.
- All number/date/currency formatting uses **`numberingSystem:'latn'`** (Latin digits in both locales). Currency is **`SAR`**.
- **No new i18n library, no locale routing, no duplicated pages.** Same components serve both languages.
- Switching language must **not** trigger navigation, refresh, or logout.
- This Next.js is modified (see `AGENTS.md`) — **read `node_modules/next/dist/docs/`** for the current `cookies()` / layout / metadata APIs before editing `src/app/layout.tsx`.
- Path alias `@/*` → `./src/*` (already configured). `resolveJsonModule` is already `true`.
- Tailwind directional utilities must be **logical** (`ms/me/ps/pe/start/end/border-s/border-e/text-start/text-end`), never physical (`ml/mr/pl/pr/left/right/text-left/text-right`), in any chrome file this plan touches.
- Commit after every task. Branch: `feat/localization-i18n` (already checked out).

---

### Task 1: Translation core (`config.ts` + `translate.ts`) + vitest

**Files:**
- Create: `src/lib/i18n/config.ts`
- Create: `src/lib/i18n/translate.ts`
- Create: `src/lib/i18n/__tests__/translate.test.ts`
- Create: `vitest.config.ts`
- Modify: `package.json` (add `vitest` devDependency + `test` script)

**Interfaces:**
- Produces: `Locale` (`'ar'|'en'`), `LOCALES`, `DEFAULT_LOCALE`, `DIR`, `LOCALE_COOKIE`, `LOCALE_TAG` from `config.ts`.
- Produces: `type Messages = Record<string, unknown>`; `lookup(messages: Messages, key: string): string | undefined`; `translate(catalogs: Record<Locale, Messages>, locale: Locale, key: string, params?: Record<string, string|number>): string` from `translate.ts`.

- [ ] **Step 1: Install vitest and add the test script**

Run:
```bash
npm install -D vitest@^2
```
Then edit `package.json` `"scripts"` to add:
```json
"test": "vitest run",
"test:watch": "vitest"
```

- [ ] **Step 2: Create the vitest config**

Create `vitest.config.ts`:
```ts
import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
```

- [ ] **Step 3: Create `config.ts`**

Create `src/lib/i18n/config.ts`:
```ts
export const LOCALES = ["ar", "en"] as const;
export type Locale = (typeof LOCALES)[number];

export const DEFAULT_LOCALE: Locale = "ar";

export const DIR: Record<Locale, "rtl" | "ltr"> = { ar: "rtl", en: "ltr" };

export const LOCALE_COOKIE = "NEXT_LOCALE";

export const LOCALE_TAG: Record<Locale, string> = { ar: "ar-SA", en: "en" };

export function isLocale(value: string | undefined | null): value is Locale {
  return value === "ar" || value === "en";
}
```

- [ ] **Step 4: Write the failing test**

Create `src/lib/i18n/__tests__/translate.test.ts`:
```ts
import { describe, it, expect } from "vitest";
import { translate, lookup } from "../translate";
import type { Locale } from "../config";

const catalogs = {
  ar: { common: { save: "حفظ" }, employee: { department: "القسم" } },
  en: { common: { save: "Save" } },
} as Record<Locale, Record<string, unknown>>;

describe("translate", () => {
  it("returns the value for the active locale", () => {
    expect(translate(catalogs, "en", "common.save")).toBe("Save");
    expect(translate(catalogs, "ar", "common.save")).toBe("حفظ");
  });

  it("falls back to Arabic when the English key is missing", () => {
    expect(translate(catalogs, "en", "employee.department")).toBe("القسم");
  });

  it("falls back to the raw key when missing in both locales", () => {
    expect(translate(catalogs, "en", "nope.here")).toBe("nope.here");
  });

  it("interpolates {param} tokens", () => {
    const c = { ar: { greet: "مرحبا {name}" }, en: { greet: "Hi {name}" } } as Record<Locale, Record<string, unknown>>;
    expect(translate(c, "en", "greet", { name: "Sara" })).toBe("Hi Sara");
  });

  it("lookup returns undefined for a non-string node", () => {
    expect(lookup(catalogs.ar, "common")).toBeUndefined();
  });
});
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `npm run test`
Expected: FAIL — `Cannot find module '../translate'`.

- [ ] **Step 6: Implement `translate.ts`**

Create `src/lib/i18n/translate.ts`:
```ts
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
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `npm run test`
Expected: PASS — 5 tests.

- [ ] **Step 8: Commit**

```bash
git add src/lib/i18n/config.ts src/lib/i18n/translate.ts src/lib/i18n/__tests__/translate.test.ts vitest.config.ts package.json package-lock.json
git commit -m "feat(i18n): translation core with Arabic fallback + vitest"
```

---

### Task 2: Locale-aware formatting (`format.ts`)

**Files:**
- Create: `src/lib/i18n/format.ts`
- Create: `src/lib/i18n/__tests__/format.test.ts`

**Interfaces:**
- Consumes: `Locale`, `LOCALE_TAG` from `config.ts`.
- Produces: `formatNumber(value: number, locale: Locale, options?: Intl.NumberFormatOptions): string`; `formatCurrency(value: number, locale: Locale): string`; `formatDate(value: Date | string | number, locale: Locale, options?: Intl.DateTimeFormatOptions): string`.

- [ ] **Step 1: Write the failing test**

Create `src/lib/i18n/__tests__/format.test.ts`:
```ts
import { describe, it, expect } from "vitest";
import { formatNumber, formatCurrency, formatDate } from "../format";

const ARABIC_INDIC = /[٠-٩]/;

describe("formatting uses Latin digits in both locales", () => {
  it("formats numbers with Latin digits in Arabic", () => {
    const out = formatNumber(1234.5, "ar");
    expect(out).toContain("1,234.5");
    expect(ARABIC_INDIC.test(out)).toBe(false);
  });

  it("formats currency as SAR with Latin digits", () => {
    const ar = formatCurrency(1234, "ar");
    const en = formatCurrency(1234, "en");
    expect(ar).toContain("1,234");
    expect(en).toContain("1,234");
    expect(ARABIC_INDIC.test(ar)).toBe(false);
  });

  it("formats a date with Latin digits and accepts an ISO string", () => {
    const out = formatDate("2026-01-15T00:00:00Z", "ar", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: "UTC" });
    expect(out).toContain("2026");
    expect(ARABIC_INDIC.test(out)).toBe(false);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm run test`
Expected: FAIL — `Cannot find module '../format'`.

- [ ] **Step 3: Implement `format.ts`**

Create `src/lib/i18n/format.ts`:
```ts
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm run test`
Expected: PASS — format tests green (translate tests still green).

- [ ] **Step 5: Commit**

```bash
git add src/lib/i18n/format.ts src/lib/i18n/__tests__/format.test.ts
git commit -m "feat(i18n): locale-aware number/currency/date formatting (Latin digits)"
```

---

### Task 3: Dynamic-data helper (`localized-name.ts`)

**Files:**
- Create: `src/lib/i18n/localized-name.ts`
- Create: `src/lib/i18n/__tests__/localized-name.test.ts`

**Interfaces:**
- Consumes: `Locale` from `config.ts`.
- Produces: `interface BilingualEntity` (fields `name?`, `nameAr?`, `nameEn?`, `description?`, `descriptionAr?`, `descriptionEn?`, all `string | null | undefined`); `localizedName(entity: BilingualEntity | null | undefined, locale: Locale): string`; `localizedDescription(entity: BilingualEntity | null | undefined, locale: Locale): string`.

- [ ] **Step 1: Write the failing test**

Create `src/lib/i18n/__tests__/localized-name.test.ts`:
```ts
import { describe, it, expect } from "vitest";
import { localizedName, localizedDescription } from "../localized-name";

describe("localizedName", () => {
  it("prefers nameEn in English when present", () => {
    expect(localizedName({ nameAr: "القسم", nameEn: "Department" }, "en")).toBe("Department");
  });

  it("falls back to Arabic when English is empty", () => {
    expect(localizedName({ nameAr: "القسم", nameEn: "" }, "en")).toBe("القسم");
    expect(localizedName({ nameAr: "القسم" }, "en")).toBe("القسم");
  });

  it("uses nameAr in Arabic even if English exists", () => {
    expect(localizedName({ nameAr: "القسم", nameEn: "Department" }, "ar")).toBe("القسم");
  });

  it("falls back to legacy `name`, then to empty string", () => {
    expect(localizedName({ name: "Legacy" }, "en")).toBe("Legacy");
    expect(localizedName(null, "en")).toBe("");
    expect(localizedName({}, "ar")).toBe("");
  });

  it("localizedDescription mirrors the same rules", () => {
    expect(localizedDescription({ descriptionAr: "وصف", descriptionEn: "Desc" }, "en")).toBe("Desc");
    expect(localizedDescription({ descriptionAr: "وصف" }, "en")).toBe("وصف");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `npm run test`
Expected: FAIL — `Cannot find module '../localized-name'`.

- [ ] **Step 3: Implement `localized-name.ts`**

Create `src/lib/i18n/localized-name.ts`:
```ts
import type { Locale } from "./config";

export interface BilingualEntity {
  name?: string | null;
  nameAr?: string | null;
  nameEn?: string | null;
  description?: string | null;
  descriptionAr?: string | null;
  descriptionEn?: string | null;
}

export function localizedName(
  entity: BilingualEntity | null | undefined,
  locale: Locale,
): string {
  if (!entity) return "";
  if (locale === "en" && entity.nameEn) return entity.nameEn;
  return entity.nameAr || entity.name || entity.nameEn || "";
}

export function localizedDescription(
  entity: BilingualEntity | null | undefined,
  locale: Locale,
): string {
  if (!entity) return "";
  if (locale === "en" && entity.descriptionEn) return entity.descriptionEn;
  return entity.descriptionAr || entity.description || entity.descriptionEn || "";
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `npm run test`
Expected: PASS — all three test files green.

- [ ] **Step 5: Commit**

```bash
git add src/lib/i18n/localized-name.ts src/lib/i18n/__tests__/localized-name.test.ts
git commit -m "feat(i18n): localizedName/localizedDescription for NameAr/NameEn data"
```

---

### Task 4: Shared catalogs + `catalog.ts` + parity script

**Files:**
- Create: `src/locales/ar/common.json`, `src/locales/en/common.json`
- Create: `src/locales/ar/navigation.json`, `src/locales/en/navigation.json`
- Create: `src/locales/ar/validation.json`, `src/locales/en/validation.json`
- Create: `src/lib/i18n/catalog.ts`
- Create: `scripts/i18n-parity.mjs`
- Modify: `package.json` (add `i18n:parity` script)

**Interfaces:**
- Consumes: `Locale` from `config.ts`; the JSON catalog files.
- Produces: `catalogs: Record<Locale, Messages>` from `catalog.ts` (deep, namespaced dictionary).

- [ ] **Step 1: Create the Arabic shared catalogs**

Create `src/locales/ar/common.json`:
```json
{
  "common": {
    "save": "حفظ",
    "cancel": "إلغاء",
    "delete": "حذف",
    "edit": "تعديل",
    "add": "إضافة",
    "create": "إنشاء",
    "update": "تحديث",
    "close": "إغلاق",
    "confirm": "تأكيد",
    "back": "رجوع",
    "search": "بحث...",
    "actions": "الإجراءات",
    "yes": "نعم",
    "no": "لا",
    "loading": "جارٍ التحميل...",
    "noData": "لا توجد بيانات",
    "errorTitle": "حدث خطأ",
    "retry": "إعادة المحاولة",
    "confirmDelete": "هل أنت متأكد من الحذف؟"
  }
}
```

Create `src/locales/ar/navigation.json`:
```json
{
  "navigation": {
    "dashboard": "لوحة التحكم",
    "employees": "الموظفين",
    "attendance": "الحضور",
    "leaves": "الإجازات",
    "payroll": "الرواتب",
    "tasks": "المهام",
    "requests": "الطلبات",
    "approvals": "الموافقات",
    "reports": "التقارير",
    "documents": "المستندات",
    "settings": "الإعدادات",
    "logout": "تسجيل الخروج",
    "notifications": "الإشعارات",
    "markAllRead": "تعليم الكل كمقروء",
    "noNotifications": "لا توجد إشعارات"
  }
}
```

Create `src/locales/ar/validation.json`:
```json
{
  "validation": {
    "required": "هذا الحقل مطلوب",
    "email": "يرجى إدخال بريد إلكتروني صحيح",
    "min": "القيمة أقل من الحد المسموح",
    "max": "القيمة أكبر من الحد المسموح",
    "numeric": "يرجى إدخال رقم صحيح"
  }
}
```

- [ ] **Step 2: Create the English shared catalogs (same keys, professional HR terminology)**

Create `src/locales/en/common.json`:
```json
{
  "common": {
    "save": "Save",
    "cancel": "Cancel",
    "delete": "Delete",
    "edit": "Edit",
    "add": "Add",
    "create": "Create",
    "update": "Update",
    "close": "Close",
    "confirm": "Confirm",
    "back": "Back",
    "search": "Search...",
    "actions": "Actions",
    "yes": "Yes",
    "no": "No",
    "loading": "Loading...",
    "noData": "No data available",
    "errorTitle": "Something went wrong",
    "retry": "Retry",
    "confirmDelete": "Are you sure you want to delete this?"
  }
}
```

Create `src/locales/en/navigation.json`:
```json
{
  "navigation": {
    "dashboard": "Dashboard",
    "employees": "Employees",
    "attendance": "Attendance",
    "leaves": "Leave",
    "payroll": "Payroll",
    "tasks": "Tasks",
    "requests": "Requests",
    "approvals": "Approvals",
    "reports": "Reports",
    "documents": "Documents",
    "settings": "Settings",
    "logout": "Sign out",
    "notifications": "Notifications",
    "markAllRead": "Mark all as read",
    "noNotifications": "No notifications"
  }
}
```

Create `src/locales/en/validation.json`:
```json
{
  "validation": {
    "required": "This field is required",
    "email": "Please enter a valid email address",
    "min": "Value is below the allowed minimum",
    "max": "Value exceeds the allowed maximum",
    "numeric": "Please enter a valid number"
  }
}
```

- [ ] **Step 3: Create `catalog.ts`**

Create `src/lib/i18n/catalog.ts`:
```ts
import type { Locale } from "./config";
import type { Messages } from "./translate";

import arCommon from "@/locales/ar/common.json";
import arNavigation from "@/locales/ar/navigation.json";
import arValidation from "@/locales/ar/validation.json";
import enCommon from "@/locales/en/common.json";
import enNavigation from "@/locales/en/navigation.json";
import enValidation from "@/locales/en/validation.json";

// Each catalog file uses a distinct top-level namespace, so a shallow
// merge is sufficient. New module slices add their file to both locales here.
export const catalogs: Record<Locale, Messages> = {
  ar: { ...arCommon, ...arNavigation, ...arValidation },
  en: { ...enCommon, ...enNavigation, ...enValidation },
};
```

- [ ] **Step 4: Create the parity script**

Create `scripts/i18n-parity.mjs`:
```js
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

const ROOT = "src/locales";
const LOCALES = ["ar", "en"];

function flatten(obj, prefix = "") {
  const out = {};
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === "object" && !Array.isArray(v)) {
      Object.assign(out, flatten(v, key));
    } else {
      out[key] = v;
    }
  }
  return out;
}

function loadLocale(locale) {
  const dir = join(ROOT, locale);
  const keys = {};
  for (const file of readdirSync(dir).filter((f) => f.endsWith(".json"))) {
    const json = JSON.parse(readFileSync(join(dir, file), "utf8"));
    Object.assign(keys, flatten(json));
  }
  return keys;
}

const ar = loadLocale("ar");
const en = loadLocale("en");
const arKeys = new Set(Object.keys(ar));
const enKeys = new Set(Object.keys(en));

const missingInEn = [...arKeys].filter((k) => !enKeys.has(k));
const missingInAr = [...enKeys].filter((k) => !arKeys.has(k));
const emptyEn = [...enKeys].filter((k) => en[k] === "");
const emptyAr = [...arKeys].filter((k) => ar[k] === "");

let failed = false;
const report = (label, arr) => {
  if (arr.length) {
    failed = true;
    console.error(`\n${label} (${arr.length}):`);
    for (const k of arr) console.error(`  - ${k}`);
  }
};

report("Keys present in ar but missing in en", missingInEn);
report("Keys present in en but missing in ar", missingInAr);
report("Empty string values in en", emptyEn);
report("Empty string values in ar", emptyAr);

if (failed) {
  console.error("\ni18n parity check FAILED");
  process.exit(1);
}
console.log(`i18n parity OK — ${arKeys.size} keys in both locales`);
```

- [ ] **Step 5: Add the npm script**

Edit `package.json` `"scripts"` to add:
```json
"i18n:parity": "node scripts/i18n-parity.mjs"
```

- [ ] **Step 6: Run the parity script to verify it passes**

Run: `npm run i18n:parity`
Expected: `i18n parity OK — 39 keys in both locales`.

- [ ] **Step 7: Commit**

```bash
git add src/locales package.json src/lib/i18n/catalog.ts scripts/i18n-parity.mjs
git commit -m "feat(i18n): shared common/navigation/validation catalogs + parity check"
```

---

### Task 5: `LocaleProvider`, `useT`, and localized toaster

**Files:**
- Create: `src/lib/i18n/locale-provider.tsx`
- Create: `src/lib/i18n/use-t.ts`
- Create: `src/components/layout/localized-toaster.tsx`

**Interfaces:**
- Consumes: `catalogs` (catalog.ts), `translate` (translate.ts), `Locale`, `DIR`, `LOCALE_COOKIE` (config.ts).
- Produces: `LocaleProvider({ initialLocale: Locale, children })`; exported `LocaleContext`; `useT(): { locale: Locale; dir: 'rtl'|'ltr'; t: (key: string, params?: Record<string,string|number>) => string; setLocale: (next: Locale) => void }`; `LocalizedToaster` component.

- [ ] **Step 1: Create `locale-provider.tsx`**

Create `src/lib/i18n/locale-provider.tsx`:
```tsx
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
```

- [ ] **Step 2: Create `use-t.ts`**

Create `src/lib/i18n/use-t.ts`:
```ts
"use client";

import { useContext } from "react";
import { LocaleContext, type LocaleContextValue } from "./locale-provider";

export function useT(): LocaleContextValue {
  const ctx = useContext(LocaleContext);
  if (!ctx) {
    throw new Error("useT must be used within a LocaleProvider");
  }
  return ctx;
}
```

- [ ] **Step 3: Create `localized-toaster.tsx`**

Create `src/components/layout/localized-toaster.tsx`:
```tsx
"use client";

import { Toaster } from "sonner";
import { useT } from "@/lib/i18n/use-t";

export function LocalizedToaster() {
  const { dir } = useT();
  return (
    <Toaster
      position="top-center"
      dir={dir}
      theme="light"
      richColors
      closeButton
      toastOptions={{ style: { fontFamily: "inherit" } }}
    />
  );
}
```

- [ ] **Step 4: Verify it type-checks / builds**

Run: `npm run build`
Expected: build succeeds (these modules are imported in Task 6; a standalone build here confirms no type errors in the new files).

- [ ] **Step 5: Commit**

```bash
git add src/lib/i18n/locale-provider.tsx src/lib/i18n/use-t.ts src/components/layout/localized-toaster.tsx
git commit -m "feat(i18n): LocaleProvider + useT hook + direction-aware toaster"
```

---

### Task 6: Root layout SSR wiring

**Files:**
- Modify: `src/app/layout.tsx`

**Interfaces:**
- Consumes: `LocaleProvider` (locale-provider.tsx), `LocalizedToaster` (localized-toaster.tsx), `cookies` (`next/headers`), `LOCALE_COOKIE`, `DIR`, `DEFAULT_LOCALE`, `isLocale` (config.ts).

- [ ] **Step 1: Read the current Next docs for `cookies()` and metadata**

Run:
```bash
ls node_modules/next/dist/docs/ 2>/dev/null || echo "no docs dir"
```
Confirm the current signature of `cookies()` (async vs sync) and `generateMetadata` before editing. In Next 16 `cookies()` is async — the layout below `await`s it. Adjust if the installed docs say otherwise.

- [ ] **Step 2: Replace `src/app/layout.tsx`**

Replace the entire file with:
```tsx
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
```

- [ ] **Step 3: Build and manually verify first paint**

Run: `npm run build && npm run dev`
Then:
- Open the app with no `NEXT_LOCALE` cookie → `<html lang="ar" dir="rtl">` (check DevTools Elements).
- In DevTools, set cookie `NEXT_LOCALE=en`, reload → `<html lang="en" dir="ltr">`, title becomes "Sanad — HR Management System".
Expected: both render with no console errors and no RTL/LTR flash.

- [ ] **Step 4: Commit**

```bash
git add src/app/layout.tsx
git commit -m "feat(i18n): SSR locale from cookie sets html lang/dir + metadata"
```

---

### Task 7: Language switch + topbar localization & RTL conversion

**Files:**
- Create: `src/components/layout/language-switch.tsx`
- Modify: `src/components/layout/topbar.tsx`

**Interfaces:**
- Consumes: `useT` (use-t.ts), `Locale` (config.ts).
- Produces: `LanguageSwitch` component (renders the other language label; calls `setLocale`).

- [ ] **Step 1: Create `language-switch.tsx`**

Create `src/components/layout/language-switch.tsx`:
```tsx
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
```

- [ ] **Step 2: Update `topbar.tsx` — localize strings, add switch, convert directional classes**

Replace `src/components/layout/topbar.tsx` with:
```tsx
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
  const initials = name && name !== "—" ? name.trim().charAt(0) : "؟";

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
```

Note the conversions: `right-3`→`start-3`, `pr-10`→`ps-10`, `text-left`→`text-start`; hardcoded `"بحث..."` → `t("common.search")`.

- [ ] **Step 3: Build and manually verify the switch**

Run: `npm run dev`
Then in the running app:
- Click the language switch → UI flips ar↔en **instantly**, no reload, no logout; `<html>` `lang`/`dir` update; search placeholder toggles "بحث..."/"Search...".
- The search icon sits on the correct (leading) side in both directions.
Expected: instant flip, no console errors.

- [ ] **Step 4: Commit**

```bash
git add src/components/layout/language-switch.tsx src/components/layout/topbar.tsx
git commit -m "feat(i18n): header language switch + localized/RTL-correct topbar"
```

---

### Task 8: Sidebar, app-shell & notification-bell — localize + RTL/LTR conversion

**Files:**
- Modify: `src/components/layout/sidebar.tsx`
- Modify: `src/components/layout/app-shell.tsx`
- Modify: `src/components/layout/notification-bell.tsx`

**Interfaces:**
- Consumes: `useT` (use-t.ts).

- [ ] **Step 1: Update `sidebar.tsx` — nav labels via keys, tooltip side, logical position**

In `src/components/layout/sidebar.tsx`:

Replace the `navItems` array (lines ~29–41) so labels become translation keys:
```tsx
const navItems = [
  { key: "navigation.dashboard", href: "/dashboard", icon: LayoutDashboard },
  { key: "navigation.employees", href: "/employees", icon: Users },
  { key: "navigation.attendance", href: "/attendance", icon: Clock },
  { key: "navigation.leaves", href: "/leaves", icon: CalendarDays },
  { key: "navigation.payroll", href: "/payroll", icon: Banknote },
  { key: "navigation.tasks", href: "/tasks", icon: CheckSquare },
  { key: "navigation.requests", href: "/requests", icon: FileText },
  { key: "navigation.approvals", href: "/approvals", icon: ClipboardCheck, badge: true },
  { key: "navigation.reports", href: "/reports", icon: BarChart3 },
  { key: "navigation.documents", href: "/documents", icon: FolderOpen },
  { key: "navigation.settings", href: "/settings", icon: Settings },
];
```

Add the hook + direction inside the component. Change the function opening (line ~43–45) to:
```tsx
export function Sidebar() {
  const { t, dir } = useT();
  const pathname = usePathname();
  const [pending, setPending] = useState(0);
```
Add the import near the other imports:
```tsx
import { useT } from "@/lib/i18n/use-t";
```

Convert the `<aside>` (line ~59) from physical to logical:
```tsx
    <aside className="fixed top-0 start-0 z-40 h-screen w-16 border-e border-border bg-secondary flex flex-col items-center py-4">
```
(`right-0`→`start-0`, `border-l`→`border-e`.)

In the nav map, render the label via `t(item.key)` and make the tooltip side direction-aware. Replace the tooltip content (line ~89–91):
```tsx
              <TooltipContent side={dir === "rtl" ? "left" : "right"} className="font-sans">
                {t(item.key)}
              </TooltipContent>
```

For the logout tooltip (lines ~105–107):
```tsx
        <TooltipContent side={dir === "rtl" ? "left" : "right"} className="font-sans">
          {t("navigation.logout")}
        </TooltipContent>
```

Also convert the badge position on the active nav item (line ~84): `right-0.5` → `end-0.5`:
```tsx
                  <span className="absolute -top-0.5 end-0.5 flex h-4 min-w-4 items-center justify-center bg-destructive px-1 text-[10px] font-bold text-white">
```

- [ ] **Step 2: Update `app-shell.tsx` — logical margin**

Replace `src/components/layout/app-shell.tsx`:
```tsx
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
```
(`mr-16` → `ms-16`.)

- [ ] **Step 3: Update `notification-bell.tsx` — localize static labels + logical position**

In `src/components/layout/notification-bell.tsx`:

Add the import and hook:
```tsx
import { useT } from "@/lib/i18n/use-t";
```
Change the component opening (line ~10–12):
```tsx
export function NotificationBell() {
  const { t } = useT();
  const router = useRouter();
```

Convert directional classes and static strings:
- Badge (line ~51): `-left-0.5` → `-end-0.5`.
- Dropdown container (line ~60): `absolute left-0` → `absolute end-0`.
- Header label (line ~62): `الإشعارات` → `{t("navigation.notifications")}`.
- Mark-all button text (line ~65): `تعليم الكل كمقروء` → `{t("navigation.markAllRead")}`.
- Empty state (line ~73): `لا توجد إشعارات` → `{t("navigation.noNotifications")}`.
- Notification item button (line ~76): `text-right` → `text-start`.

Leave `n.titleAr` / `n.bodyAr` and `toLocaleString("ar")` as-is — dynamic notification content is handled in the later Approvals/notifications module slice.

- [ ] **Step 4: Build and verify the full chrome in both directions**

Run: `npm run dev`
Then:
- In **Arabic**: sidebar on the right, content margin on the right, nav tooltips open to the left, nav labels Arabic.
- Switch to **English**: sidebar moves to the **left**, content margin to the left, tooltips open to the right, nav labels English, notification dropdown aligns correctly.
Expected: no layout overlap, no console errors, instant flip.

- [ ] **Step 5: Run parity + build**

Run: `npm run i18n:parity && npm run build`
Expected: parity OK; build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/components/layout/sidebar.tsx src/components/layout/app-shell.tsx src/components/layout/notification-bell.tsx
git commit -m "feat(i18n): localized + RTL/LTR-correct sidebar, app-shell, notifications"
```

---

### Task 9: Hardcoded-string guard + foundation QA + push

**Files:**
- Create: `scripts/i18n-no-hardcoded.mjs`
- Modify: `package.json` (add `i18n:check` script)

**Interfaces:**
- Consumes: nothing (standalone Node script).
- Produces: `scripts/i18n-no-hardcoded.mjs` — flags Arabic-letter literals in `.tsx` files under a given directory (used by every future module slice as its exit gate).

- [ ] **Step 1: Create the guard script**

Create `scripts/i18n-no-hardcoded.mjs`:
```js
import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

// Usage: node scripts/i18n-no-hardcoded.mjs <dir> [<dir> ...]
const targets = process.argv.slice(2);
if (targets.length === 0) {
  console.error("Usage: node scripts/i18n-no-hardcoded.mjs <dir> [<dir> ...]");
  process.exit(2);
}

const ARABIC = /[؀-ۿ]/;

function walk(dir) {
  const files = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) files.push(...walk(full));
    else if (full.endsWith(".tsx")) files.push(full);
  }
  return files;
}

let offenders = 0;
for (const dir of targets) {
  for (const file of walk(dir)) {
    const lines = readFileSync(file, "utf8").split("\n");
    lines.forEach((line, i) => {
      // Skip the allowlist marker for intentional literals (e.g. brand text).
      if (line.includes("i18n-allow")) return;
      if (ARABIC.test(line)) {
        offenders++;
        console.error(`${file}:${i + 1}: ${line.trim()}`);
      }
    });
  }
}

if (offenders > 0) {
  console.error(`\nFound ${offenders} hard-coded Arabic literal(s). Move them to a catalog or add an "i18n-allow" comment if intentional.`);
  process.exit(1);
}
console.log("No hard-coded Arabic literals found in target(s).");
```

- [ ] **Step 2: Add the npm script**

Edit `package.json` `"scripts"` to add:
```json
"i18n:check": "node scripts/i18n-no-hardcoded.mjs"
```

- [ ] **Step 3: Run the guard against the localized chrome**

Run: `npm run i18n:check src/components/layout`
Expected: it reports the remaining dynamic Arabic in `notification-bell.tsx` (`n.titleAr` is fine, but `toLocaleString("ar")` and any comment text may match). Review each hit:
- If it is a genuine dynamic-data line (out of foundation scope), append `// i18n-allow` to that line.
- The goal for this task: `src/components/layout/language-switch.tsx`, `topbar.tsx`, `sidebar.tsx`, `app-shell.tsx` produce **zero** hits; only intentionally-deferred lines in `notification-bell.tsx` carry `i18n-allow`.

Re-run until only allow-listed lines remain:
Run: `npm run i18n:check src/components/layout`
Expected: `No hard-coded Arabic literals found in target(s).`

- [ ] **Step 4: Full foundation verification**

Run: `npm run test && npm run i18n:parity && npm run build`
Expected: all vitest tests pass; parity OK; build succeeds.

Then `npm run dev` and walk the **manual QA checklist** in both languages:
- Language switch flips ar↔en instantly, no reload/logout.
- `<html lang/dir>` correct on first load (cookie honored) and after switch.
- Sidebar side, content margin, tooltips, breadcrumbs/arrows flip correctly.
- Navigation labels, search placeholder, notification labels translated.
- No console errors; no visible layout breakage in either direction.

- [ ] **Step 5: Commit and push the foundation slice**

```bash
git add scripts/i18n-no-hardcoded.mjs package.json src/components/layout/notification-bell.tsx
git commit -m "chore(i18n): hard-coded-string guard script + foundation QA gate"
git push -u origin feat/localization-i18n
```

---

## Notes for subsequent module slices (not part of this plan)

Each module (Employees, Attendance, Leaves, Requests/Workflows, Payroll, Reports, Dashboard, Tasks, Documents, Expenses/Loans, Approvals, Settings×2–3, Access/Permissions, Auth) gets its **own plan + commit**, following this recipe:
1. Add `src/locales/{ar,en}/<module>.json`; register it in `catalog.ts`.
2. Replace hardcoded strings in that module with `t('<module>.…')`.
3. Convert directional Tailwind utilities to logical; flip directional icons.
4. Swap dynamic-data displays to `localizedName`/`localizedDescription`; verify the API returns `nameEn`/`descriptionEn`.
5. Gate: `npm run test && npm run i18n:parity && npm run i18n:check src/app/(dashboard)/<module> src/components/<module>` → all clean.
6. Manual QA both languages; commit + push the slice.
```
