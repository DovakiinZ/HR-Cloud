using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

// D1: Folders query (D2/D3 will append tags and user-state queries to this file)
public record GetReportFoldersQuery : IRequest<List<ReportFolderDto>>;

public class GetReportFoldersQueryHandler : IRequestHandler<GetReportFoldersQuery, List<ReportFolderDto>>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public GetReportFoldersQueryHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<List<ReportFolderDto>> Handle(GetReportFoldersQuery q, CancellationToken ct)
        => _mapper.Map<List<ReportFolderDto>>(await _db.ReportFolders.OrderBy(f => f.NameEn).ToListAsync(ct));
}

// D2: Tags query
public record GetReportTagsQuery : IRequest<List<ReportTagDto>>;

public class GetReportTagsQueryHandler : IRequestHandler<GetReportTagsQuery, List<ReportTagDto>>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public GetReportTagsQueryHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<List<ReportTagDto>> Handle(GetReportTagsQuery q, CancellationToken ct)
        => _mapper.Map<List<ReportTagDto>>(await _db.ReportTags.OrderBy(t => t.Name).ToListAsync(ct));
}
