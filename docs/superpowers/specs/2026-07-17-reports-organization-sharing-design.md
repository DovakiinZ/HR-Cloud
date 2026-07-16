# Reports Organization + Sharing UI (SP-1b) — Design

**Date:** 2026-07-17
**Status:** Approved (design)
**Part of program:** Reports completion (SP-1a builder power-up ✅ → **SP-1b organization/sharing** → SP-2 scheduling infra → SP-3 dashboards backlog).

## Context / current state (verified 2026-07-17)

The Reports backend for organization (folders/tags/favorites/pin) and sharing is **complete and deployed**; the canonical `src/lib/api/reports.ts` client already exposes all of it, and the reports list (`getReports`) already accepts `{ view, folderId, tagId }`. This sub-project is **frontend-only, no migration**.

Verified client surface (already merged, do NOT modify the client):
- **List filters:** `getReports({ page?, pageSize?, search?, scope?, view?: "favorites"|"recent"|"pinned", folderId?, tagId? })`. `ReportDefinition` carries `folderId`, `tags: ReportTag[]`, `isFavorite`, `isPinned`.
- **Folders:** `getReportFolders()`, `createReportFolder({ nameEn, nameAr, parentFolderId? })`, `updateReportFolder(id, {...})`, `deleteReportFolder(id)`, `setReportFolder(reportId, folderId|null)`. `ReportFolder = { id, nameEn, nameAr, parentFolderId? }`.
- **Tags:** `getReportTags()`, `createReportTag({ name, color? })`, `deleteReportTag(id)`, `assignReportTag(reportId, tagId)`, `unassignReportTag(reportId, tagId)`. `ReportTag = { id, name, color? }`.
- **Per-user state:** `toggleReportFavorite(reportId) → boolean`, `toggleReportPin(reportId) → boolean`.
- **Sharing:** `getReportShares(reportId) → ReportShare[]`, `addReportShare(reportId, { sharedWithUserId?, sharedWithRoleId?, sharedWithDepartmentId?, canEdit })`, `removeReportShare(reportId, shareId)`. `ReportShare = { id, reportDefinitionId, sharedWithUserId?, sharedWithRoleId?, sharedWithDepartmentId?, canEdit, sharedAt }`.

Reuse: the existing **dashboard share dialog** `src/components/dashboard/share-dialog.tsx` is the exact pattern (same target tabs, same `sharedWith*`/`canEdit` shape, same lookups). Lookups: `getDepartments()`+`orgLabel` (`@/lib/api/org`), `apiFetch("/api/roles")` (`{ id, name, nameAr? }[]`), `apiFetch("/api/users")` (`{ id, fullName?, email }[]`). Reports gating: `usePermission("Platform.Reports.Edit")` for share/organize actions.

Current list: `src/app/(dashboard)/reports/page.tsx` — a single table with New/Open/Edit/export, no organization.

Design principle: reuse the canonical client + the dashboard share-dialog pattern; keep the list page readable by extracting the sidebar and share dialog into their own components.

---

## Component 1 — Reports share dialog

**File:** `src/components/reports/share-dialog.tsx` (new)

A near-copy of `src/components/dashboard/share-dialog.tsx`, retargeted to reports:
- Props: `{ reportId: string; reportName: string; shares: ReportShare[]; onClose: () => void; onChanged: () => void }`.
- Same target tabs (department/role/user), same `/api/users` + `/api/roles` + `getDepartments()` lookups, same `canEdit` checkbox, same current-shares list with remove.
- Add → `addReportShare(reportId, { sharedWithUserId, sharedWithRoleId, sharedWithDepartmentId, canEdit })`; remove → `removeReportShare(reportId, share.id)`. Import `ReportShare` from `@/lib/api/reports`.

## Component 2 — Reports sidebar (views + folders + tags)

**File:** `src/components/reports/reports-sidebar.tsx` (new)

