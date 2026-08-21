# Localization (Arabic / English) — Design Spec

**Date:** 2026-08-22
**Status:** Approved design → ready for implementation planning
**Scope of this spec:** The **foundation slice** (i18n machinery + conventions + global chrome), plus the **module backlog** that every subsequent slice follows mechanically.

---

## 1. Goal & Constraints

Add full Arabic + English localization to the **existing** HR frontend (Next.js 16 App Router, React 19, Tailwind 4, ~175 components / 77 pages / 17 modules). Do **not** rebuild the UI — localize it in place.

Hard requirements (from the request):

- `«العربية / English»` language switch in the main header.
- **Arabic is the default.** Arabic = full RTL, English = full LTR.
- Switching updates the UI **immediately** — no logout, no refresh.
- **Persist** the user's language preference.
- **No hard-coded** Arabic/English strings in components — everything through localization keys (e.g. `requests.leave.title`, `attendance.permission.type`, `employee.department`, `common.save`).
- **No automatic runtime translation.**
- Natural professional HR terminology in both languages — not literal word-for-word.
- Dynamic/system-seeded data uses `NameAr`/`NameEn` + `DescriptionAr`/`DescriptionEn`; tenant-created content may be ar, en, or both.
- Dates, numbers, currency follow the selected locale.
- Tables, icons, arrows, breadcrumbs, sidebars, form layouts flip correctly between RTL/LTR.
- **Same components** for both languages — no duplicated English pages.
- **Fallback to Arabic** (or the existing default label) when an English translation is missing.
- Replace hard-coded strings **incrementally**; commit + push each stable, tested slice separately.

## 2. Confirmed Decisions

| Decision | Choice |
|---|---|
| i18n architecture | **Custom React Context** (`LocaleProvider` + `useT()`), no library, no locale routing |
| Catalog layout | **Per-module namespaced JSON** under `src/locales/<locale>/<module>.json` |
| Persistence | **Cookie (`NEXT_LOCALE`) + localStorage** — per device; SSR reads cookie for first paint |
| Backend preference sync | **Deferred** to a later small slice |
| Arabic numerals | **Latin digits (0–9) in both locales** (`numberingSystem: 'latn'`) |
| Coverage | **All 17 modules**, committed as separate per-module slices after the foundation |
| Default locale | **Arabic (`ar`)**, RTL; English (`en`), LTR |

## 3. Current State (verified)

- Root layout hardcodes `<html lang="ar" dir="rtl">` (`src/app/layout.tsx`); metadata + Sonner toaster hardcoded Arabic/RTL.
- **No i18n library** installed. ~59/77 pages are client components; the "server" pages are static config objects with hardcoded Arabic strings (e.g. `settings/page.tsx`, `settings/organization/grades/page.tsx`).
- Backend **already exposes** `NameAr`/`NameEn`/`DescriptionAr`/`DescriptionEn` broadly across domain entities (5271 occurrences) — dynamic-data localization is mostly a frontend display concern, verified per slice.
- **No frontend test framework** (no jest/vitest in `package.json`). QA relies on scripts + manual checklist (see §8).

## 4. Architecture

```
src/lib/i18n/
  config.ts            # LOCALES=['ar','en']; DEFAULT_LOCALE='ar'; DIR={ar:'rtl',en:'ltr'}; COOKIE='NEXT_LOCALE'
  catalog.ts           # statically imports every src/locales/**/*.json; composes { ar:{…}, en:{…} }
  locale-provider.tsx  # 'use client' — Context provider (see §4.2)
  use-t.ts             # useT(): returns { t, locale, dir, setLocale }
  format.ts            # formatDate / formatNumber / formatCurrency (Intl, latn digits)
  localized-name.ts    # localizedName(entity) / localizedDescription(entity)
src/locales/
  ar/{common,navigation,employees,attendance,…}.json
  en/{common,navigation,employees,attendance,…}.json
```

### 4.1 Catalog & keys

- Each JSON file is a **namespaced tree**; the top-level key is the module (`common`, `navigation`, `employee`, `attendance`, `requests`, …). `catalog.ts` deep-merges all files per locale into a single nested dictionary.
- Key conventions: dotted paths — `common.save`, `common.cancel`, `employee.department`, `attendance.permission.type`, `requests.leave.title`, `validation.required`, `state.empty`, `state.loading`, `state.error`.
- Interpolation: `t('greeting', { name })` replaces `{name}` tokens.
- Missing-key resolution: **current locale → Arabic → raw key** (raw key surfaces gaps in dev). English-missing therefore renders the Arabic string (satisfies the Arabic-fallback requirement).

### 4.2 `LocaleProvider` (client)

- Holds `locale` state (initialized from a server-provided `initialLocale` prop to avoid hydration mismatch).
- `setLocale(next)`:
  1. `setState(next)`
  2. write cookie `NEXT_LOCALE=next` (1-year, path=/) + `localStorage`
  3. imperatively set `document.documentElement.lang = next` and `dir = DIR[next]`
  4. no navigation, no refresh — Context re-render updates all `useT()` consumers instantly.
- Exposes memoized `t` bound to the active locale dictionary.

### 4.3 Server first-paint wiring

- Root `layout.tsx` becomes/stays a server component: read cookie via `next/headers` `cookies()`, compute `locale`/`dir`, render `<html lang={locale} dir={dir}>`, and wrap children in `<LocaleProvider initialLocale={locale}>`.
- Toaster `dir` + any layout-level Arabic strings become locale-driven.
- **Implementation note:** AGENTS.md warns this Next.js is modified — read `node_modules/next/dist/docs/` for the current `cookies()` / layout APIs before coding.

