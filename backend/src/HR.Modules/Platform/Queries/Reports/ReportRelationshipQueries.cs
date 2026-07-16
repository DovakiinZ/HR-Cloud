using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record GetReportRelationshipsQuery(Guid ReportDefinitionId) : IRequest<List<ReportRelationshipDto>>;

public class GetReportRelationshipsQueryHandler : IRequestHandler<GetReportRelationshipsQuery, List<ReportRelationshipDto>>
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly IReportAccessService _access;

    public GetReportRelationshipsQueryHandler(ApplicationDbContext db, IMapper mapper, IReportAccessService access)
    { _db = db; _mapper = mapper; _access = access; }

    public async Task<List<ReportRelationshipDto>> Handle(GetReportRelationshipsQuery q, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(q.ReportDefinitionId, ct);
        var rels = await _db.Set<ReportRelationship>().AsNoTracking()
            .Where(x => x.ReportDefinitionId == q.ReportDefinitionId)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        return _mapper.Map<List<ReportRelationshipDto>>(rels);
    }
}
