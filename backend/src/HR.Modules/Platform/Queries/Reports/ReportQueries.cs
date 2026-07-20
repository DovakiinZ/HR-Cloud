using System;
using System.Linq;
using AutoMapper;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.Reports;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record RunReportQuery(
    Guid Id, int Page, int PageSize,
    IReadOnlyDictionary<string, string?>? Parameters = null) : IRequest<ReportResult>;

public class RunReportQueryHandler : IRequestHandler<RunReportQuery, ReportResult>
{
    private readonly IReportExecutionService _exec;
    private readonly IReportAccessService _access;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    public RunReportQueryHandler(IReportExecutionService exec, IReportAccessService access, ApplicationDbContext db, ICurrentUserService user)
    { _exec = exec; _access = access; _db = db; _user = user; }
    public async Task<ReportResult> Handle(RunReportQuery request, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(request.Id, ct);
        var result = await _exec.RunAsync(request.Id, request.Page, request.PageSize, request.Parameters, ct);
        await StampLastViewedAsync(request.Id, ct);
        return result;
    }

    /// <summary>
    /// Records "you last opened this report now". This is bookkeeping for the Recent view, so it
    /// must never fail the run: the report has already executed and the caller is entitled to it.
    ///
    /// Two concurrent runs of the same report both see no user-state row, both insert, and the
    /// second violates IX_engine_report_user_states_TenantId_UserId_ReportDefinitionId — a 500 that
    /// loses the whole result over a timestamp. React's development double-invoke reproduces it on
    /// a plain page load. On that collision the other request has already written the row, so
    /// stamping it again is redundant; give up quietly rather than retry.
    /// </summary>
    private async Task StampLastViewedAsync(Guid reportId, CancellationToken ct)
    {
        try
        {
            var state = await ReportUserStateHelper.GetOrCreateAsync(_db, _user.UserId, reportId, ct);
            state.LastViewedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Detach the row we failed to insert, or it is retried on the next SaveChanges made
            // through this same scoped context and fails again there.
            foreach (var entry in _db.ChangeTracker.Entries<ReportUserState>()
                         .Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
    }
}

public static class ReportSearch
{
    /// <summary>Escape character for the LIKE patterns built from user input.</summary>
    public const string LikeEscape = "\\";

    /// <summary>Neutralises LIKE metacharacters in a user's search term. Without this, searching for
    /// the literal code "DEMO_001" would treat "_" as "any single character" and also match
    /// "DEMO-001" or "DEMOX001".</summary>
    public static string EscapeLike(string input)
        => input.Replace(LikeEscape, LikeEscape + LikeEscape)
                .Replace("%", LikeEscape + "%")
                .Replace("_", LikeEscape + "_");
}

public record GetReportsQuery : IRequest<PaginatedList<ReportDefinitionDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? Search { get; init; }
    public string? Scope { get; init; }
    public string? View { get; init; }      // favorites | recent | pinned
    public Guid? FolderId { get; init; }
    public Guid? TagId { get; init; }
}

public record GetReportByIdQuery(Guid Id) : IRequest<ReportDefinitionDto>;
public record GetReportTemplatesQuery : IRequest<List<ReportTemplateDto>>;

public class GetReportsQueryHandler : IRequestHandler<GetReportsQuery, PaginatedList<ReportDefinitionDto>>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access; private readonly ICurrentUserService _user;
    public GetReportsQueryHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access, ICurrentUserService user)
    { _context = context; _mapper = mapper; _access = access; _user = user; }
    public async Task<PaginatedList<ReportDefinitionDto>> Handle(GetReportsQuery request, CancellationToken ct)
    {
        var baseQuery = _context.Set<ReportDefinition>()
            .Include(r => r.Fields.OrderBy(f => f.SortOrder)).Include(r => r.Filters)
            .Include(r => r.Groupings).Include(r => r.Sortings).Include(r => r.Shares)
            .Include(r => r.Relationships.OrderBy(x => x.SortOrder))
            .AsQueryable();
        var query = await _access.FilterVisibleAsync(baseQuery, ct);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // ILIKE, not Contains: Contains translates to case-sensitive LIKE, so "attendance" found
            // nothing while "Attendance" did. Code is searched too — users refer to reports by code
            // (DEMO_001, SYS_PAYROLL) at least as often as by name.
            // % and _ are escaped so a code containing an underscore matches literally rather than
            // as a single-character wildcard.
            var term = $"%{ReportSearch.EscapeLike(request.Search.Trim())}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.NameEn, term, ReportSearch.LikeEscape)
                || EF.Functions.ILike(r.NameAr, term, ReportSearch.LikeEscape)
                || EF.Functions.ILike(r.Code, term, ReportSearch.LikeEscape));
        }

        // Scope was declared but never applied — filtering by it silently returned everything.
        if (!string.IsNullOrWhiteSpace(request.Scope))
        {
            if (!Enum.TryParse<ReportScope>(request.Scope, ignoreCase: true, out var scope))
                throw new HR.Application.Common.Exceptions.ValidationException(new[]
                {
                    new FluentValidation.Results.ValidationFailure("scope",
                        $"Unknown scope '{request.Scope}'. Use Personal, Department, Company, or Shared."),
                });
            query = query.Where(r => r.Scope == scope);
        }

        if (request.FolderId is { } fid)
            query = query.Where(r => r.FolderId == fid);

        if (request.TagId is { } tid)
            query = query.Where(r => _context.ReportDefinitionTags.Any(l => l.ReportTagId == tid && l.ReportDefinitionId == r.Id));

        var uid = _user.UserId;
        if (string.Equals(request.View, "favorites", StringComparison.OrdinalIgnoreCase))
            query = query.Where(r => _context.ReportUserStates.Any(s => s.UserId == uid && s.ReportDefinitionId == r.Id && s.IsFavorite));
        else if (string.Equals(request.View, "pinned", StringComparison.OrdinalIgnoreCase))
            query = query.Where(r => _context.ReportUserStates.Any(s => s.UserId == uid && s.ReportDefinitionId == r.Id && s.IsPinned));

        if (string.Equals(request.View, "recent", StringComparison.OrdinalIgnoreCase))
        {
            var recent = from r in query
                         join s in _context.ReportUserStates.Where(s => s.UserId == uid && s.LastViewedAt != null)
                             on r.Id equals s.ReportDefinitionId
                         orderby s.LastViewedAt descending
                         select r;
            query = recent;
        }

        var totalCount = await query.CountAsync(ct);

        IQueryable<ReportDefinition> ordered = string.Equals(request.View, "recent", StringComparison.OrdinalIgnoreCase)
            ? query
            : query.OrderByDescending(r => r.CreatedAt);

        var items = await ordered
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);

        var dtos = _mapper.Map<List<ReportDefinitionDto>>(items);
        await StitchUserStateAndTagsAsync(_context, _mapper, uid, dtos, ct);
        return new PaginatedList<ReportDefinitionDto> { Items = dtos, PageNumber = request.PageNumber, PageSize = request.PageSize, TotalCount = totalCount };
    }

    /// <summary>Loads the caller's states and the tag links for the whole page in two batched
    /// queries, then stitches. Per-report queries here would be an N+1 across the page.</summary>
    internal static async Task StitchUserStateAndTagsAsync(
        ApplicationDbContext context, IMapper mapper, Guid userId, List<ReportDefinitionDto> dtos, CancellationToken ct)
    {
        if (dtos.Count == 0) return;
        var ids = dtos.Select(d => d.Id).ToList();

        var states = await context.ReportUserStates
            .Where(s => s.UserId == userId && ids.Contains(s.ReportDefinitionId))
            .ToListAsync(ct);

        var tagLinks = await (from link in context.ReportDefinitionTags
                              join tag in context.ReportTags on link.ReportTagId equals tag.Id
                              where ids.Contains(link.ReportDefinitionId)
                              select new { link.ReportDefinitionId, Tag = tag })
            .ToListAsync(ct);

        var tagsByReportId = tagLinks
            .GroupBy(x => x.ReportDefinitionId)
            .ToDictionary(g => g.Key, g => mapper.Map<List<ReportTagDto>>(g.Select(x => x.Tag).ToList()));

        ReportListProjector.Apply(dtos, states, tagsByReportId);
    }
}

