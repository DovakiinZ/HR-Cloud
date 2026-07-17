# Email Delivery + Report-Schedule Precision — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make queued email actually deliver via Azure Communication Services (drains `EmailNotificationQueue` for approvals, workflows, AND scheduled reports), attach report files, and give schedules a precise Frequency + time-of-day.

**Architecture:** One `EmailDeliveryHostedService` ticks every 60s and calls `EmailQueueDrainer`, which pulls due queue rows, composes an `OutboundEmail` (attaching the report `StoredFile` when present), sends via an injected `IEmailSender` (`AcsEmailSender` when ACS is configured, else `NullEmailSender`), and applies a pure status/attempt decision. Schedule timing moves from "roll by frequency" to a pure `ScheduleMath.ComputeNextRun` that honors time-of-day + day anchor in Asia/Riyadh.

**Tech Stack:** .NET 8, EF Core (Npgsql), MediatR, xUnit + FluentAssertions, `Azure.Communication.Email` SDK, Next.js 16 (TSX) frontend.

## Global Constraints

- Backend target framework `net8.0`; tests use xUnit `2.9.2` + FluentAssertions `6.12.1`.
- Entities live in `HR.Domain`; abstractions/DTOs in `HR.Application`; DB + external-SDK implementations in `HR.Infrastructure`; feature services in `HR.Modules.Platform`; hosted services in `HR.Api`. Do NOT reintroduce circular deps (see [[backend-build-structure]]).
- Migrations are additive + reversible; do NOT apply to Azure in code — generation only (apply is a deploy step).
- Frontend is **Next.js 16.2.6** (App Router). `next build` must stay green.
- Timezone for schedules is **Asia/Riyadh, fixed +03:00, no DST**. Represent as `TimeSpan.FromHours(3)`; never use `TimeZoneInfo.FindSystemTimeZoneById` (Linux/Windows id mismatch).
- Sender: Azure-managed domain only (`DoNotReply@<guid>.azurecomm.net`); no custom domain.
- Pure helpers carry the logic + unit tests; DB/SDK services stay thin. Follow the existing `ScheduleMath` (pure) + `ReportScheduleRunner` (thin) split.
- Constants: `EmailQueueDrainer` `BatchSize = 25`, `MaxAttempts = 5`, `MaxAttachmentBytes = 10 * 1024 * 1024`.

---

## File map

**Create**
- `backend/src/HR.Application/Engines/Notifications/IEmailSender.cs` — abstraction + `OutboundEmail`/`OutboundAttachment`/`EmailSendResult` + `EmailOptions`.
- `backend/src/HR.Application/Engines/Notifications/EmailComposer.cs` — pure: row (+ optional attachment bytes) → `OutboundEmail`.
- `backend/src/HR.Application/Engines/Notifications/EmailDeliveryDecision.cs` — pure: apply send result to a queue row.
- `backend/src/HR.Infrastructure/Engines/Notifications/AcsEmailSender.cs` — ACS SDK implementation.
- `backend/src/HR.Infrastructure/Engines/Notifications/NullEmailSender.cs` — no-op fallback.
- `backend/src/HR.Modules/Platform/Services/Notifications/IEmailQueueDrainer.cs`
- `backend/src/HR.Modules/Platform/Services/Notifications/EmailQueueDrainer.cs`
- `backend/src/HR.Api/Services/EmailDeliveryHostedService.cs`
- `backend/tests/HR.Modules.Platform.Tests/Notifications/EmailComposerTests.cs`
- `backend/tests/HR.Modules.Platform.Tests/Notifications/EmailDeliveryDecisionTests.cs`
- `backend/tests/HR.Modules.Platform.Tests/Notifications/ScheduleMathTimeOfDayTests.cs`

**Modify**
- `backend/src/HR.Domain/Engines/Notifications/EmailNotificationQueue.cs` — add `AttachmentFileId`.
- `backend/src/HR.Domain/Engines/Reports/ReportSchedule.cs` — add `TimeOfDayMinutes`, `DayOfWeek`, `DayOfMonth`.
- `backend/src/HR.Modules/Platform/Services/Reports/ReportScheduleRunner.cs` — `ScheduleMath.ComputeNextRun` rewrite; runner sets `AttachmentFileId`.
- `backend/src/HR.Modules/Platform/Commands/Reports/ReportCommands.cs` — thread new schedule fields + set initial `NextRunAt`.
- `backend/src/HR.Modules/Platform/DTOs/Reports/ReportDtos.cs` (`ReportScheduleDto`, line ~85) — add new fields.
- `backend/src/HR.Infrastructure/DependencyInjection.cs` — register `IEmailSender` (Acs vs Null) + bind `EmailOptions`.
- `backend/src/HR.Modules/Platform/DependencyInjection.cs` — register `IEmailQueueDrainer`.
- `backend/src/HR.Api/Program.cs` — `AddHostedService<EmailDeliveryHostedService>()`.
- `backend/src/HR.Infrastructure/HR.Infrastructure.csproj` — add `Azure.Communication.Email` package.
- `backend/src/HR.Api/appsettings.json` — empty `Email` + `ConnectionStrings:AcsEmail` placeholders.
- `backend/tests/HR.Modules.Platform.Tests/Reports/ReportScheduleRunnerTests.cs` — update `ScheduleMathTests` to new signature.
- `src/lib/api/reports.ts` — extend `ReportSchedule` interface.
- `src/components/reports/schedule-panel.tsx` — time + day pickers.

---

## Task 1: Email sender abstraction + options (HR.Application)

**Files:**
- Create: `backend/src/HR.Application/Engines/Notifications/IEmailSender.cs`

