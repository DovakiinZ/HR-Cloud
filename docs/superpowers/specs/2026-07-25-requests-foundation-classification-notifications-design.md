# Requests Productionization — SP0 Foundation: Field Classification + Manager Notifications

**Date:** 2026-07-25
**Status:** Approved design, pre-implementation
**Program:** "Connect the 9 essential HR request types to real system effects." This is the shared
**Foundation** (SP0) every subsequent request sub-project builds on. Order after this: Leave → Attendance
Correction → Attendance Permission → Overtime → Expense → Salary Advance → Employee Loan → Asset
Request/Return → Employee Data Update.

## Problem

Two shared governance capabilities are missing and are needed by all/most of the 9 request types:

1. **Field classification.** `FormField` (backend/src/HR.Domain/Engines/Forms/FormField.cs) has only
   `IsRequired` — no way to mark a field **System-Required / Business-Required / Optional / Custom**, and no
   guard preventing an admin from deleting or disabling a field that a required effect depends on. Forms are
   editable post-seed via `FormsController` (AddField/UpdateField/DeleteField/reorder), so an admin can today
   delete a field a required effect maps to; activation validation catches it only later.
2. **Manager notification recipients.** `Notification.Send` + `NotificationSendExecutor` resolve
   employee/email recipients but cannot target the requester's **manager**, so "notify employee *and*
   manager" cannot be configured.

## Goals

- Add a field-classification convention on `FormField`, with server-enforced locking of System-Required
  fields and a business-friendly UI badge.
- Expose the requester's **manager** as request-context values so a `Notification.Send` effect can target
  them — no executor change required for the common case.
- Everything **additive and backward-compatible**: nullable column, defaulted classification, new context
  keys. No existing effect, form, or request instance changes behavior. Tenant customizations preserved.

**Non-goals (deferred):** role-based notification recipients (`recipientRole`) — added later when a real
request flow needs dept/role routing. No rebuild of the forms, requests, or effect engines.

## Grounding (verified 2026-07-25)

- `FormField` fields: Code, NameEn/NameAr, FieldType, `IsRequired`, SortOrder, SectionName, Placeholder,
  DefaultValue, ValidationRules(json), Options(json). **No `MetadataJson`.**
- Field CRUD: `FormsController` (backend/src/HR.Modules/Platform/Controllers/FormsController.cs) —
  `POST {id}/fields`, `PUT {id}/fields/{fieldId}`, `DELETE {id}/fields/{fieldId}`, `PUT {id}/fields/reorder`.
  The backing form service is where the lock guard must live.
- `EffectConfigurationValidator` already fails activation if a required effect input maps to a form-field
  Code that no longer exists — so the lock guard is defense-in-depth that fails *early* with a clear message.
- `RequestProvisioningService.CurrentSeedVersion = 2`; `ReconcileRequiredEffects` is additive and preserves
  tenant customizations. Seeding is idempotent per code.
- `RequestContextKeys` + the request-context resolver (used by `EffectValueResolver`) expose request/employee
  values (employeeId, requestId, requestNumber, leave snapshot). `Employee.ManagerId` exists;
  `Employee.Email` exists. `INotificationService.NotifyAsync(userId, …)` targets one user; callers resolve
  manager/role ids today.

## Design

### A. Field classification

**A1. Data.** Add nullable `MetadataJson` (jsonb) to `FormField`. Reserved shape:
```json
{ "classification": "SystemRequired" | "BusinessRequired" | "Optional" | "Custom", "isLocked": true }
```
`isLocked` is derived from classification (SystemRequired ⇒ locked) but stored explicitly for the UI. A
`FormFieldClassification` static helper parses `MetadataJson` with a **total** default: absent/invalid →
`Optional`. (Provisioning stamps real values; see A4.)

**A2. Classification semantics.**
| Classification | Delete | Disable / clear IsRequired | Rename label & help text | Reorder | Change internal `Code` | Feed a required effect input |
| --- | --- | --- | --- | --- | --- | --- |
| **SystemRequired** | ❌ | ❌ | ✅ (NameEn/NameAr/Placeholder only) | ✅ | ❌ | yes (locked mapping) |
| **BusinessRequired** | ❌ while required | ✅ | ✅ | ✅ | ❌ | optional |
| **Optional** | ✅ | ✅ | ✅ | ✅ | ❌ | optional |
| **Custom** | ✅ | ✅ | ✅ | ✅ | ✅ (tenant owns it) | only via a registered catalog input (never a raw column) |

