using AutoMapper;
using HR.Application.Common.Models;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record RunReportQuery(Guid Id, int Page, int PageSize) : IRequest<ReportResult>;

public class RunReportQueryHandler : IRequestHandler<RunReportQuery, ReportResult>
{
    private readonly IReportExecutionService _exec;
    public RunReportQueryHandler(IReportExecutionService exec) => _exec = exec;
    public Task<ReportResult> Handle(RunReportQuery request, CancellationToken ct)
        => _exec.RunAsync(request.Id, request.Page, request.PageSize, ct);
}

public record GetReportsQuery : IRequest<PaginatedList<ReportDefinitionDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? Search { get; init; }
    public string? Scope { get; init; }
}

public record GetReportByIdQuery(Guid Id) : IRequest<ReportDefinitionDto>;
public record GetReportTemplatesQuery : IRequest<List<ReportTemplateDto>>;

public class GetReportsQueryHandler : IRequestHandler<GetReportsQuery, PaginatedList<ReportDefinitionDto>>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper;
    public GetReportsQueryHandler(ApplicationDbContext context, IMapper mapper) { _context = context; _mapper = mapper; }
    public async Task<PaginatedList<ReportDefinitionDto>> Handle(GetReportsQuery request, CancellationToken ct)
    {
        var query = _context.Set<ReportDefinition>().Include(r => r.Fields.OrderBy(f => f.SortOrder)).Include(r => r.Filters).Include(r => r.Groupings).Include(r => r.Sortings).AsQueryable();
        if (!string.IsNullOrEmpty(request.Search)) query = query.Where(r => r.NameEn.Contains(request.Search) || r.NameAr.Contains(request.Search));
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.CreatedAt).Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new PaginatedList<ReportDefinitionDto> { Items = _mapper.Map<List<ReportDefinitionDto>>(items), PageNumber = request.PageNumber, PageSize = request.PageSize, TotalCount = totalCount };
    }
}

public class GetReportByIdQueryHandler : IRequestHandler<GetReportByIdQuery, ReportDefinitionDto>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper;
    public GetReportByIdQueryHandler(ApplicationDbContext context, IMapper mapper) { _context = context; _mapper = mapper; }
    public async Task<ReportDefinitionDto> Handle(GetReportByIdQuery request, CancellationToken ct)
    {
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
