using System.Text.Json.Serialization;
using AutoMapper;
using HR.Application.Common.Interfaces;
using HR.Application.Reports.Registry;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Commands.Reports;

// ════════════════════════════════════════════════════════════════════════════════
//  Simplified ("quick") report creation — Reports simplification Phases 3 + 4.
//  The caller sends trusted business field KEYS from the Report Field Registry; the
//  server discovers the primary object, the join chain and the executable columns.
//  Nothing here modifies the report execution engine, the SQL builder or saved reports.
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>Create a runnable report from registry field keys (no joins/codes/SQL from the client).</summary>
public record CreateQuickReportCommand : IRequest<QuickReportResult>
{
    public string NameAr { get; init; } = null!;
    public string NameEn { get; init; } = null!;
    public string? Description { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReportScope Scope { get; init; } = ReportScope.Company;

    /// <summary>Ordered registry field keys to show as columns (e.g. "employees.fullName").</summary>
    public List<string> FieldKeys { get; init; } = new();

    public List<QuickFilterInput> Filters { get; init; } = new();
    public List<string> GroupByKeys { get; init; } = new();
    public List<QuickSortInput> Sorts { get; init; } = new();

    /// <summary>Optional explicit code (used by the system-report seeder for idempotency).
    /// When null a unique code is generated.</summary>
    public string? Code { get; init; }

    /// <summary>Publish immediately so the report is ready to run/share. Default true.</summary>
    public bool Publish { get; init; } = true;
}

public sealed record QuickReportResult(
    ReportDefinitionDto Report,
    IReadOnlyList<string> SkippedFieldKeys,
    IReadOnlyList<string> SkippedFilterKeys,
    IReadOnlyList<string> UnknownKeys);

public class CreateQuickReportCommandHandler : IRequestHandler<CreateQuickReportCommand, QuickReportResult>
{
    private readonly ApplicationDbContext _context;
    private readonly IMapper _mapper;
    private readonly IReportFieldRegistry _registry;
    private readonly IReportObjectIdResolver _ids;
    private readonly ICurrentUserService _user;

    public CreateQuickReportCommandHandler(
        ApplicationDbContext context, IMapper mapper,
        IReportFieldRegistry registry, IReportObjectIdResolver ids, ICurrentUserService user)
    {
        _context = context; _mapper = mapper; _registry = registry; _ids = ids; _user = user;
    }

    public async Task<QuickReportResult> Handle(CreateQuickReportCommand request, CancellationToken ct)
    {
        var ctx = new ReportRegistryContext(_user.Permissions);

        // Resolve the UNION of every referenced key once (display + filter + group + sort), so the
        // composer can look any of them up — permission-filtered by the caller's own permissions.
        var allKeys = request.FieldKeys
            .Concat(request.Filters.Select(f => f.FieldKey))
            .Concat(request.GroupByKeys)
            .Concat(request.Sorts.Select(s => s.FieldKey))
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var resolved = _registry.Resolve(ctx, allKeys);
        var byKey = resolved.Fields
            .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var plan = QuickReportComposer.Compose(
            byKey, request.FieldKeys, request.Filters, request.GroupByKeys, request.Sorts, _ids);

        var reportId = Guid.NewGuid();
        var report = new ReportDefinition
        {
            Id = reportId,
            Code = request.Code ?? $"QR_{Guid.NewGuid():N}"[..15].ToUpperInvariant(),
            NameEn = request.NameEn,
            NameAr = request.NameAr,
            Description = request.Description,
            ReportType = plan.HasAggregation ? ReportType.Summary : ReportType.Tabular,
            Scope = request.Scope,
            OwnerId = _user.UserId,
            PrimaryObjectId = plan.PrimaryObjectId,
            IsPublished = request.Publish,
            IsActive = true,
            Version = 1,
        };

        foreach (var f in plan.Fields)
            report.Fields.Add(new ReportField
            {
                Id = Guid.NewGuid(),
                ReportDefinitionId = reportId,
                FieldType = ReportFieldType.ObjectField,
                ObjectDefinitionId = f.ObjectDefinitionId,
                FieldCode = f.FieldCode,
                DisplayNameEn = f.LabelEn,
                DisplayNameAr = f.LabelAr,
                Aggregation = f.Aggregation,
                SortOrder = f.SortOrder,
                IsVisible = true,
            });

        foreach (var j in plan.Joins)
            report.Relationships.Add(new ReportRelationship
            {
                Id = Guid.NewGuid(),
                ReportDefinitionId = reportId,
                SourceObjectId = j.SourceObjectId,
                TargetObjectId = j.TargetObjectId,
                JoinField = j.JoinField,
                JoinType = "Left",   // include primary rows even when the related row is missing
                SortOrder = j.SortOrder,
            });

        foreach (var flt in plan.Filters)
            report.Filters.Add(new ReportFilter
            {
                Id = Guid.NewGuid(),
                ReportDefinitionId = reportId,
                FieldCode = flt.FieldCode,
                Operator = flt.Operator,
                Value = flt.Value,
                ValueTo = flt.ValueTo,
                IsParameter = flt.IsParameter,
            });

        foreach (var g in plan.Groupings)
            report.Groupings.Add(new ReportGrouping
            {
                Id = Guid.NewGuid(),
                ReportDefinitionId = reportId,
                FieldCode = g.FieldCode,
                SortOrder = g.SortOrder,
            });

        foreach (var s in plan.Sortings)
            report.Sortings.Add(new ReportSorting
            {
                Id = Guid.NewGuid(),
                ReportDefinitionId = reportId,
                FieldCode = s.FieldCode,
                Direction = s.Direction,
                SortOrder = s.SortOrder,
            });

        _context.Set<ReportDefinition>().Add(report);
        await _context.SaveChangesAsync(ct);

        return new QuickReportResult(
            _mapper.Map<ReportDefinitionDto>(report),
            plan.SkippedFieldKeys,
            plan.SkippedFilterKeys,
            resolved.UnknownKeys);
    }
}

// ════════════════════════════════════════════════════════════════════════════════
//  System / "standard" reports seeding — Reports simplification Phase 2.
//  Idempotently creates one ready-to-run standard report per subject the caller can
//  see, using the quick-create path above. Codes are SYS_<SUBJECT> so the frontend can
//  group them under "التقارير الأساسية". Safe to call repeatedly (per tenant).
// ════════════════════════════════════════════════════════════════════════════════

public record SeedSystemReportsCommand : IRequest<SeedSystemReportsResult>;

public sealed record SeedSystemReportsResult(
    int Created, int Skipped, IReadOnlyList<string> Codes);

public class SeedSystemReportsCommandHandler : IRequestHandler<SeedSystemReportsCommand, SeedSystemReportsResult>
{
    // A comfortable column cap for an auto-built standard report.
    private const int MaxColumns = 12;