**Interfaces:**
- Produces: `IEmailSender.SendAsync(OutboundEmail, CancellationToken) : Task<EmailSendResult>`; records `OutboundEmail(string To, string Subject, string Body, OutboundAttachment? Attachment)`, `OutboundAttachment(string FileName, string ContentType, byte[] Content)`, `EmailSendResult(bool Sent, string? Error)`; `EmailOptions { string? SenderAddress }` with const `EmailOptions.SectionName = "Email"`.

- [ ] **Step 1: Create the file (no test — pure declarations)**

```csharp
namespace HR.Application.Engines.Notifications;

/// <summary>An email ready to hand to a transport. Names avoid clashing with the ACS SDK's EmailMessage/EmailAttachment.</summary>
public sealed record OutboundEmail(string To, string Subject, string Body, OutboundAttachment? Attachment = null);

public sealed record OutboundAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Transport outcome. A failed send is (false, error) — never an exception — so callers control retry.</summary>
public sealed record EmailSendResult(bool Sent, string? Error)
{
    public static EmailSendResult Ok() => new(true, null);
    public static EmailSendResult Fail(string error) => new(false, error);
}

/// <summary>Sends one email. Implementations MUST NOT throw for a send failure; map it to EmailSendResult.Fail.</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken ct);
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";
    public string? SenderAddress { get; set; }
}
```

- [ ] **Step 2: Build the project**

Run: `dotnet build backend/src/HR.Application/HR.Application.csproj -v q`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/HR.Application/Engines/Notifications/IEmailSender.cs
git commit -m "feat(email): IEmailSender abstraction + OutboundEmail/EmailOptions"
```

---

## Task 2: EmailComposer — pure row→OutboundEmail with attachment/oversize fallback

**Files:**
- Create: `backend/src/HR.Application/Engines/Notifications/EmailComposer.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/EmailComposerTests.cs`

**Interfaces:**
- Consumes: `OutboundEmail`, `OutboundAttachment` (Task 1); `EmailNotificationQueue` (HR.Domain).
- Produces: `EmailComposer.Compose(EmailNotificationQueue row, byte[]? attachmentBytes, string? attachmentFileName, string? attachmentContentType, int maxAttachmentBytes) : OutboundEmail`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class EmailComposerTests
{
    private static EmailNotificationQueue Row(string? link = null) => new()
        { ToEmail = "a@b.com", Subject = "Report", Body = "Body text", Link = link };

    [Fact]
    public void Compose_without_attachment_passes_body_through()
    {
        var e = EmailComposer.Compose(Row(), null, null, null, 10_000);
        e.To.Should().Be("a@b.com");
        e.Subject.Should().Be("Report");
        e.Body.Should().Be("Body text");
        e.Attachment.Should().BeNull();
    }

    [Fact]
    public void Compose_attaches_when_bytes_within_limit()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var e = EmailComposer.Compose(Row(), bytes, "r.pdf", "application/pdf", 10_000);
        e.Attachment.Should().NotBeNull();
        e.Attachment!.FileName.Should().Be("r.pdf");
        e.Attachment.ContentType.Should().Be("application/pdf");
        e.Attachment.Content.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public void Compose_oversized_attachment_is_dropped_and_link_appended()
    {
        var big = new byte[20];
        var e = EmailComposer.Compose(Row(link: "/api/files/123"), big, "r.pdf", "application/pdf", 10);
        e.Attachment.Should().BeNull();
        e.Body.Should().Contain("/api/files/123");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~EmailComposerTests`
Expected: FAIL — `EmailComposer` does not exist.

- [ ] **Step 3: Implement**

```csharp
using HR.Domain.Engines.Notifications;

namespace HR.Application.Engines.Notifications;

/// <summary>Pure: turns a queued row (+ optionally its attachment bytes) into an OutboundEmail.
/// If the attachment exceeds the transport cap, it is dropped and the row's Link is appended to the body
/// so the recipient still has a way to reach the file.</summary>
public static class EmailComposer
{
    public static OutboundEmail Compose(
        EmailNotificationQueue row, byte[]? attachmentBytes, string? attachmentFileName,
        string? attachmentContentType, int maxAttachmentBytes)
    {
        var body = row.Body;
        OutboundAttachment? attachment = null;

        if (attachmentBytes is { Length: > 0 } && attachmentFileName is not null && attachmentContentType is not null)
        {
            if (attachmentBytes.Length <= maxAttachmentBytes)
                attachment = new OutboundAttachment(attachmentFileName, attachmentContentType, attachmentBytes);
            else if (!string.IsNullOrWhiteSpace(row.Link))
                body = $"{body}\n{row.Link}";
        }

        return new OutboundEmail(row.ToEmail, row.Subject, body, attachment);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~EmailComposerTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Application/Engines/Notifications/EmailComposer.cs backend/tests/HR.Modules.Platform.Tests/Notifications/EmailComposerTests.cs
git commit -m "feat(email): pure EmailComposer with attachment + oversize link fallback"
```

---

## Task 3: EmailDeliveryDecision — pure status/attempt transition

**Files:**
- Create: `backend/src/HR.Application/Engines/Notifications/EmailDeliveryDecision.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Notifications/EmailDeliveryDecisionTests.cs`

