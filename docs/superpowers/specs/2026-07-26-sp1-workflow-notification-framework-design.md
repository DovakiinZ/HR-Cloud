# SP1 — Workflow-Driven Notification Framework (Leave as first consumer)

**Date:** 2026-07-26
**Program:** Dynamic HR Requests productionization → "Connect 9 essential request types to real effects"
**Supersedes:** the earlier fixed-`Notification.Send`-per-type approach for SP1 Leave.
**Status:** Design — approved for spec commit + plan.

## 1. Goal

Replace the hardcoded, fixed-recipient notifications that `RequestEngine` fires inline today with a
**configurable, workflow-event-driven notification framework**: a workflow event resolves to
notification rules, each rule resolves to recipients, and each recipient is delivered a rendered
message through the existing bell + email infrastructure. Leave Request is the first consumer; the
framework is generic across all request types.

The overlap/balance validation the previous SP1 draft planned to "add" **already exists and is
enforced** at submission (`RequestEngine.cs:110`, via `ILeaveService.PreviewAsync`). It is out of
scope here — nothing to build.

## 2. Scope

### In scope (the spine + reuse-now events)

- One new rule entity + one new dispatch-ledger entity + one migration + indexes.
- `WorkflowNotificationEvent` enum (all 12 values defined; **6 dispatched**).
- `NotificationRecipientType` enum (all 13 values defined; reuse-now subset resolved).
- `INotificationRecipientResolver` — promotes existing `RequestEngine` resolvers to a shared,
  list-returning service.
- `IWorkflowNotificationDispatcher` — the centralized "event → rule → resolver → delivery" service.
- `RequestEngine` refactor: 6 inline notify calls → dispatcher calls.
- `SystemWorkflowNotificationRules` code defaults for `LEAVE_REQUEST` + non-destructive
  provisioning reconcile + `SeedVersion` 3→4.
- Recipient JSON validation, token whitelist, precedence, dedup, idempotency, failure isolation.
- Tests for every safeguard.

### Deferred (each becomes a small add on top of the spine, its own SP)

- **SLA reminder + escalation** — needs a scheduler/hosted service; only `RequestApproval.DueAt` exists today.
- **"More information requested"** — needs a new `RequestApprovalStatus` + endpoint.
- **Effect executed / effect failed** events — need hooks in `CompletionEngine`.
- **Request cancelled** dispatch — `CancelAsync` has no notify today; enum value defined, dispatch deferred.
- **Form-selected employee** recipient — needs a new `EmployeeReference` field type.
- **Template CRUD admin UI** — the 8-tab settings UI is already a separate planned sub-project.

The two deferred enums' unimplemented values are **hidden from admin APIs** by a capability registry (§10).

## 3. Architecture — four stages

```
Workflow transition (RequestEngine)
  → IWorkflowNotificationDispatcher.DispatchAsync(event, instance, step)
      → rule lookup  (WorkflowNotificationRule, by precedence §6)
      → recipient resolution  (INotificationRecipientResolver, per RecipientSpec §5)
      → template render  ({{token}} via existing DocumentTokenResolver, §8)
      → idempotency + dedup guard  (§7)
      → delivery  (existing INotificationService.NotifyAsync → bell + email queue)
```

**Reused unchanged:** `INotificationService`, `EmailNotificationQueue` + `EmailQueueDrainer` + ACS,
`DocumentTokenResolver` / `ResolveTokens`, `NotificationsController`.

**Direct dispatcher call, not a MediatR event bus** — one consumer today; a single-handler event
bus would be an interface with one implementation. The dispatcher *is* the pipeline.

## 4. Failure isolation (safeguard)

Notification work must **never** roll back or fail a request transition.

- `DispatchAsync` wraps its entire body in try/catch. Any exception is logged (with request id,
  event, rule id) and swallowed — the transition proceeds.
- Per-recipient resolution/delivery is independently guarded: one failing recipient is logged and
  skipped; the rest still deliver.
- Delivery is **enqueue-only** (`INotificationService` writes a bell row + an `EmailNotificationQueue`
  row inside the current unit of work). Actual send is the existing drainer's job, so a mail
  transport outage cannot affect the workflow, and failed sends **retry through the existing email
  queue** (`Attempts`/`MaxAttempts`, already implemented).
- The dispatcher never throws for "no rules matched" or "no recipient resolved" — both are normal.

## 5. Recipient model + validation

