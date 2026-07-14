using System;
using System.Linq;
using AutoMapper;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Models;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.Reports;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record RunReportQuery(Guid Id, int Page, int PageSize) : IRequest<ReportResult>;

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
        var result = await _exec.RunAsync(request.Id, request.Page, request.PageSize, ct);
        var state = await ReportUserStateHelper.GetOrCreateAsync(_db, _user.UserId, request.Id, ct);
        state.LastViewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return result;
    }
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
            .AsQueryable();
        var query = await _access.FilterVisibleAsync(baseQuery, ct);
        if (!string.IsNullOrEmpty(request.Search))
            query = query.Where(r => r.NameEn.Contains(request.Search) || r.NameAr.Contains(request.Search));

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
        return new PaginatedList<ReportDefinitionDto> { Items = _mapper.Map<List<ReportDefinitionDto>>(items), PageNumber = request.PageNumber, PageSize = request.PageSize, TotalCount = totalCount };
    }
}

public class GetReportByIdQueryHandler : IRequestHandler<GetReportByIdQuery, ReportDefinitionDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public GetReportByIdQueryHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access)
    { _context = context; _mapper = mapper; _access = access; }
    public async Task<ReportDefinitionDto> Handle(GetReportByIdQuery request, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(request.Id, ct);
        var entity = await _context.Set<ReportDefinition>().Include(r => r.Fields.OrderBy(f => f.SortOrder)).Include(r => r.Filters).Include(r => r.Groupings).Include(r => r.Sortings).FirstOrDefaultAsync(r => r.Id == request.Id, ct) ?? throw new HR.Application.Common.Exceptions.NotFoundException("ReportDefinition", request.Id);
        return _mapper.Map<ReportDefinitionDto>(entity);
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