    private readonly ApplicationDbContext _context;
    private readonly IReportFieldRegistry _registry;
    private readonly ICurrentUserService _user;
    private readonly ISender _mediator;
    private readonly ILogger<SeedSystemReportsCommandHandler> _logger;

    public SeedSystemReportsCommandHandler(
        ApplicationDbContext context, IReportFieldRegistry registry,
        ICurrentUserService user, ISender mediator,
        ILogger<SeedSystemReportsCommandHandler> logger)
    {
        _context = context; _registry = registry; _user = user; _mediator = mediator; _logger = logger;
    }

    public async Task<SeedSystemReportsResult> Handle(SeedSystemReportsCommand request, CancellationToken ct)
    {
        var ctx = new ReportRegistryContext(_user.Permissions);
        var created = new List<string>();
        var skipped = 0;

        var existingCodes = await _context.Set<ReportDefinition>()
            .Select(r => r.Code)
            .Where(c => c.StartsWith("SYS_"))
            .ToListAsync(ct);
        var existing = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        foreach (var subject in _registry.GetSubjects(ctx))
        {
            var code = $"SYS_{subject.Key.ToUpperInvariant()}";
            if (existing.Contains(code)) { skipped++; continue; }

            var keys = SelectStandardColumns(_registry.GetFields(ctx, subject.Key));
            if (keys.Count == 0) { skipped++; continue; }

            try
            {
                var result = await _mediator.Send(new CreateQuickReportCommand
                {
                    Code = code,
                    NameAr = $"{subject.LabelAr} — تقرير أساسي",
                    NameEn = $"{subject.LabelEn} — Standard Report",
                    Description = "تقرير قياسي تم إنشاؤه تلقائيًا. يمكنك نسخه وتخصيصه.",
                    Scope = ReportScope.Company,
                    FieldKeys = keys,
                    Publish = true,
                }, ct);
                created.Add(result.Report.Code);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipped standard report for subject {Subject}", subject.Key);
                skipped++;
            }
        }

        return new SeedSystemReportsResult(created.Count, skipped, created);
    }

    /// <summary>Pick a sensible, collision-free column set for a subject: default-visible fields
    /// first (falling back to the first fields), excluding the self-referential manager field
    /// which the R1 engine can't resolve to the manager's own row.</summary>
    private static List<string> SelectStandardColumns(IReadOnlyList<ReportFieldDescriptor> fields)
    {
        bool Usable(ReportFieldDescriptor f) =>
            !f.Key.EndsWith("managerName", StringComparison.OrdinalIgnoreCase);

        var ordered = fields
            .Where(Usable)
            .OrderByDescending(f => f.IsDefault)
            .ThenBy(f => f.JoinPath.Count) // own fields before joined ones
            .ThenBy(f => f.DisplayOrder)
            .ToList();

        var picked = new List<string>();
        var usedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ordered)
        {
            if (!usedColumns.Add(f.PropertyPath)) continue; // avoid engine duplicate-column collisions
            picked.Add(f.Key);
            if (picked.Count >= MaxColumns) break;
        }
        return picked;
    }
}