### RecipientSpec (stored as validated JSON on the rule)

`RecipientsJson` is an object envelope carrying a schema version and the recipient list:

```json
{ "v": 1, "recipients": [ { "type": "CurrentApprover" }, { "type": "Role", "refId": "<guid>" } ] }
```

- `type` — a `NotificationRecipientType` name.
- `refId` — required only for types that reference an entity (`SpecificEmployee`, `Role`); forbidden
  for all others.

### `NotificationRecipientType` (all 13 defined; resolved subset marked ✅)

| Type | SP1 | Resolution (reused) |
|------|-----|---------------------|
| Requester | ✅ | `instance.EmployeeId` → `Employee.UserId` |
| EmployeeConcerned | ✅ | same as requester for self-submitted; = `instance.EmployeeId` |
| CurrentApprover | ✅ | `RequestApproval` where `Status=Pending`, min `StepOrder` → `AssignedToUserId` |
| PreviousApprover | ✅ | `RequestApproval` with `DecidedByUserId != null`, max `StepOrder < current` |
| DirectManager | ✅ | `ManagerUserAsync(employee.ManagerId)` |
| DepartmentManager | ✅ | `DepartmentHeadUserAsync(employee.DepartmentId)` |
| SpecificEmployee | ✅ | `refId` (employee id) → `Employee.UserId` |
| Role | ✅ | **all** active users with `refId` role (list, not FirstOrDefault) |
| HrTeam | ✅ | all active users in a role whose name ILIKE `%HR%` (list) |
| FinanceTeam | ✅ | all active users in a role whose name ILIKE `%Finance%` (list) |
| StepAssignees | ✅ | all `AssignedToUserId` across the instance's `RequestApproval` rows |
| FormSelectedEmployee | ⛔ deferred | needs `EmployeeReference` field type — log + skip until its SP |
| Custom | ⛔ deferred | reserved — log + skip until its SP |

The resolver returns `IReadOnlyList<Guid>` user ids (0..N). Empty result = log + skip that recipient.
It **never** falls back to another person.

### Validation (create / update / provisioning)

`RecipientSpec.ParseAndValidate(json)` enforces, rejecting with a clear error on failure:

1. Valid JSON, known envelope shape, `v` present and supported.
2. Every `type` is a **supported** recipient (per capability registry §10) — unknown/deferred → reject.
3. `refId` present iff the type requires it; absent otherwise. Malformed guid → reject.
4. **No unknown properties** on the envelope or any recipient object (strict; extra keys → reject).
5. **Max recipient count** = 20 → reject beyond.
6. **Duplicate recipient definitions removed** (same type+refId collapses to one) before persist.

Validation runs at the admin API boundary *and* again in the provisioning seed path (defensive).

## 6. Rule precedence (safeguard)

For a `(requestTypeCode, event, stepOrder)` dispatch, candidate rules are ranked by specificity.
The **most specific non-empty tier wins**; lower tiers are not applied:

1. `RequestTypeCode == code  &&  Event == e  &&  StepOrder == step`
2. `RequestTypeCode == code  &&  Event == e  &&  StepOrder == null`
3. `RequestTypeCode == null  &&  Event == e  &&  StepOrder == step`
4. `RequestTypeCode == null  &&  Event == e  &&  StepOrder == null`

Only `IsActive` rules are considered. Within the winning tier, **all** rules apply (a tier may hold
several rules targeting different recipients); their resolved users are unioned and de-duplicated
(§7). Precedence is deterministic and unit-tested.

## 7. Duplicate-delivery prevention + idempotency (safeguard)

Two layers:

- **Within one dispatch:** union all resolved user ids across the winning tier's rules and
  `Distinct()` before delivery — a user matched by two recipient specs is notified once per event.
- **Across dispatches (idempotency):** a `WorkflowNotificationDispatch` ledger row keyed by the
  deterministic tuple **`(RequestInstanceId, Event, StepOrder, RuleId, UserId)`** with a unique
  index. Before delivering, the dispatcher inserts the key; a conflict means "already delivered" →
  skip. This makes a replayed/duplicated transition (retry, double-click) a no-op for delivery.
  `StepOrder` is part of the key so the same rule legitimately fires again for a later step.

## 8. Templates + token whitelist (safeguard)

- Subject/body live on the rule: `SubjectAr`, `SubjectEn`, `BodyAr`, `BodyEn`. Scoping the rule by
  request type + optional step *is* "templates configurable per request type and workflow step" —
  no separate template entity in SP1.
