using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Forms;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Infrastructure.Services;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

/// <summary>
/// TDD: 3 facts for BackfillFieldClassificationsAsync — the provisioning pass that stamps
/// FormField.MetadataJson with SystemRequired / Optional and never overwrites an existing value.
///
/// These use an in-memory provider because the behaviour under test (classification logic) does not
/// require the global tenant query filter or SaveChanges tenant stamping; that is covered by
/// RequestProvisioningTests (which needs a real PostgreSQL connection).
/// </summary>
public class FieldClassificationSeedingTests
{
    // ─── Harness ───────────────────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public static readonly Guid UserId   = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        public static readonly Guid TenantId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        Guid ICurrentUserService.UserId          => UserId;
        Guid ICurrentUserService.TenantId        => TenantId;
        string? ICurrentUserService.Email        => "test@hr.local";
        IReadOnlyList<string> ICurrentUserService.Permissions => Array.Empty<string>();
        bool ICurrentUserService.IsAuthenticated => true;
    }

    private static ApplicationDbContext MakeDb()
        => new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            new FakeUser());

    private sealed class NoopPageTemplateSeeder : HR.Modules.Platform.Services.Documents.IPageTemplateSeeder
    {
        public Task<Guid> SeedAsync(CancellationToken ct) => Task.FromResult(Guid.Empty);
    }

    private sealed class NoopDocumentLibrarySeeder : HR.Modules.Platform.Services.Documents.IDocumentLibrarySeeder
    {
        public Task SeedAsync(Guid defaultPageTemplateId, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Build a RequestProvisioningService wired to an in-memory DB that is already scoped to a
    /// single tenant (so global query filters work as expected for in-memory).
    /// </summary>
    private static (ApplicationDbContext db, BackgroundExecutionContext bg, RequestProvisioningService svc)
        BuildHarness(ApplicationDbContext db)
    {
        var bg   = new BackgroundExecutionContext();
        // Activate the background scope so the ambient tenant is set for seeding.
        var user = new FakeUser();
        _ = bg.Begin(FakeUser.TenantId, FakeUser.UserId, email: null, correlationId: Guid.NewGuid());
        var seeder = new RequestSeeder(db, new NoopPageTemplateSeeder(), new NoopDocumentLibrarySeeder());
        var svc    = new RequestProvisioningService(db, seeder, bg, NullLogger<RequestProvisioningService>.Instance);
        return (db, bg, svc);
    }

    // ─── Fact 1: after provisioning, mapped field → SystemRequired; unmapped → Optional ─

    /// <summary>
    /// Selects LOAN_REQUEST: the LoanCreate required effect maps loanType, amount, installmentMonths
    /// via FormField; the "kind" input uses Const (not FormField). The form also has a "reason" field
    /// that is NOT referenced by any required-effect input at all.
    ///
    /// Expected outcome:
    ///   loanType, amount, installmentMonths → SystemRequired  (mapped via FormField source)
    ///   reason                              → Optional        (present on form, not in required effects)
    /// </summary>
    [Fact]
    public async Task Backfill_stamps_system_required_on_effect_mapped_fields()
    {
        var db = MakeDb();
        var (_, bg, svc) = BuildHarness(db);

        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var loanType = await db.RequestTypes.FirstAsync(t => t.Code == "LOAN_REQUEST");
        var fields   = await db.FormFields
            .Where(f => f.FormDefinitionId == loanType.FormDefinitionId)
            .ToListAsync();

        // Fields mapped by the required LoanCreate effect via FormField source → SystemRequired
        foreach (var mappedCode in new[] { "loanType", "amount", "installmentMonths" })
        {
            var f = fields.Should().ContainSingle(x => x.Code == mappedCode).Subject;
            FormFieldClassification.Of(f.MetadataJson).Should().Be(FieldClassification.SystemRequired,
                because: $"{mappedCode} is a FormField input of the LOAN_REQUEST required effect");
        }

        // "reason" is on the form but NOT referenced by any required effect → Optional
        var reasonField = fields.Should().ContainSingle(f => f.Code == "reason").Subject;
        FormFieldClassification.Of(reasonField.MetadataJson).Should().Be(FieldClassification.Optional,
            because: "reason is present on the loan form but not mapped by any required effect");
    }

    // ─── Fact 2: an existing classification is NEVER overwritten ──────────────

    [Fact]
    public async Task Backfill_does_not_overwrite_existing_classification()
    {
        var db = MakeDb();
        var (_, bg, svc) = BuildHarness(db);

        // First provision to create all the system request types + forms.
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // Simulate a tenant that has set the "loanType" field on LOAN_REQUEST to Custom.
        // "loanType" would normally be stamped SystemRequired, so this also verifies that
        // an existing explicit classification (even one that contradicts what backfill would set)
        // is never overwritten.
        var loanType = await db.RequestTypes.FirstAsync(t => t.Code == "LOAN_REQUEST");
        var loanTypeField = await db.FormFields
            .FirstAsync(f => f.FormDefinitionId == loanType.FormDefinitionId && f.Code == "loanType");

        loanTypeField.MetadataJson = FormFieldClassification.With(FieldClassification.Custom);
        // Also roll back the SeedVersion so provisioning re-runs the backfill pass.
        loanType.SeedVersion = 0;
        await db.SaveChangesAsync();

        // Second provision — the backfill must NOT overwrite the Custom classification.
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var reloaded = await db.FormFields
            .FirstAsync(f => f.FormDefinitionId == loanType.FormDefinitionId && f.Code == "loanType");
        FormFieldClassification.Of(reloaded.MetadataJson).Should().Be(FieldClassification.Custom,
            because: "a tenant-set Custom classification must survive a re-provision");
    }

    // ─── Fact 3: idempotency — two provision passes produce identical state ────

    [Fact]
    public async Task Backfill_is_idempotent()
    {
        var db = MakeDb();
        var (_, bg, svc) = BuildHarness(db);

        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        // Capture all field classifications after the first pass.
        var after1 = await db.FormFields
            .AsNoTracking()
            .Select(f => new { f.Id, f.MetadataJson })
            .ToDictionaryAsync(f => f.Id, f => f.MetadataJson);

        // Run again — the second pass should change nothing.
        await svc.ProvisionTenantAsync(FakeUser.TenantId, FakeUser.UserId, default);

        var after2 = await db.FormFields
            .AsNoTracking()
            .Select(f => new { f.Id, f.MetadataJson })
            .ToDictionaryAsync(f => f.Id, f => f.MetadataJson);

        after2.Should().BeEquivalentTo(after1,
            because: "re-provisioning must not change any field classification");
    }
}
