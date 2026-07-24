using AutoMapper;
using FluentAssertions;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Forms;
using HR.Domain.Engines.Forms;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.Forms;
using HR.Modules.Platform.MappingProfiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace HR.Modules.Platform.Tests.Forms;

file sealed class FakeUser : HR.Application.Common.Interfaces.ICurrentUserService
{
    public Guid UserId => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public Guid TenantId => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    public string? Email => "test@hr.local";
    public IReadOnlyList<string> Permissions => Array.Empty<string>();
    public bool IsAuthenticated => true;
}

/// <summary>
/// TDD: 6 facts that verify server-enforced field lock guards.
/// </summary>
public class FormFieldLockTests
{
    private static ApplicationDbContext MakeDb()
    {
        var user = new FakeUser();
        return new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options,
            user);
    }

    private static IMapper MakeMapper() =>
        new MapperConfiguration(c => c.AddProfile<PlatformMappingProfile>()).CreateMapper();

    // ── Shared seed helpers ────────────────────────────────────────────────────

    private static async Task<(ApplicationDbContext db, FormDefinition form, FormField field)>
        SeedFormWithField(FieldClassification classification, string code = "FIELD_CODE")
    {
        var db = MakeDb();

        var form = new FormDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = db.CurrentTenantId(),
            Code = "TEST_FORM",
            NameEn = "Test Form",
            NameAr = "نموذج اختبار",
            Module = "Platform",
        };
        db.FormDefinitions.Add(form);

        var field = new FormField
        {
            Id = Guid.NewGuid(),
            FormDefinitionId = form.Id,
            Code = code,
            NameEn = "Field",
            NameAr = "حقل",
            FieldType = FieldType.Text,
            IsRequired = true,
            MetadataJson = FormFieldClassification.With(classification),
        };
        db.FormFields.Add(field);

        await db.SaveChangesAsync();
        return (db, form, field);
    }

    // ── Fact 1: Delete SystemRequired field is blocked ────────────────────────

    [Fact]
    public async Task Delete_system_required_field_is_blocked()
    {
        var (db, _, field) = await SeedFormWithField(FieldClassification.SystemRequired);
        var handler = new DeleteFormFieldCommandHandler(db);

        var act = () => handler.Handle(new DeleteFormFieldCommand(field.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        // Field must still exist in DB
        db.FormFields.Any(f => f.Id == field.Id).Should().BeTrue();
    }

    // ── Fact 2: Delete Optional field is allowed ──────────────────────────────

    [Fact]
    public async Task Delete_optional_field_is_allowed()
    {
        var (db, _, field) = await SeedFormWithField(FieldClassification.Optional);
        var handler = new DeleteFormFieldCommandHandler(db);

        await handler.Handle(new DeleteFormFieldCommand(field.Id), CancellationToken.None);

        db.FormFields.Any(f => f.Id == field.Id).Should().BeFalse();
    }

    // ── Fact 3: Update clearing IsRequired on SystemRequired field is blocked ─

    [Fact]
    public async Task Update_clearing_required_on_system_field_is_blocked()
    {
        var (db, _, field) = await SeedFormWithField(FieldClassification.SystemRequired);
        var mapper = MakeMapper();
        var handler = new UpdateFormFieldCommandHandler(db, mapper);

        var cmd = new UpdateFormFieldCommand
        {
            Id = field.Id,
            Code = field.Code,
            NameEn = field.NameEn,
            NameAr = field.NameAr,
            FieldType = field.FieldType,
            IsRequired = false,   // clearing required on a SystemRequired field → blocked
            SortOrder = field.SortOrder,
        };

        var act = () => handler.Handle(cmd, CancellationToken.None);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    // ── Fact 4: Update changing Code on non-Custom field is blocked; Custom is allowed ──

    [Fact]
    public async Task Update_changing_code_of_non_custom_field_is_blocked_and_custom_is_allowed()
    {
        // Non-Custom (SystemRequired): changing Code is blocked
        {
            var (db, _, field) = await SeedFormWithField(FieldClassification.SystemRequired, code: "ORIG_CODE");
            var mapper = MakeMapper();
            var handler = new UpdateFormFieldCommandHandler(db, mapper);

            var cmd = new UpdateFormFieldCommand
            {
                Id = field.Id,
                Code = "NEW_CODE",   // changed
                NameEn = field.NameEn,
                NameAr = field.NameAr,
                FieldType = field.FieldType,
                IsRequired = true,
                SortOrder = field.SortOrder,
            };

            var act = () => handler.Handle(cmd, CancellationToken.None);
            await act.Should().ThrowAsync<ForbiddenException>();
        }

        // Custom: changing Code is allowed
        {
            var (db, _, field) = await SeedFormWithField(FieldClassification.Custom, code: "ORIG_CODE");
            var mapper = MakeMapper();
            var handler = new UpdateFormFieldCommandHandler(db, mapper);

            var cmd = new UpdateFormFieldCommand
            {
                Id = field.Id,
                Code = "NEW_CODE",
                NameEn = field.NameEn,
                NameAr = field.NameAr,
                FieldType = field.FieldType,
                IsRequired = false,
                SortOrder = field.SortOrder,
            };

            await handler.Handle(cmd, CancellationToken.None);
            // Code was updated
            var updated = await db.FormFields.FindAsync(field.Id);
            updated!.Code.Should().Be("NEW_CODE");
        }
    }

    // ── Fact 5: Label/Placeholder edits on SystemRequired field are allowed ───

    [Fact]
    public async Task Update_label_and_placeholder_on_system_field_is_allowed()
    {
        var (db, _, field) = await SeedFormWithField(FieldClassification.SystemRequired);
        var mapper = MakeMapper();
        var handler = new UpdateFormFieldCommandHandler(db, mapper);

        var cmd = new UpdateFormFieldCommand
        {
            Id = field.Id,
            Code = field.Code,            // unchanged
            NameEn = "Updated Label EN",  // changed label
            NameAr = "تسمية محدّثة",      // changed label
            FieldType = field.FieldType,
            IsRequired = true,            // keep required
            SortOrder = field.SortOrder,
            Placeholder = "New placeholder",
        };

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.NameEn.Should().Be("Updated Label EN");
        result.NameAr.Should().Be("تسمية محدّثة");
    }

    // ── Fact 6: Delete field mapped by a required effect is blocked ───────────

    [Fact]
    public async Task Delete_field_mapped_by_a_required_effect_is_blocked()
    {
        const string fieldCode = "MAPPED_FIELD";
        var (db, form, field) = await SeedFormWithField(FieldClassification.Optional, code: fieldCode);

        // Seed a RequestType using this form
        var requestType = new RequestType
        {
            Id = Guid.NewGuid(),
            TenantId = db.CurrentTenantId(),
            Code = "RT_TEST",
            NameEn = "RT Test",
            NameAr = "اختبار",
            FormDefinitionId = form.Id,
            IsActive = true,
        };
        db.RequestTypes.Add(requestType);

        // Seed a required, enabled effect whose ConfigurationJson maps an input to this field
        var effectConfig = EffectConfiguration.Serialize(
            new Dictionary<string, EffectValueMapping>
            {
                ["someInput"] = new EffectValueMapping
                {
                    Source = EffectValueSource.FormField,
                    Key = fieldCode,
                },
            });

        var effect = new RequestEffectDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = db.CurrentTenantId(),
            RequestTypeId = requestType.Id,
            EffectType = "Test.Action",
            IsRequired = true,
            IsEnabled = true,
            ConfigurationJson = effectConfig,
        };
        db.RequestEffectDefinitions.Add(effect);
        await db.SaveChangesAsync();

        var handler = new DeleteFormFieldCommandHandler(db);
        var act = () => handler.Handle(new DeleteFormFieldCommand(field.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}

/// <summary>Helper extension to expose the fake user's TenantId from the DbContext.</summary>
file static class DbContextExtensions
{
    public static Guid CurrentTenantId(this ApplicationDbContext db)
        => Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
}
