using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Files;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

/// <summary>Integration tests for EmailQueueDrainer using EF Core InMemory.</summary>
public class EmailQueueDrainerTests
{
    // ── shared test tenant ──────────────────────────────────────────────────
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId                           => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public Guid TenantId                         => EmailQueueDrainerTests.TenantId;
        public string? Email                         => "test@local";
        public IReadOnlyList<string> Permissions     { get; } = Array.Empty<string>();
        public bool IsAuthenticated                  => true;
    }

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        new FakeUser());

    // ── configurable fake sender ─────────────────────────────────────────────
    private sealed class FakeSender : IEmailSender
    {
        private readonly bool _canSend;
        private readonly Func<OutboundEmail, EmailSendResult>? _perCall;

        public FakeSender(bool canSend = true, Func<OutboundEmail, EmailSendResult>? perCall = null)
        {
            _canSend = canSend;
            _perCall = perCall;
        }

        public bool CanSend => _canSend;

        public List<OutboundEmail> Received { get; } = new();

        public Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken ct)
        {
            Received.Add(email);
            var result = _perCall?.Invoke(email) ?? EmailSendResult.Ok();
            return Task.FromResult(result);
        }
    }

    // A sender that throws synchronously (covers the defensive catch in the drainer).
    private sealed class ThrowingSender : IEmailSender
    {
        public bool CanSend => true;
        public int CallCount { get; private set; }

        public Task<EmailSendResult> SendAsync(OutboundEmail email, CancellationToken ct)
        {
            CallCount++;
            throw new InvalidOperationException("transport blew up");
        }
    }

    // ── helper to seed a minimal Pending row ─────────────────────────────────
    private static EmailNotificationQueue PendingRow(int attempts = 0, Guid? attachmentFileId = null) => new()
    {
        TenantId         = TenantId,
        ToEmail          = "dest@example.com",
        Subject          = "Test",
        Body             = "Hello",
        Status           = EmailQueueStatus.Pending,
        Attempts         = attempts,
        AttachmentFileId = attachmentFileId,
    };

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Success_marks_row_Sent_and_returns_1()
    {
        await using var db = Ctx(nameof(Success_marks_row_Sent_and_returns_1));
        db.EmailQueue.Add(PendingRow());
        await db.SaveChangesAsync();

        var sender = new FakeSender();
        var drainer = new EmailQueueDrainer(db, sender, NullLogger<EmailQueueDrainer>.Instance);

        var count = await drainer.DrainAsync(CancellationToken.None);

        count.Should().Be(1);
        var row = await db.EmailQueue.IgnoreQueryFilters().SingleAsync();
        row.Status.Should().Be(EmailQueueStatus.Sent);
        row.SentAt.Should().NotBeNull();
        sender.Received.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendFailure_below_cap_increments_attempts_stays_Pending_returns_0()
    {
        await using var db = Ctx(nameof(SendFailure_below_cap_increments_attempts_stays_Pending_returns_0));
        db.EmailQueue.Add(PendingRow(attempts: 1));
        await db.SaveChangesAsync();

        var sender = new FakeSender(perCall: _ => EmailSendResult.Fail("boom"));
        var drainer = new EmailQueueDrainer(db, sender, NullLogger<EmailQueueDrainer>.Instance);

        var count = await drainer.DrainAsync(CancellationToken.None);

        count.Should().Be(0);
        var row = await db.EmailQueue.IgnoreQueryFilters().SingleAsync();
        row.Status.Should().Be(EmailQueueStatus.Pending);
        row.Attempts.Should().Be(2);
        row.SentAt.Should().BeNull();
    }

    [Fact]
    public async Task Row_at_attempts_4_that_fails_becomes_Failed()
    {
        await using var db = Ctx(nameof(Row_at_attempts_4_that_fails_becomes_Failed));
        db.EmailQueue.Add(PendingRow(attempts: 4));
        await db.SaveChangesAsync();

        var sender = new FakeSender(perCall: _ => EmailSendResult.Fail("still failing"));
        var drainer = new EmailQueueDrainer(db, sender, NullLogger<EmailQueueDrainer>.Instance);

        await drainer.DrainAsync(CancellationToken.None);

        var row = await db.EmailQueue.IgnoreQueryFilters().SingleAsync();
        row.Status.Should().Be(EmailQueueStatus.Failed);
        row.Attempts.Should().Be(5);
    }

    [Fact]
    public async Task Row_with_attachment_sends_OutboundEmail_with_attachment_bytes()
    {
        await using var db = Ctx(nameof(Row_with_attachment_sends_OutboundEmail_with_attachment_bytes));

        // Seed a StoredFile — it is an AuditableEntity (not TenantEntity) so no global filter issues.
        var fileBytes = new byte[] { 10, 20, 30 };
        var storedFile = new StoredFile
        {
            TenantId    = TenantId,
            FileName    = "report.pdf",
            ContentType = "application/pdf",
            Data        = fileBytes,
            SizeBytes   = fileBytes.Length,
        };
        db.Files.Add(storedFile);
        await db.SaveChangesAsync();

        db.EmailQueue.Add(PendingRow(attachmentFileId: storedFile.Id));
        await db.SaveChangesAsync();

        var sender = new FakeSender();
        var drainer = new EmailQueueDrainer(db, sender, NullLogger<EmailQueueDrainer>.Instance);

        await drainer.DrainAsync(CancellationToken.None);

        sender.Received.Should().HaveCount(1);
        var outbound = sender.Received[0];
        outbound.Attachment.Should().NotBeNull();
        outbound.Attachment!.FileName.Should().Be("report.pdf");
        outbound.Attachment.ContentType.Should().Be("application/pdf");
        outbound.Attachment.Content.Should().BeEquivalentTo(fileBytes);
    }

    [Fact]
    public async Task BatchIsolation_first_row_throws_second_still_processed()
    {
        await using var db = Ctx(nameof(BatchIsolation_first_row_throws_second_still_processed));

        // Insert two rows. The throwing sender throws on the first call, succeeds on the second.
        // We use ordering by CreatedAt so row1 arrives before row2.
        var row1 = PendingRow();
        row1.Id = Guid.Parse("11111111-0000-0000-0000-000000000001");
        var row2 = PendingRow();
        row2.Id = Guid.Parse("11111111-0000-0000-0000-000000000002");

        // Give them distinct CreatedAt so ordering is deterministic.
        row1.CreatedAt = DateTime.UtcNow.AddSeconds(-2);
        row2.CreatedAt = DateTime.UtcNow.AddSeconds(-1);

        db.EmailQueue.AddRange(row1, row2);
        await db.SaveChangesAsync();

        // The sender throws only on the first call, succeeds on the second.
        int callIndex = 0;
        var sender = new FakeSender(perCall: _ =>
        {
            if (++callIndex == 1) throw new Exception("bang on first row");
            return EmailSendResult.Ok();
        });

        var drainer = new EmailQueueDrainer(db, sender, NullLogger<EmailQueueDrainer>.Instance);
        var count = await drainer.DrainAsync(CancellationToken.None);

        // Second row sent successfully → count = 1.
        count.Should().Be(1);

        var rows = await db.EmailQueue.IgnoreQueryFilters().OrderBy(r => r.CreatedAt).ToListAsync();
        // First row: exception mapped to a failed attempt (Attempts becomes 1, stays Pending at cap>1).
        rows[0].Attempts.Should().Be(1);
        rows[0].Status.Should().Be(EmailQueueStatus.Pending);
        // Second row: sent successfully.
        rows[1].Status.Should().Be(EmailQueueStatus.Sent);
        rows[1].SentAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CanSend_false_rows_stay_Pending_and_returns_0()
    {
        await using var db = Ctx(nameof(CanSend_false_rows_stay_Pending_and_returns_0));
        db.EmailQueue.Add(PendingRow(attempts: 0));
        db.EmailQueue.Add(PendingRow(attempts: 2));
        await db.SaveChangesAsync();

        var sender = new FakeSender(canSend: false);
        var drainer = new EmailQueueDrainer(db, sender, NullLogger<EmailQueueDrainer>.Instance);

        var count = await drainer.DrainAsync(CancellationToken.None);

        count.Should().Be(0);
        sender.Received.Should().BeEmpty();

        var rows = await db.EmailQueue.IgnoreQueryFilters().ToListAsync();
        rows.Should().AllSatisfy(r =>
        {
            r.Status.Should().Be(EmailQueueStatus.Pending);
            r.SentAt.Should().BeNull();
        });
        // Attempts must be untouched — this is the regression test for FIX 1.
        rows.Select(r => r.Attempts).Should().BeEquivalentTo(new[] { 0, 2 });
    }
}