- Rendering reuses `DocumentRenderer.ResolveTokens` + `DocumentTokenResolver.ResolveForRequestAsync`
  (`{{Employee.FullName}}`, `{{Request.Number}}`, `{{Leave.StartDate}}`, …).
- **Token whitelist:** the set of keys `DocumentTokenResolver` produces is the source of truth.
  - On rule create/update, subject/body are scanned for `{{token}}`; any token **not** on the
    whitelist yields a **validation warning** (surfaced to the caller), not a hard failure and not a
    rejection.
  - At render time, unknown tokens are **left visible** verbatim (existing `ResolveTokens` behavior),
    never resolved against arbitrary object properties. No reflection, no open property access.

## 9. Seeding + provisioning (safeguard: never overwrite tenant rules)

- `SystemWorkflowNotificationRules` declares code defaults per request code (mirrors
  `SystemRequestEffects`). Each seeded rule carries a stable `SystemKey`
  (e.g. `LEAVE_REQUEST:Submitted:Requester`).
- Rule entity carries: `IsSystemOwned` (bool), `SystemKey` (string?, unique per tenant when set),
  `IsCustomized` (bool, set true when a tenant edits a system rule via API).
- `RequestProvisioningService` gains `ReconcileWorkflowNotificationRules(type)` called alongside the
  existing reconcilers (`RequestProvisioningService.cs:95`), and `CurrentSeedVersion` 3 → 4.