### 4.4 Formatting (`format.ts`)

- `formatDate(value, opts)` → `Intl.DateTimeFormat(localeTag, { numberingSystem:'latn', …})`; localeTag = `ar-SA` | `en`.
- `formatNumber` / `formatCurrency(value)` → `Intl.NumberFormat` with `numberingSystem:'latn'`, currency `SAR`.
- Latin digits in both locales (confirmed decision). Payroll/CSV/SIF exports are unaffected (already Latin).

### 4.5 Dynamic data (`localized-name.ts`)

- `localizedName(entity)`: `locale==='en' && entity.nameEn ? nameEn : (entity.nameAr ?? entity.name ?? '')`. Field casing normalized to the API's actual shape per slice (API returns camelCase JSON).
- `localizedDescription(entity)` analogous.
- Tenant content that has only one language renders that language (no fabrication) — the helper's non-empty checks handle this.

## 5. Language switch component

- `src/components/layout/language-switch.tsx` — a compact `«العربية / English»` control in `topbar.tsx`.
- Shows the **other** language as the actionable label; calls `setLocale`. Accessible (button, `aria-label` localized).

## 6. RTL / LTR correctness

- **Foundation slice** converts global chrome — `app-shell.tsx`, `sidebar.tsx`, `topbar.tsx`, `notification-bell.tsx` — from physical Tailwind utilities (`ml/mr/pl/pr/left/right`) to **logical** ones (`ms/me/ps/pe/start/end`). Tailwind 4 logical utilities respond to the `dir` attribute automatically.
- **Directional icons** (chevrons, back arrows, breadcrumb separators, sidebar collapse): flip via `dir`-aware rendering — either swap the icon or apply `rtl:-scale-x-100` / `ltr:` variants. A small `<DirectionalIcon>` helper (or `useT().dir`) standardizes this.
- **Each module slice** converts its own directional utilities as it is localized; the foundation establishes the pattern + fixes shared chrome.
- Charts/tables: verify recharts axis orientation and table header alignment per module (mostly CSS-driven by `dir`).

## 7. Scope breakdown

### 7.1 Foundation slice (this spec — one commit)

1. `src/lib/i18n/*` (config, catalog, provider, use-t, format, localized-name).
2. `src/locales/{ar,en}/common.json` + `navigation.json` + `validation.json` (shared: buttons, empty/loading/error, dialogs/confirmations, common form + validation strings, nav labels).
3. Root layout SSR cookie wiring + `LocaleProvider`.
4. `language-switch.tsx` in the topbar.
5. Global chrome RTL/LTR conversion (sidebar/topbar/app-shell/notification-bell) + fully localized navigation.
6. **Tooling:** `scripts/i18n-parity.mjs` (ar/en key parity + missing-key report) and a hardcoded-Arabic grep check; documented manual QA checklist.

### 7.2 Module slices (each its own commit, in order)

Employees → Attendance → Leaves → Requests/Workflows → Payroll → Reports → Dashboard → Tasks → Documents → Expenses/Loans → Approvals → **Settings** (split into 2–3 sub-slices: access, organization/master-data, payroll/requests/documents config) → Access/Permissions → Auth (login/register).

Every module slice follows the same recipe:
1. Add `src/locales/{ar,en}/<module>.json`.
2. Replace hardcoded strings in that module's pages/components with `t('<module>.…')`.
3. Convert directional utilities to logical; flip directional icons.
4. Swap dynamic-data displays to `localizedName`/`localizedDescription`; verify the API returns both fields.
5. Run parity script + hardcoded-string grep; manual QA in both languages.
6. Commit + push the slice.

## 8. Testing & verification

- **`scripts/i18n-parity.mjs`**: fails if any key exists in one locale but not the other; reports missing/empty values. Run per slice.
- **Hardcoded-string check**: grep for Arabic-letter literals (`[؀-ۿ]`) in `.tsx` under a module's directory — should be empty once a module is localized (allowlist for intentional cases).
- **Manual QA checklist** (no FE test framework): for each module, in **both** ar and en — navigation labels, page titles, buttons, forms + field labels, validation messages, notifications/toasts, empty/loading/error states, dialogs/confirmations, table header alignment + row flow, icon/arrow/breadcrumb direction, dynamic names, date/number/currency formatting.
- **Printed documents:** bilingual document templates already supported server-side (QuestPDF NameAr/NameEn) — verify the selected-locale field is chosen where templates support it; out of scope to add new bilingual templates.

## 9. Out of scope (this program)

- Backend preferred-language persistence (deferred small slice).
- Adding new bilingual print templates (only use existing bilingual support).
- Automatic/machine translation of any kind.
- Third+ languages (structure supports adding later via a new locale folder + config entry).

## 10. Risks & mitigations

- **Hydration mismatch** on locale-dependent first render → mitigated by server-provided `initialLocale` from cookie.
- **Missed hardcoded strings** → grep check + parity script + per-module manual checklist.
- **Directional CSS regressions** → convert incrementally per module, visual QA both directions; global chrome done once in foundation.
- **Modified Next.js APIs** (AGENTS.md) → read `node_modules/next/dist/docs/` before touching layout/cookies.
- **Translation quality** → use natural HR terminology; keys reviewed against professional bilingual HR vocabulary, not literal translation.