public class GetReportByIdQueryHandler : IRequestHandler<GetReportByIdQuery, ReportDefinitionDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access; private readonly ICurrentUserService _user;
    public GetReportByIdQueryHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access, ICurrentUserService user)
    { _context = context; _mapper = mapper; _access = access; _user = user; }
    public async Task<ReportDefinitionDto> Handle(GetReportByIdQuery request, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(request.Id, ct);
        var entity = await _context.Set<ReportDefinition>().Include(r => r.Fields.OrderBy(f => f.SortOrder)).Include(r => r.Filters).Include(r => r.Groupings).Include(r => r.Sortings).Include(r => r.Relationships.OrderBy(x => x.SortOrder)).FirstOrDefaultAsync(r => r.Id == request.Id, ct) ?? throw new HR.Application.Common.Exceptions.NotFoundException("ReportDefinition", request.Id);
        var dto = _mapper.Map<ReportDefinitionDto>(entity);
        await GetReportsQueryHandler.StitchUserStateAndTagsAsync(_context, _mapper, _user.UserId, new List<ReportDefinitionDto> { dto }, ct);
        return dto;
    }
}

public class GetReportTemplatesQueryHandler : IRequestHandler<GetReportTemplatesQuery, List<ReportTemplateDto>>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper;
    public GetReportTemplatesQueryHandler(ApplicationDbContext context, IMapper mapper) { _context = context; _mapper = mapper; }
    public async Task<List<ReportTemplateDto>> Handle(GetReportTemplatesQuery request, CancellationToken ct)
    {
        var items = await _context.Set<ReportTemplate>().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ToListAsync(ct);
        return _mapper.Map<List<ReportTemplateDto>>(items);
    }
}