- Reconcile logic (non-destructive, mirrors SP0's `HasClassification` guard):
  - **Insert** a system rule only if no rule with that `SystemKey` exists for the tenant (missing-only).
  - **Safely upgrade** an existing system rule (`IsSystemOwned && !IsCustomized`) in place if the
    shipped definition changed.
  - **Never touch** a tenant-authored rule (`!IsSystemOwned`) or a customized system rule
    (`IsCustomized`).

### Seeded LEAVE_REQUEST rules (encode the requested behavior)

| SystemKey | Event | Recipients | Note |
|-----------|-------|-----------|------|
| `LEAVE_REQUEST:Submitted:Requester` | Submitted | Requester | confirmation to requester on submit |
| `LEAVE_REQUEST:StepAssigned:CurrentApprover` | StepAssigned | CurrentApprover | approver notified when their step activates |
| `LEAVE_REQUEST:Rejected:Requester` | Rejected | Requester | |
| `LEAVE_REQUEST:Returned:Requester` | Returned | Requester | |
| `LEAVE_REQUEST:FinalApproved:Requester` | FinalApproved | Requester | plus any tenant-added recipients for this event |

HR / attendance teams are **not** seeded — they are notified only if a tenant adds a rule listing
`HrTeam`/`Role`, satisfying "only if part of the workflow or notification configuration".

## 10. Capability registry (safeguard: hide unimplemented enum values)

`NotificationCapabilityRegistry` exposes the **supported** subset:

- `SupportedEvents` = { Submitted, StepAssigned, StepApproved, Rejected, Returned, FinalApproved }.
- `SupportedRecipientTypes` = the ✅ rows in §5.

The registry is the single source used by (a) `RecipientSpec` validation and event validation to
reject rules referencing unsupported values, and (b) any future admin/metadata API listing available
events/recipients. Deferred enum values exist in code (forward-compat, no later migration) but are
invisible and unusable through APIs until their resolver/dispatch lands and is added to the registry.

## 11. Data model summary

### `WorkflowNotificationRule : TenantEntity`

| Column | Type | Notes |
|--------|------|-------|
| RequestTypeCode | string? | null = applies to all types |
| Event | int | `WorkflowNotificationEvent` |
| StepOrder | int? | null = any step |
| RecipientsJson | string | validated envelope (§5); `v` = schema version |
| SubjectAr / SubjectEn | string | token template |
| BodyAr / BodyEn | string | token template |
| ChannelBell | bool | default true |
| ChannelEmail | bool | default true |
| IsActive | bool | default true |
| IsSystemOwned | bool | seeded rules |
| SystemKey | string? | stable seed identity (unique per tenant when set) |
| IsCustomized | bool | tenant edited a system rule |

**Indexes:** composite `(TenantId, RequestTypeCode, Event, IsActive)` covering the dispatcher's hot
lookup, plus `(TenantId, StepOrder)` and a unique `(TenantId, SystemKey)` (filtered to non-null).
Individual filterability on `TenantId, RequestTypeCode, Event, StepOrder, IsActive` is satisfied by
the composite's leading columns.

### `WorkflowNotificationDispatch : TenantEntity`

| Column | Type | Notes |
|--------|------|-------|
| RequestInstanceId | Guid | |
| Event | int | |
| StepOrder | int | -1 sentinel when step-agnostic (keeps the key non-null) |
| RuleId | Guid | |
| UserId | Guid | |

**Unique index:** `(RequestInstanceId, Event, StepOrder, RuleId, UserId)` — the idempotency key.

Both tables ship in **one** migration `WorkflowNotifications`.

## 12. RequestEngine integration points (the refactor)

Replace the inline notifies; dispatch at these 6 points:

| Point | File:line (current) | Event dispatched |
|-------|---------------------|------------------|
| Submit, after chain built | `RequestEngine.cs:144` (first-approver notify) | `Submitted` (→requester) **and** `StepAssigned` (→step 1) |
| Advance to next step | `RequestEngine.cs:224` (next-approver notify) | `StepAssigned` |
| Step approved (non-final) | `RequestEngine.cs:209` | `StepApproved` |
| Reject | `RequestEngine.cs:203` (submitter notify) | `Rejected` |
| Return | `RequestEngine.cs:274` (submitter notify) | `Returned` |
| Final approval (on `completion.Success`) | `RequestEngine.cs:238` (submitter notify) | `FinalApproved` |

The old hardcoded `NotifyAsync`/`NotifySubmitterAsync` calls are removed; the seeded Leave rules
reproduce (and extend, adding the submit-confirmation to the requester) the prior behavior, so there
is no regression. The private resolver methods move to `INotificationRecipientResolver`.

## 13. Testing (safeguard)

Unit/integration tests, at minimum:

- **Rule precedence** — the most-specific non-empty tier wins; lower tiers suppressed; deterministic.
- **Tenant isolation** — a tenant's rules and dispatch ledger never leak to another tenant.
- **Recipient de-duplication** — a user matched by two recipient specs is delivered once per event.
- **Unresolved recipient** — an empty resolution logs + skips that recipient only, never redirects,
  and never fails the dispatch.
- **Invalid RecipientsJson** — unknown type, missing/forbidden `refId`, unknown property, over-max
  count all reject; duplicates collapse.
- **Missing/unknown token** — validation warns; render leaves the token visible; no property leakage.
- **Delivery failure isolation** — a throwing delivery/resolution never rolls back or fails the
  transition (the request still advances).
- **Duplicate dispatch protection** — re-dispatching the same `(instance, event, step, rule, user)`
  delivers once; the ledger conflict is a no-op.
- **Seed non-destruction** — reconcile inserts missing system rules, upgrades untouched system
  rules, and leaves tenant-authored + customized rules unchanged.
- **Leave regression** — approver-on-assign and requester-on-decide notifications still fire via the
  seeded rules.

## 14. Implementation slices (commit + push boundaries)

Per standing rule: commit and push each stable, tested slice separately (to `origin` + `sanad`).

1. **Model + migration** — `WorkflowNotificationRule`, `WorkflowNotificationDispatch`, both enums,
   EF configs, indexes, one migration. No behavior. (Build green.)
2. **Recipient JSON + validator + token whitelist + capability registry** — pure, fully unit-tested.
3. **`INotificationRecipientResolver`** — promote/adapt resolvers, list-returning; tenant-isolation
   + per-type tests.
4. **`IWorkflowNotificationDispatcher`** — rule lookup + precedence + render + dedup + idempotency
   ledger + failure isolation; full dispatcher test suite.
5. **`RequestEngine` refactor** — wire the 6 dispatch points, delete hardcoded notifies; regression
   tests.
6. **Seed + provisioning** — `SystemWorkflowNotificationRules`, `ReconcileWorkflowNotificationRules`,
   `SeedVersion` 3→4, non-destructive tests.

Migration apply to Azure + API redeploy is user-gated and batched with the pending SP0
`FormFieldClassificationMetadata` migration before the first SP1 deploy.

## 15. Out-of-scope / future SPs

SLA reminder + escalation (scheduler); "more info requested" status; effect-executed/failed hooks;
request-cancelled dispatch; form-selected-employee recipient + `EmployeeReference` field type;
template CRUD admin UI (8-tab settings). Each is a small addition on this spine.