A left rail the list page renders. Props: `{ view, folderId, tagId, onSelectView, onSelectFolder, onSelectTag, folders, tags, onFoldersChanged, onTagsChanged, canEdit }`.
- **View tabs:** All / المفضلة (favorites) / الأخيرة (recent) / المثبّتة (pinned) → `onSelectView("" | "favorites" | "recent" | "pinned")` (clearing folder/tag).
- **Folders list:** each folder is clickable (`onSelectFolder(id)`); the active folder highlights. When `canEdit`: a "＋ مجلد" inline create (nameAr/nameEn → `createReportFolder`), and per-folder rename (`updateReportFolder`) + delete (`deleteReportFolder`, then `onFoldersChanged`). A "الكل" entry clears the folder filter.
- **Tags list:** each tag clickable (`onSelectTag(id)` toggles the tag filter); when `canEdit`: create (`createReportTag({ name })`) + delete (`deleteReportTag`). Active tag highlights.
- Folders/tags data (`getReportFolders`/`getReportTags`) is loaded by the list page and passed down; mutations call `onFoldersChanged`/`onTagsChanged` to refetch.

## Component 3 — List page: sidebar layout + row organization actions + sharing

**File:** `src/app/(dashboard)/reports/page.tsx` (rewrite)

- **Layout:** two columns on `md+` — `<ReportsSidebar>` (left) + the reports table (right); stacked on mobile. State: `view`, `folderId`, `tagId`, plus `folders`, `tags`, `shareTarget` (the report whose share dialog is open) and its `shares`.
- **Fetch:** `getReports({ pageSize: 100, view, folderId, tagId })` re-runs whenever `view/folderId/tagId` change. Load `getReportFolders()` + `getReportTags()` once (and on change).
- **Row actions (added to each report row, gated by `canEdit` where mutating):**
  - **Favorite star** — filled when `r.isFavorite`; click → `toggleReportFavorite(r.id)` then refetch. (Gate: `Platform.Reports.View` — favorites are per-user; keep it available to viewers.)
  - **Pin** — filled when `r.isPinned`; click → `toggleReportPin(r.id)` then refetch.
  - **Tag chips** — render `r.tags` as small chips; a "الوسوم" button opens a compact inline panel (checkbox list of all `tags`, checked = assigned) toggling `assignReportTag`/`unassignReportTag` then refetch. (canEdit)
  - **Folder select** — a small dropdown of `folders` (+ "بدون مجلد") bound to `r.folderId` → `setReportFolder(r.id, value || null)` then refetch. (canEdit)
  - **Share button** — opens `<ReportShareDialog>` after loading `getReportShares(r.id)`. (canEdit)
- Keep existing New/Open/Edit/export exactly as today. The table gains a compact "تنظيم" (organize) action group per row containing star/pin/folder/tags/share so the layout stays readable.

---

## Testing & gates
- **Frontend only.** `npx next build` = 0 errors is the gate (no FE test runner). No backend, no migration.
- Manual verification against the live API: create a folder + tag, assign a report to each, filter by folder/tag/view, favorite/pin, and share a report with a role/department (then revoke).
- **Deploy:** push → Vercel auto-deploys the FE. No API deploy needed (client + endpoints already live).

## Known limits carried forward
- Folder tree is rendered flat (parentFolderId supported by the backend but shown as a flat list this increment; nesting UI is a later polish).
- Tag color (`ReportTag.color`) is stored but not surfaced as a color swatch this increment (chips are neutral).
- Recent view ordering is backend-driven (`LastViewedAt`); the UI just requests `view=recent`.

## Self-review
- No placeholders; each component names its file, the exact client calls, and behavior.
- Consistent: reuses the canonical client + the dashboard share-dialog pattern; no backend/migration.
- Scope: one implementation plan (share dialog + sidebar + list rewrite). Builder power-up was SP-1a; scheduling infra + dashboards are separate sub-projects.
- Ambiguity resolved: favorites/pin available to viewers (per-user), organize/share mutations gated by `Platform.Reports.Edit`; folders/tags loaded by the page and passed to the sidebar; share dialog mirrors the dashboard one against the reports client.