**Interfaces:**
- Consumes: `EmailSendResult` (Task 1); `EmailNotificationQueue`, `EmailQueueStatus` (HR.Domain).
- Produces: `EmailDeliveryDecision.Apply(EmailNotificationQueue row, EmailSendResult result, int maxAttempts, DateTime nowUtc) : void` (mutates row in place).

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class EmailDeliveryDecisionTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private static EmailNotificationQueue Row(int attempts = 0) => new()
        { ToEmail = "a@b.com", Subject = "s", Body = "b", Attempts = attempts, Status = EmailQueueStatus.Pending };

    [Fact]
    public void Success_marks_Sent_and_stamps_time()
    {
        var row = Row();
        EmailDeliveryDecision.Apply(row, EmailSendResult.Ok(), maxAttempts: 5, Now);
        row.Status.Should().Be(EmailQueueStatus.Sent);
        row.SentAt.Should().Be(Now);
        row.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_below_cap_increments_and_stays_Pending()
    {
        var row = Row(attempts: 1);
        EmailDeliveryDecision.Apply(row, EmailSendResult.Fail("boom"), maxAttempts: 5, Now);
        row.Status.Should().Be(EmailQueueStatus.Pending);
        row.Attempts.Should().Be(2);
        row.Error.Should().Be("boom");
        row.SentAt.Should().BeNull();
    }

    [Fact]
    public void Failure_reaching_cap_marks_Failed()
    {
        var row = Row(attempts: 4);
        EmailDeliveryDecision.Apply(row, EmailSendResult.Fail("boom"), maxAttempts: 5, Now);
        row.Attempts.Should().Be(5);
        row.Status.Should().Be(EmailQueueStatus.Failed);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~EmailDeliveryDecisionTests`
Expected: FAIL — `EmailDeliveryDecision` does not exist.

- [ ] **Step 3: Implement**

```csharp
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;

namespace HR.Application.Engines.Notifications;

/// <summary>Pure: applies a send result to a queue row (Sent, or Attempts++ → Pending/Failed at cap).</summary>
public static class EmailDeliveryDecision
{
    public static void Apply(EmailNotificationQueue row, EmailSendResult result, int maxAttempts, DateTime nowUtc)
    {
        if (result.Sent)
        {
            row.Status = EmailQueueStatus.Sent;
            row.SentAt = nowUtc;
            row.Error = null;
            return;
        }

        row.Attempts += 1;
        row.Error = result.Error;
        row.Status = row.Attempts >= maxAttempts ? EmailQueueStatus.Failed : EmailQueueStatus.Pending;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~EmailDeliveryDecisionTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Application/Engines/Notifications/EmailDeliveryDecision.cs backend/tests/HR.Modules.Platform.Tests/Notifications/EmailDeliveryDecisionTests.cs
git commit -m "feat(email): pure EmailDeliveryDecision status/attempt transitions"
```

---

## Task 4: EmailNotificationQueue.AttachmentFileId

**Files:**
- Modify: `backend/src/HR.Domain/Engines/Notifications/EmailNotificationQueue.cs`

**Interfaces:**
- Produces: `EmailNotificationQueue.AttachmentFileId` (`Guid?`).

- [ ] **Step 1: Add the property**

In `EmailNotificationQueue`, after `public int Attempts { get; set; }` add:

```csharp
    /// <summary>Optional StoredFile.Id to attach when delivering (e.g. a scheduled report file). FK-less, like other Guid refs.</summary>
    public Guid? AttachmentFileId { get; set; }
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/HR.Domain/HR.Domain.csproj -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/HR.Domain/Engines/Notifications/EmailNotificationQueue.cs
git commit -m "feat(email): EmailNotificationQueue.AttachmentFileId column (entity)"
```

---

## Task 5: EmailQueueDrainer (thin DB orchestrator)

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Notifications/IEmailQueueDrainer.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Notifications/EmailQueueDrainer.cs`

**Interfaces:**
- Consumes: `IEmailSender`, `EmailComposer`, `EmailDeliveryDecision`, `OutboundEmail` (Tasks 1–3); `ApplicationDbContext.EmailQueue`/`.Files`; `EmailNotificationQueue.AttachmentFileId` (Task 4).
- Produces: `IEmailQueueDrainer.DrainAsync(CancellationToken) : Task<int>`.

- [ ] **Step 1: Create the interface**

```csharp
namespace HR.Modules.Platform.Services.Notifications;

public interface IEmailQueueDrainer
{
    /// <summary>Sends up to a batch of due queued emails. Returns the number successfully sent.</summary>
    Task<int> DrainAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Implement the drainer**

```csharp
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>Drains EmailNotificationQueue: pull due rows across tenants, compose (attach StoredFile if any),
/// send via IEmailSender, apply the pure delivery decision, persist. One row failing never blocks the batch.</summary>
public sealed class EmailQueueDrainer : IEmailQueueDrainer
{
    private const int BatchSize = 25;
    private const int MaxAttempts = 5;
    private const int MaxAttachmentBytes = 10 * 1024 * 1024;

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailQueueDrainer> _logger;

    public EmailQueueDrainer(ApplicationDbContext db, IEmailSender sender, ILogger<EmailQueueDrainer> logger)
    { _db = db; _sender = sender; _logger = logger; }

    public async Task<int> DrainAsync(CancellationToken ct)
    {
        // EmailQueue is a TenantEntity (global filter) — IgnoreQueryFilters to drain every tenant.
        var batch = await _db.EmailQueue.IgnoreQueryFilters()
            .Where(e => e.Status == EmailQueueStatus.Pending && e.Attempts < MaxAttempts)
            .OrderBy(e => e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var row in batch)
        {
            var now = DateTime.UtcNow;
            try
            {
                byte[]? bytes = null; string? name = null; string? contentType = null;
                if (row.AttachmentFileId is { } fileId)
                {
                    var file = await _db.Files.IgnoreQueryFilters()
                        .Where(f => f.Id == fileId)
                        .Select(f => new { f.Data, f.FileName, f.ContentType })
                        .FirstOrDefaultAsync(ct);
                    if (file is not null) { bytes = file.Data; name = file.FileName; contentType = file.ContentType; }
                }

                var email = EmailComposer.Compose(row, bytes, name, contentType, MaxAttachmentBytes);
                var result = await _sender.SendAsync(email, ct);
                EmailDeliveryDecision.Apply(row, result, MaxAttempts, now);
                if (result.Sent) sent++;
            }
            catch (Exception ex)
            {
                // Defensive: senders shouldn't throw, but never let one row abort the batch.
                _logger.LogError(ex, "Email {EmailId} threw during send.", row.Id);
                EmailDeliveryDecision.Apply(row, EmailSendResult.Fail(ex.Message), MaxAttempts, now);
            }
        }

        if (batch.Count > 0) await _db.SaveChangesAsync(ct);
        return sent;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build backend/src/HR.Modules/Platform/HR.Modules.Platform.csproj -v q`
Expected: Build succeeded. (If `CreatedAt` is not found on `EmailNotificationQueue`, it inherits from `TenantEntity`/`AuditableEntity`; confirm the base exposes `CreatedAt` — it does, used by other queries.)

- [ ] **Step 4: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Notifications/IEmailQueueDrainer.cs backend/src/HR.Modules/Platform/Services/Notifications/EmailQueueDrainer.cs
git commit -m "feat(email): EmailQueueDrainer (compose->send->decide->persist)"
```

---

## Task 6: NullEmailSender + AcsEmailSender + DI + config (HR.Infrastructure)

**Files:**
- Create: `backend/src/HR.Infrastructure/Engines/Notifications/NullEmailSender.cs`
- Create: `backend/src/HR.Infrastructure/Engines/Notifications/AcsEmailSender.cs`
- Modify: `backend/src/HR.Infrastructure/HR.Infrastructure.csproj`
- Modify: `backend/src/HR.Infrastructure/DependencyInjection.cs`
- Modify: `backend/src/HR.Api/appsettings.json`

**Interfaces:**
- Consumes: `IEmailSender`, `OutboundEmail`, `EmailSendResult`, `EmailOptions` (Task 1).
- Produces: DI binding of `IEmailSender` (Acs when configured, else Null).

- [ ] **Step 1: Add the ACS package to `HR.Infrastructure.csproj`**

Inside the main `<ItemGroup>` of PackageReferences add:

```xml
    <PackageReference Include="Azure.Communication.Email" Version="1.0.1" />
```

Run: `dotnet restore backend/src/HR.Infrastructure/HR.Infrastructure.csproj`
Expected: Restore succeeded.

- [ ] **Step 2: NullEmailSender**

```csharp
using HR.Application.Engines.Notifications;
using Microsoft.Extensions.Logging;

namespace HR.Infrastructure.Engines.Notifications;

/// <summary>Bound when ACS is not configured: leaves rows Pending (drainer will retry once a transport exists).</summary>
public sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;
    public NullEmailSender(ILogger<NullEmailSender> logger) => _logger = logger;

    public Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken ct)
    {
        _logger.LogWarning("Email transport not configured; leaving {To} pending.", email.To);
        return Task.FromResult(EmailSendResult.Fail("Email transport not configured"));
    }
}
```

- [ ] **Step 3: AcsEmailSender**

```csharp
using Azure;
using Azure.Communication.Email;
using HR.Application.Engines.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Engines.Notifications;

/// <summary>Sends via Azure Communication Services Email. Never throws for a send failure.</summary>
public sealed class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly string _sender;
    private readonly ILogger<AcsEmailSender> _logger;

    public AcsEmailSender(string connectionString, IOptions<EmailOptions> options, ILogger<AcsEmailSender> logger)
    {
        _client = new EmailClient(connectionString);
        _sender = options.Value.SenderAddress ?? throw new InvalidOperationException("Email:SenderAddress is required for ACS.");
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken ct)
    {
        try
        {
            var message = new Azure.Communication.Email.EmailMessage(
                senderAddress: _sender,
                recipientAddress: email.To,
                content: new EmailContent(email.Subject) { PlainText = email.Body });

            if (email.Attachment is { } a)
                message.Attachments.Add(new Azure.Communication.Email.EmailAttachment(
                    a.FileName, a.ContentType, new BinaryData(a.Content)));

            var op = await _client.SendAsync(WaitUntil.Completed, message, ct);
            return op.HasCompleted && op.Value.Status == EmailSendStatus.Succeeded
                ? EmailSendResult.Ok()
                : EmailSendResult.Fail($"ACS status: {op.Value.Status}");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "ACS send failed for {To}.", email.To);
            return EmailSendResult.Fail(ex.Message);
        }
    }
}
```

- [ ] **Step 4: Register in `HR.Infrastructure/DependencyInjection.cs`**

Find `AddInfrastructure(this IServiceCollection services, IConfiguration configuration)` and add before `return services;`:

```csharp
        // Email transport: ACS when configured, else a no-op that leaves rows Pending.
        services.Configure<HR.Application.Engines.Notifications.EmailOptions>(
            configuration.GetSection(HR.Application.Engines.Notifications.EmailOptions.SectionName));
        var acsConn = configuration.GetConnectionString("AcsEmail");
        var senderAddr = configuration[$"{HR.Application.Engines.Notifications.EmailOptions.SectionName}:SenderAddress"];
        if (!string.IsNullOrWhiteSpace(acsConn) && !string.IsNullOrWhiteSpace(senderAddr))
            services.AddSingleton<HR.Application.Engines.Notifications.IEmailSender>(sp =>
                new HR.Infrastructure.Engines.Notifications.AcsEmailSender(
                    acsConn,
                    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<HR.Application.Engines.Notifications.EmailOptions>>(),
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<HR.Infrastructure.Engines.Notifications.AcsEmailSender>>()));
        else
            services.AddSingleton<HR.Application.Engines.Notifications.IEmailSender,
                HR.Infrastructure.Engines.Notifications.NullEmailSender>();
```

> If `AddInfrastructure` does not currently receive `IConfiguration`, it does — the DB registration uses `configuration.GetConnectionString("DefaultConnection")`. Reuse that same `configuration` parameter.

- [ ] **Step 5: appsettings placeholders**

In `backend/src/HR.Api/appsettings.json`, add `"AcsEmail": ""` to the `ConnectionStrings` object, and add a top-level section:

```json
  "Email": {
    "SenderAddress": ""
  },
```

- [ ] **Step 6: Build**

Run: `dotnet build backend/src/HR.Infrastructure/HR.Infrastructure.csproj -v q`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add backend/src/HR.Infrastructure/Engines/Notifications/ backend/src/HR.Infrastructure/HR.Infrastructure.csproj backend/src/HR.Infrastructure/DependencyInjection.cs backend/src/HR.Api/appsettings.json
git commit -m "feat(email): ACS + Null IEmailSender with config-driven DI binding"
```

---

## Task 7: Register drainer + hosted service

**Files:**
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection.cs`
- Create: `backend/src/HR.Api/Services/EmailDeliveryHostedService.cs`
- Modify: `backend/src/HR.Api/Program.cs`

**Interfaces:**
- Consumes: `IEmailQueueDrainer`/`EmailQueueDrainer` (Task 5).

- [ ] **Step 1: Register the drainer** in `HR.Modules/Platform/DependencyInjection.cs`, after the notification-engine block (near line 79):

```csharp
        services.AddScoped<HR.Modules.Platform.Services.Notifications.IEmailQueueDrainer,
            HR.Modules.Platform.Services.Notifications.EmailQueueDrainer>();
```

- [ ] **Step 2: Hosted service**

```csharp
using HR.Modules.Platform.Services.Notifications;

namespace HR.Api.Services;

/// <summary>Drains the email queue every 60s (emails should feel timely). Mirrors ReportScheduleHostedService.</summary>
public sealed class EmailDeliveryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDeliveryHostedService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public EmailDeliveryHostedService(IServiceScopeFactory scopeFactory, ILogger<EmailDeliveryHostedService> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var drainer = scope.ServiceProvider.GetRequiredService<IEmailQueueDrainer>();
                var count = await drainer.DrainAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Email delivery sent {Count} message(s).", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Email delivery tick failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
```

- [ ] **Step 3: Register in `Program.cs`** — after `builder.Services.AddHostedService<HR.Api.Services.ReportScheduleHostedService>();` (line ~84):

```csharp
builder.Services.AddHostedService<HR.Api.Services.EmailDeliveryHostedService>();
```

- [ ] **Step 4: Build the API**

Run: `dotnet build backend/src/HR.Api/HR.Api.csproj -v q`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/DependencyInjection.cs backend/src/HR.Api/Services/EmailDeliveryHostedService.cs backend/src/HR.Api/Program.cs
git commit -m "feat(email): register drainer + 60s EmailDeliveryHostedService"
```

---

## Task 8: ReportSchedule time-of-day columns

**Files:**
- Modify: `backend/src/HR.Domain/Engines/Reports/ReportSchedule.cs`

**Interfaces:**
- Produces: `ReportSchedule.TimeOfDayMinutes` (`int?`), `.DayOfWeek` (`int?`), `.DayOfMonth` (`int?`).

- [ ] **Step 1: Add properties** after `public string? CronExpression { get; set; }`:

```csharp
    /// <summary>Minutes past midnight (Asia/Riyadh) to send. Null = 0 (00:00).</summary>
    public int? TimeOfDayMinutes { get; set; }
    /// <summary>0=Sunday..6=Saturday for Weekly. Null = anchor to creation weekday at compute time.</summary>
    public int? DayOfWeek { get; set; }
    /// <summary>1..28 (clamped) for Monthly/Quarterly. Null = day 1.</summary>
    public int? DayOfMonth { get; set; }
```

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/HR.Domain/HR.Domain.csproj -v q`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/HR.Domain/Engines/Reports/ReportSchedule.cs
git commit -m "feat(reports): ReportSchedule time-of-day + day-anchor columns (entity)"
```

---

## Task 9: ScheduleMath.ComputeNextRun rewrite + command/DTO threading

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Services/Reports/ReportScheduleRunner.cs` (the `ScheduleMath` class + runner call site)
- Modify: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportScheduleRunnerTests.cs` (existing `ScheduleMathTests`)
- Create: `backend/tests/HR.Modules.Platform.Tests/Notifications/ScheduleMathTimeOfDayTests.cs`
- Modify: `backend/src/HR.Modules/Platform/Commands/Reports/ReportCommands.cs`
- Modify: `backend/src/HR.Modules/Platform/DTOs/Reports/ReportDtos.cs` (`ReportScheduleDto`, line ~85)

**Interfaces:**
- Consumes: `ReportSchedule` (Task 8).
- Produces: `ScheduleMath.ComputeNextRun(ReportSchedule schedule, DateTime fromUtc) : DateTime` (UTC). **Signature change** — old `(ReportScheduleFrequency, DateTime)` overload is removed.

- [ ] **Step 1: Update the existing `ScheduleMathTests`** in `ReportScheduleRunnerTests.cs` — replace the `ComputeNextRun_*` tests (keep the `ParseEmails_*` tests unchanged) with schedule-based calls:

```csharp
    private static ReportSchedule Sched(ReportScheduleFrequency f, int? tod = null, int? dow = null, int? dom = null)
        => new() { Frequency = f, TimeOfDayMinutes = tod, DayOfWeek = dow, DayOfMonth = dom };

    // Base is 2026-07-16 08:00 UTC = 11:00 Riyadh.
    [Fact]
    public void Daily_next_is_tomorrow_at_configured_local_time()
    {
        // 06:00 Riyadh = 03:00 UTC. From 11:00 Riyadh, next 06:00 is tomorrow 03:00 UTC.
        var next = ScheduleMath.ComputeNextRun(Sched(ReportScheduleFrequency.Daily, tod: 360), Base);
        next.Should().Be(new DateTime(2026, 7, 17, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Daily_later_today_local_stays_today()
    {
        // 20:00 Riyadh = 17:00 UTC, still ahead of 11:00 Riyadh now.
        var next = ScheduleMath.ComputeNextRun(Sched(ReportScheduleFrequency.Daily, tod: 1200), Base);
        next.Should().Be(new DateTime(2026, 7, 16, 17, 0, 0, DateTimeKind.Utc));
    }
```

- [ ] **Step 2: Write the dedicated boundary test file** `ScheduleMathTimeOfDayTests.cs`:

```csharp
using System;
using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class ScheduleMathTimeOfDayTests
{
    // 2026-07-16 is a Thursday. 05:00 UTC = 08:00 Riyadh.
    private static readonly DateTime From = new(2026, 7, 16, 5, 0, 0, DateTimeKind.Utc);
    private static ReportSchedule S(ReportScheduleFrequency f, int? tod = null, int? dow = null, int? dom = null)
        => new() { Frequency = f, TimeOfDayMinutes = tod, DayOfWeek = dow, DayOfMonth = dom };

    [Fact]
    public void Weekly_picks_next_named_weekday_at_time()
    {
        // Want Sunday(0) 09:00 Riyadh = 06:00 UTC. From Thu 08:00 Riyadh → Sun 2026-07-19 06:00 UTC.
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Weekly, tod: 540, dow: 0), From);
        next.Should().Be(new DateTime(2026, 7, 19, 6, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Monthly_this_month_if_day_still_future()
    {
        // Day 25 at 08:00 Riyadh (05:00 UTC), from the 16th → same month.
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Monthly, tod: 480, dom: 25), From);
        next.Should().Be(new DateTime(2026, 7, 25, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Monthly_rolls_to_next_month_when_day_passed()
    {
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Monthly, tod: 480, dom: 5), From);
        next.Should().Be(new DateTime(2026, 8, 5, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void DayOfMonth_clamped_to_short_month()
    {
        // Day 31 requested; from Feb should clamp to 28 (2027 not leap). Use a Feb "from".
        var feb = new DateTime(2027, 2, 3, 5, 0, 0, DateTimeKind.Utc);
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Monthly, tod: 480, dom: 31), feb);
        next.Should().Be(new DateTime(2027, 2, 28, 5, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Quarterly_adds_three_months()
    {
        var next = ScheduleMath.ComputeNextRun(S(ReportScheduleFrequency.Quarterly, tod: 480, dom: 5), From);
        next.Should().Be(new DateTime(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc));
    }
}
```

- [ ] **Step 3: Run both to verify they fail**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj --filter FullyQualifiedName~ScheduleMath`
Expected: FAIL — new `ComputeNextRun(ReportSchedule, DateTime)` overload does not exist / old signature mismatch.

- [ ] **Step 4: Rewrite `ScheduleMath.ComputeNextRun`** in `ReportScheduleRunner.cs`. Replace the existing `ComputeNextRun(ReportScheduleFrequency, DateTime)` method with:

```csharp
    private static readonly TimeSpan Riyadh = TimeSpan.FromHours(3); // +03:00, no DST

    /// <summary>Next UTC run for a schedule, honoring time-of-day + day anchor in Asia/Riyadh.</summary>
    public static DateTime ComputeNextRun(ReportSchedule s, DateTime fromUtc)
    {
        var local = fromUtc + Riyadh;                     // treat as Riyadh wall-clock
        var minutes = Math.Clamp(s.TimeOfDayMinutes ?? 0, 0, 24 * 60 - 1);
        var timeOfDay = TimeSpan.FromMinutes(minutes);

        DateTime nextLocal = s.Frequency switch
        {
            ReportScheduleFrequency.Weekly    => NextWeekly(local, s.DayOfWeek ?? (int)local.DayOfWeek, timeOfDay),
            ReportScheduleFrequency.Monthly   => NextMonthly(local, s.DayOfMonth ?? 1, timeOfDay, monthStep: 1),
            ReportScheduleFrequency.Quarterly => NextMonthly(local, s.DayOfMonth ?? 1, timeOfDay, monthStep: 3),
            _                                 => NextDaily(local, timeOfDay),
        };
        return nextLocal - Riyadh;                        // back to UTC
    }

    private static DateTime NextDaily(DateTime local, TimeSpan tod)
    {
        var candidate = local.Date + tod;
        return candidate > local ? candidate : candidate.AddDays(1);
    }

    private static DateTime NextWeekly(DateTime local, int targetDow, TimeSpan tod)
    {
        int delta = ((targetDow - (int)local.DayOfWeek) % 7 + 7) % 7;
        var candidate = local.Date.AddDays(delta) + tod;
        return candidate > local ? candidate : candidate.AddDays(7);
    }

    private static DateTime NextMonthly(DateTime local, int targetDom, TimeSpan tod, int monthStep)
    {
        DateTime Build(int year, int month)
        {
            var day = Math.Clamp(targetDom, 1, DateTime.DaysInMonth(year, month));
            return new DateTime(year, month, day) + tod;
        }
        var thisMonth = Build(local.Year, local.Month);
        if (thisMonth > local) return thisMonth;
        var rolled = new DateTime(local.Year, local.Month, 1).AddMonths(monthStep);
        return Build(rolled.Year, rolled.Month);
    }
```

Add `using HR.Domain.Engines.Reports;` if not present (it is — the file already references `ReportSchedule`).

- [ ] **Step 5: Update the runner call site** in `RunDueAsync` — change:

```csharp
                    schedule.NextRunAt = ScheduleMath.ComputeNextRun(schedule.Frequency, now);
```

to:

```csharp
                    schedule.NextRunAt = ScheduleMath.ComputeNextRun(schedule, now);
```

- [ ] **Step 6: Set `AttachmentFileId` when enqueuing** (folds in the runner half of Task 4). In `RunDueAsync`, inside the `foreach (var email in ScheduleMath.ParseEmails(...))` loop, add `AttachmentFileId = stored.Id,` to the `EmailNotificationQueue` initializer (after `Link = link,`).

- [ ] **Step 7: Thread new fields through the command** in `ReportCommands.cs`:
  - Add to `AddReportScheduleCommand`: `public int? TimeOfDayMinutes { get; init; }`, `public int? DayOfWeek { get; init; }`, `public int? DayOfMonth { get; init; }`.
  - In `AddReportScheduleCommandHandler.Handle`, set those on the new `ReportSchedule`, and set the initial next-run so it fires at the right time instead of immediately:

```csharp
        var entity = new ReportSchedule
        {
            ReportDefinitionId = request.ReportDefinitionId,
            Frequency = request.Frequency,
            CronExpression = request.CronExpression,
            ExportFormat = request.ExportFormat,
            Recipients = request.Recipients,
            TimeOfDayMinutes = request.TimeOfDayMinutes,
            DayOfWeek = request.DayOfWeek,
            DayOfMonth = request.DayOfMonth,
            IsActive = true,
        };
        entity.NextRunAt = HR.Modules.Platform.Services.Reports.ScheduleMath.ComputeNextRun(entity, DateTime.UtcNow);
        _context.Set<ReportSchedule>().Add(entity); await _context.SaveChangesAsync(ct);
```

- [ ] **Step 8: Expose fields on `ReportScheduleDto`** — in `backend/src/HR.Modules/Platform/DTOs/Reports/ReportDtos.cs` (`class ReportScheduleDto`, ~line 85) add `public int? TimeOfDayMinutes { get; set; }`, `public int? DayOfWeek { get; set; }`, `public int? DayOfMonth { get; set; }`. AutoMapper maps by name (existing `CreateMap<ReportSchedule, ReportScheduleDto>()` at `PlatformMappingProfile.cs:126`), so no profile change is needed.

- [ ] **Step 9: Run the full Platform test project**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: PASS — all `ScheduleMath*`, `EmailComposer`, `EmailDeliveryDecision` tests green; previously-passing tests unaffected; DB-gated `[SkippableFact]` remain skipped.

- [ ] **Step 10: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportScheduleRunner.cs backend/tests/HR.Modules.Platform.Tests/ backend/src/HR.Modules/Platform/Commands/Reports/ReportCommands.cs backend/src/HR.Application
git commit -m "feat(reports): schedule time-of-day compute (Riyadh) + attach report file + initial NextRunAt"
```

---

## Task 10: Single EF migration (AttachmentFileId + schedule columns)

**Files:**
- Create: migration under `backend/src/HR.Infrastructure/Migrations/` (generated).

- [ ] **Step 1: Generate the migration** (design-time build; no DB connection needed):

Run:
```bash
dotnet ef migrations add EmailDeliveryAndScheduleTiming \
  --project backend/src/HR.Infrastructure/HR.Infrastructure.csproj \
  --startup-project backend/src/HR.Api/HR.Api.csproj
```
Expected: creates `<timestamp>_EmailDeliveryAndScheduleTiming.cs` adding nullable columns `EmailNotificationQueue.AttachmentFileId`, `ReportSchedules.TimeOfDayMinutes`, `ReportSchedules.DayOfWeek`, `ReportSchedules.DayOfMonth`.

- [ ] **Step 2: Inspect** the generated `Up`/`Down` — confirm ALL four columns are `nullable: true` and `Down` drops exactly them. No data changes, no other tables touched.

- [ ] **Step 3: Build the API** (verifies the model snapshot compiles)

Run: `dotnet build backend/src/HR.Api/HR.Api.csproj -v q`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/HR.Infrastructure/Migrations/
git commit -m "feat(email): migration EmailDeliveryAndScheduleTiming (additive nullable columns)"
```

> Do NOT apply to Azure here. Application is a deploy step (Task 13 / provisioning).

---

## Task 11: Frontend — schedule time + day pickers

**Files:**
- Modify: `src/lib/api/reports.ts` (the `ReportSchedule` interface, lines ~587–591)
- Modify: `src/components/reports/schedule-panel.tsx`

**Interfaces:**
- Consumes: `addSchedule(id, body)` (already sends an arbitrary body object).

- [ ] **Step 1: Extend the `ReportSchedule` interface** in `reports.ts`:

```ts
export interface ReportSchedule {
  id: string; frequency: string; cronExpression?: string | null;
  exportFormat: string; recipients: string; isActive: boolean;
  timeOfDayMinutes?: number | null; dayOfWeek?: number | null; dayOfMonth?: number | null;
  lastRunAt?: string | null; nextRunAt?: string | null;
}
```

- [ ] **Step 2: Add time + day state and inputs** in `schedule-panel.tsx`. After the existing `freq/fmt/emails` state add:

```tsx
  const [time, setTime] = useState("08:00");        // HH:mm, Riyadh
  const [dow, setDow] = useState(0);                // 0=Sunday
  const [dom, setDom] = useState(1);                // 1..28
```

Add day-name constants near `FREQ`:

```tsx
const DOW = [{ v: 0, l: "الأحد" }, { v: 1, l: "الاثنين" }, { v: 2, l: "الثلاثاء" }, { v: 3, l: "الأربعاء" }, { v: 4, l: "الخميس" }, { v: 5, l: "الجمعة" }, { v: 6, l: "السبت" }];
```

- [ ] **Step 3: Send the new fields** — replace the `addSchedule` call in `add()`:

```tsx
      const [hh, mm] = time.split(":").map(Number);
      const timeOfDayMinutes = (hh || 0) * 60 + (mm || 0);
      await addSchedule(reportId, {
        frequency: freq, exportFormat: fmt, recipients: JSON.stringify(list),
        timeOfDayMinutes,
        dayOfWeek: freq === 2 ? dow : null,
        dayOfMonth: freq === 3 || freq === 4 ? Math.min(Math.max(dom, 1), 28) : null,
      });
```

- [ ] **Step 4: Render the controls** — in the input row, before the email `<input>`, add a time picker (always) and conditional day picker:

```tsx
        <input
          type="time"
          value={time}
          onChange={(e) => setTime(e.target.value)}
          className="h-9 border border-border bg-background px-2 text-sm"
          title="وقت الإرسال (توقيت الرياض)"
        />
        {freq === 2 && (
          <select value={dow} onChange={(e) => setDow(Number(e.target.value))}
            className="h-9 border border-border bg-background px-2 text-sm">
            {DOW.map((d) => <option key={d.v} value={d.v}>{d.l}</option>)}
          </select>
        )}
        {(freq === 3 || freq === 4) && (
          <select value={dom} onChange={(e) => setDom(Number(e.target.value))}
            className="h-9 border border-border bg-background px-2 text-sm" title="يوم الشهر">
            {Array.from({ length: 28 }, (_, i) => i + 1).map((d) => <option key={d} value={d}>{d}</option>)}
          </select>
        )}
```

- [ ] **Step 5: Build the frontend**

Run: `npx next build`
Expected: Compiled successfully, 0 type errors.

- [ ] **Step 6: Commit**

```bash
git add src/lib/api/reports.ts src/components/reports/schedule-panel.tsx
git commit -m "feat(reports): schedule panel time-of-day + day-of-week/month pickers"
```

---

## Task 12: Full backend build + test gate

- [ ] **Step 1: Build the whole backend solution**

Run: `dotnet build backend -v q` (or the `.sln` path)
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the full Platform test suite**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests/HR.Modules.Platform.Tests.csproj`
Expected: All non-skipped tests PASS (includes the 6 new pure tests). Skipped count unchanged.

- [ ] **Step 3: Commit (if any incidental fixes were needed)**

```bash
git add -A && git commit -m "chore(email): full build + test green" || echo "nothing to commit"
```

---

## Task 13: Provisioning + deploy (operator step — not code)

> Runs after merge. Requires Azure CLI auth.

- [ ] **Step 1:** Provision ACS + Email + Azure-managed domain, link domain, capture connection string + `DoNotReply@<guid>.azurecomm.net` sender (via `az communication` + `az communication email` commands).
- [ ] **Step 2:** Store the connection string in Key Vault `secretpulse`; set App Service `hrcloud-api-v4xd` settings `ConnectionStrings__AcsEmail` and `Email__SenderAddress`.
- [ ] **Step 3:** Apply the migration to Azure Postgres (`dotnet ef database update` with the Azure connection string; add dev IP to the Flexible-Server firewall first — see [[reports-engine-r1]]).
- [ ] **Step 4:** Publish → zip (forward-slash entries) → `az webapp deploy --type zip` (see [[dashboard-platform-engine]] deploy recipe).
- [ ] **Step 5:** Live-verify: create a schedule due within the hour (or enqueue a test approval) → confirm the queue row flips to `Sent` and the email arrives.

---

## Self-Review

**Spec coverage:**
- Deliver queued email → Tasks 1,3,5,6,7 (sender + drainer + hosted service). ✅
- Attach report files → Tasks 2,4 (`AttachmentFileId`), runner sets it (Task 9 Step 6), composer attaches (Task 2). ✅
- Schedule precision (Frequency + time-of-day, Riyadh) → Tasks 8,9. ✅
- Platform-wide scope (all producers) → drainer queries the whole queue regardless of Category (Task 5). ✅
- Azure-managed sender only, config-driven, Null fallback → Task 6. ✅
- Retry defaults (60s tick, 5-attempt cap, batch 25, 10MB) → constants in Tasks 5,7; decision in Task 3. ✅
- Oversized attachment → link fallback → Task 2. ✅
- Migration additive/nullable, generation-only → Task 10. ✅
- FE time/day pickers → Task 11. ✅
- Provisioning/deploy → Task 13. ✅

**Placeholder scan:** No TBD/TODO; every code step shows full code. The only lookup is `ReportScheduleDto`'s file (Task 9 Step 8) with the exact `grep` to find it and the exact properties to add. ✅

**Type consistency:** `ComputeNextRun(ReportSchedule, DateTime)` used identically in runner (Task 9 Step 5), command (Step 7), and all tests. `IEmailSender.SendAsync(OutboundEmail, ct)` consistent across Acs/Null/drainer. `EmailDeliveryDecision.Apply(row, result, maxAttempts, nowUtc)` and `EmailComposer.Compose(row, bytes, name, contentType, max)` match their call sites in the drainer. `AttachmentFileId` (`Guid?`) consistent entity↔drainer↔runner. ✅
