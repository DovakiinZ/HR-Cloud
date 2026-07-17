# SP-2 — Email Delivery + Report-Schedule Precision — Design

**Date:** 2026-07-17
**Status:** Approved (design), pending implementation plan
**Owner sign-off:** ammn.com.sa owner (tech@ammn.com.sa)
**Related:** [[reports-engine-r1]], [[dashboard-platform-engine]], `docs/superpowers/specs/2026-07-16-reports-builder-viewer-scheduling-sif-design.md`

## Problem

The app queues email but never sends it. `EmailNotificationQueue` is written by three producers —
`NotificationService` (approvals / bell), `QueueWorkflowEmailSender` (workflow steps), and
`ReportScheduleRunner` (scheduled reports) — but **nothing drains the queue**, so every email is silently
undelivered. Report scheduling therefore *looks* complete (UI + runner + queue rows) but delivers nothing.

Secondary gap: `ReportSchedule.CronExpression` exists but is ignored; `ScheduleMath.ComputeNextRun` only rolls
`NextRunAt` forward by the `Frequency` enum from the last run, so a "daily" report goes out at an arbitrary
time-of-day.

## Goals

1. **Deliver queued email** — a single platform-wide worker drains `EmailNotificationQueue` via Azure
   Communication Services (ACS) Email. Fixes approvals, workflows, AND reports at once.
2. **Attach report files** — scheduled-report emails attach the generated Excel/CSV/PDF (the current
   `/api/files/{id}` link requires auth and is unusable by external recipients).
3. **Schedule precision** — Frequency + a chosen time-of-day (and weekday / day-of-month), anchored to
   Asia/Riyadh, so "daily at 08:00" is reliable. **No cron library** (YAGNI).

## Non-goals

- Full cron-expression parsing (`CronExpression` field stays, unused).
- Custom sender domain / `ammn.com.sa` verification — this is a blank test domain. Use the **Azure-managed
  domain** (`DoNotReply@<guid>.azurecomm.net`) only.
- Per-tenant sender addresses, HTML templating, open/click tracking, exponential backoff — all deferred.

## Decisions (locked with owner)

| Decision | Choice |
|---|---|
| Transport | Azure Communication Services (ACS) Email |
| Sender identity | Azure-managed domain `DoNotReply@<guid>.azurecomm.net` (no custom domain) |
| Scope | Platform-wide drainer (all `EmailNotificationQueue` rows, any Category) |
| Report file delivery | Attach the file to the email |
| Schedule precision | Frequency + time-of-day + day anchor; timezone Asia/Riyadh (+03:00, no DST) |

## Architecture

```
Producers (exist, unchanged)          Queue (exists)               NEW delivery layer
──────────────────────────           ─────────────                ──────────────────
NotificationService  ┐
QueueWorkflowEmailSender ├─enqueue──▶ EmailNotificationQueue ◀─drain── EmailQueueDrainer
ReportScheduleRunner ┘                (Pending/Sent/Failed)                  │ per row
                                                                        IEmailSender
                                                                        ├─ AcsEmailSender  (ACS configured)
                                                                        └─ NullEmailSender (not configured)

EmailDeliveryHostedService (BackgroundService, ~60s tick) ─▶ EmailQueueDrainer.DrainAsync(ct)
```

### Components

**`IEmailSender`** (`HR.Application` interface)
- `Task<EmailSendResult> SendAsync(EmailMessage msg, CancellationToken ct)`
- `EmailMessage { string To, string Subject, string Body, EmailAttachment? Attachment }`
- `EmailAttachment { string FileName, string ContentType, byte[] Content }`
- `EmailSendResult { bool Sent, string? Error }` — never throws for a send failure; transport exceptions are
  caught and mapped to `Sent=false, Error=<message>` so the drainer controls retry/dead-letter.

**`AcsEmailSender : IEmailSender`** (`HR.Infrastructure`)
- Wraps `Azure.Communication.Email.EmailClient` (NuGet `Azure.Communication.Email`).
- Reads `EmailOptions.SenderAddress`; builds `EmailMessage` with plain-text body + optional attachment
  (base64). Calls `SendAsync(WaitUntil.Completed, ...)`; maps success/failure to `EmailSendResult`.
- Registered only when ACS connection string + sender address are present.

**`NullEmailSender : IEmailSender`**
- Returns `Sent=false, Error="Email transport not configured"` and logs once at startup. Bound when ACS
  config is absent so dev/local and un-provisioned environments never crash. (The producers keep enqueuing;
  rows simply stay `Pending` until a transport is configured — no data loss.)
  *Rationale:* mirrors the existing "a sender drains the queue when SMTP is configured" contract in
  `EmailNotificationQueue`/`QueueWorkflowEmailSender` comments.

**`EmailQueueDrainer`** (`HR.Modules.Platform`, scoped service; `IEmailQueueDrainer`)
- `Task<int> DrainAsync(CancellationToken ct)` returns count sent.
- Query: `EmailQueue.IgnoreQueryFilters().Where(e => e.Status == Pending && e.Attempts < MaxAttempts)`
  ordered by `CreatedAt`, `Take(BatchSize)`. Cross-tenant (queue is `TenantEntity`; like `ReportScheduleRunner`).
- Per row: if `AttachmentFileId` set, load `StoredFile` (bytes/name/contentType). If attachment bytes exceed
  `MaxAttachmentBytes`, drop the attachment and append the `Link` to the body (fallback), log a warning.
  Call `IEmailSender.SendAsync`.
  - `Sent=true` → `Status=Sent, SentAt=UtcNow`.
  - `Sent=false` → `Attempts++`, `Error=result.Error`; if `Attempts >= MaxAttempts` → `Status=Failed`.