The internal `Code` is immutable for system-owned fields (SystemRequired/BusinessRequired/Optional seeded by
the system); only **Custom** fields (tenant-authored) may set their own Code. Label/help-text edits are
always allowed.

**A3. Server-side guards** (in the form service behind `FormsController`; the UI mirrors them but the server
is the real protection). On UpdateField / DeleteField / reorder:
- `DeleteField` on a SystemRequired field → `ForbiddenException` (bilingual: "حقل نظامي مطلوب ولا يمكن حذفه" /
  "System-required field cannot be deleted").
- `UpdateField` that would clear `IsRequired` or disable a SystemRequired field → `ForbiddenException`.
- `UpdateField` that changes the `Code` of any non-Custom field → `ForbiddenException`.
- Label/help-text/order changes on any field → allowed.
- A field that is currently mapped as a required effect input cannot be deleted regardless of classification
  (cross-check against the request type's required effect definitions) — closes the early-failure gap.

**A4. Seeding & backfill.** The request seeder stamps each system field's classification: a field whose Code
is referenced by a required effect input (from `SystemRequestEffects.Required` mappings) → `SystemRequired`;
all other seeded fields → `Optional` (BusinessRequired is opt-in per request in later sub-projects).
`RequestProvisioningService` backfills existing tenants' fields **only where `MetadataJson` has no
classification** — never overwriting a tenant's edits. Bump `CurrentSeedVersion` 2 → 3.

**A5. UI.** In the form editor, each field shows a read-only business badge for its classification (e.g.
«حقل نظامي مطلوب» / "System-required"), and the delete + "required" toggle controls are disabled for
SystemRequired fields. The internal `Code` field is read-only for non-Custom fields. No raw entity/column/
class names are shown.

### B. Manager notification recipients

**B1.** Extend `RequestContextKeys` with `managerUserId` and `managerEmail`, and extend the request-context
resolver to populate them from the requester's `Employee.ManagerId` → manager `Employee` (UserId, Email).
Null-safe: if there is no manager or no manager email, the keys resolve to null/empty and a
`Notification.Send` mapped to them **skips gracefully** (the executor already returns Skip on unresolved
recipient).

**B2.** With B1, "notify manager" needs **no executor change**: it is a second `Notification.Send` effect
whose `toEmail` input is mapped from `RequestContext:managerEmail`. Later request sub-projects add these two
notification effects (employee + manager) as required async effects.

**B3. Deferred (out of scope here):** `recipientRole` fan-out on `NotificationSendExecutor` — added when a
request flow needs role/dept routing.

## Compatibility

- `FormField.MetadataJson` is nullable; existing rows and the classification helper default to `Optional`,
  so nothing that works today changes.
- New context keys are additive; existing effect mappings are untouched.
- Provisioning backfill is additive and non-destructive (only stamps where classification is absent).
- Target: full backend test suite stays green.

## Testing

- `FormFieldClassification` helper: each classification parses; absent/invalid → `Optional`; `isLocked`
  derivation.
- Form service guards: delete SystemRequired → blocked; disable/clear-required SystemRequired → blocked;
  change Code of non-Custom → blocked; delete a field mapped by a required effect → blocked; label/help-text
  edit on SystemRequired → allowed; delete Optional → allowed; Custom field free edits → allowed.
- Manager resolution: `managerEmail`/`managerUserId` resolve for an employee with a manager; null-safe when
  no manager / no email; a `Notification.Send` mapped to `managerEmail` skips gracefully when unresolved.
- Provisioning backfill: stamps classification where absent; **does not** overwrite an existing tenant
  classification; idempotent across runs; system-required fields correctly identified from required-effect
  input mappings.

## Delivery (focused commits, push each before the next)

1. `FormField.MetadataJson` migration + `FormFieldClassification` helper + unit tests.
2. Form-service lock guards (delete/disable/Code/required-mapping) + tests.
3. Seeding + provisioning backfill (SeedVersion 3) + tests.
4. Manager request-context keys (`managerUserId`/`managerEmail`) + resolver + tests.
5. UI: classification badge + disabled controls for locked fields.

(Deploy — migration apply + redeploy — is a later, user-gated step, batched after the Foundation and the
first requests, not per commit.)