- `SaveChangesAsync` after the batch. One row failing never aborts the batch (independent updates).
- Constants: `BatchSize=25`, `MaxAttempts=5`, `MaxAttachmentBytes=10*1024*1024`.

**`EmailDeliveryHostedService : BackgroundService`** (`HR.Api`)
- 1-minute startup delay, then `DrainAsync` every 60s (emails feel timely; report runner stays hourly and
  separate). Scoped resolution of `IEmailQueueDrainer`. Mirrors `ReportScheduleHostedService`. Registered in
  `Program.cs`.

### Config

`EmailOptions` bound from configuration:
```
"Email": { "SenderAddress": "DoNotReply@<guid>.azurecomm.net" }
```
ACS connection string read from config key `ConnectionStrings:AcsEmail` (env var
`ConnectionStrings__AcsEmail`; Key Vault `secretpulse` in prod, like the DB). DI: if both the connection
string and `SenderAddress` are non-empty → `AcsEmailSender`, else `NullEmailSender`. `appsettings.json` ships empty placeholders (no
secrets committed), matching the R2 section convention.

## Data model (one additive migration — all nullable, reversible)

`EmailNotificationQueue`:
- `AttachmentFileId Guid?` — FK-less reference to `StoredFile.Id` (same loose-Guid convention used elsewhere).

`ReportSchedule`:
- `TimeOfDayMinutes int?` — minutes past midnight, Riyadh local (e.g. 480 = 08:00). Null → treated as 0 (midnight).
- `DayOfWeek int?` — 0=Sunday..6=Saturday, used by Weekly. Null → anchor to schedule creation weekday.
- `DayOfMonth int?` — 1..28 (clamped), used by Monthly/Quarterly. Null → anchor to day 1.

`ReportScheduleRunner` change: after creating the `StoredFile`, set the enqueued
`EmailNotificationQueue.AttachmentFileId = stored.Id` (keep the existing `Link` in the body as fallback).

## Scheduling — `ScheduleMath.ComputeNextRun` (pure, rewritten)

Signature: `DateTime ComputeNextRun(ReportSchedule s, DateTime fromUtc)`.

Algorithm (all in Asia/Riyadh = `fromUtc + 3h`, no DST, then convert result back to UTC):
- **Daily:** next occurrence of `TimeOfDayMinutes` strictly after `from` local.
- **Weekly:** next `DayOfWeek` at `TimeOfDayMinutes` strictly after `from` local.
- **Monthly:** `DayOfMonth` (clamped to month length) at `TimeOfDayMinutes` in this month if still future, else next month.
- **Quarterly:** same as Monthly but +3 months.

Returns a UTC `DateTime`. "Strictly after" avoids double-firing when a tick lands exactly on the boundary.
Fully unit-tested: boundary at exact time, day-of-week wrap across week end, Feb/30-day month clamping,
quarter rollover, year rollover.

## Provisioning (deploy step — I run it, like the DB)

1. `az communication create` (ACS resource) + `az communication email create` + Azure-managed domain +
   link domain to the ACS resource; grab the connection string + the `DoNotReply@<guid>.azurecomm.net` sender.
2. Store connection string in Key Vault `secretpulse`; set App Service settings
   `ConnectionStrings__AcsEmail` and `Email__SenderAddress`.
3. Live-verify: enqueue a test row (or trigger a due schedule) → confirm `Status` flips to `Sent` and the
   email arrives.

## Frontend

`src/components/reports/schedule-panel.tsx` (or wherever the schedule add-form lives): add a **time picker**
(HH:mm) writing `TimeOfDayMinutes`, plus a **day-of-week** select (Weekly) and **day-of-month** select
(Monthly/Quarterly), shown conditionally on the chosen Frequency. Existing frequency/format/recipients fields
unchanged. `next build` must stay green.

## Testing

- **`ScheduleMath` unit tests** (no DB): each frequency × time-of-day × day anchor, plus the boundary cases above.
- **`EmailQueueDrainer` tests** with a fake `IEmailSender` + in-memory/SQLite DB: success→Sent;
  failure→Attempts++ stays Pending; reaching cap→Failed; attachment loaded from StoredFile; oversized
  attachment→link fallback; batch isolation (one failure doesn't block the rest); `NullEmailSender` leaves
  rows Pending.
- **`AcsEmailSender`** — thin wrapper; covered by manual live-verify during provisioning (no live ACS in CI).
- Backend `dotnet test` green; `next build` green.

## Rollout / increments (for the plan)

- **Increment A — platform delivery:** `IEmailSender` + `AcsEmailSender` + `NullEmailSender` + `EmailOptions`
  + DI + `EmailQueueDrainer` + `EmailDeliveryHostedService` + `AttachmentFileId` column + runner sets it.
  Ships delivery for ALL email. Migration #1.
- **Increment B — schedule precision:** `ReportSchedule` columns + `ScheduleMath` rewrite + `schedule-panel`
  UI. Migration folded into #1 if built together, else #2.

## Risks / edge cases

- **ACS attachment size cap** (~10 MB) — handled via link fallback + log.
- **Un-provisioned environment** — `NullEmailSender` keeps rows `Pending` (no loss); flip transport on later.
- **Timezone** — fixed Asia/Riyadh; if the product later serves multiple regions, promote to a per-tenant
  timezone (out of scope now).
- **Poison row** — capped at `MaxAttempts` then `Failed`; visible for manual inspection. No auto-purge.
- **Backfill** — existing `Pending` rows (approvals/workflows queued before this) will send on first drain;
  acceptable (they were meant to send). If a stale flood is a concern, a one-time `UPDATE ... SET Status=Failed`
  for rows older than N days can be run at deploy — noted, not built.
