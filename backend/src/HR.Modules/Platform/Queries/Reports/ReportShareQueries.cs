using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record GetReportSharesQuery(Guid ReportDefinitionId) : IRequest<List<ReportShareDto>>;

public class GetReportSharesQueryHandler : IRequestHandler<GetReportSharesQuery, List<ReportShareDto>>
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly IReportAccessService _access;

    public GetReportSharesQueryHandler(ApplicationDbContext db, IMapper mapper, IReportAccessService access)
    { _db = db; _mapper = mapper; _access = access; }

    public async Task<List<ReportShareDto>> Handle(GetReportSharesQuery q, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(q.ReportDefinitionId, ct);
        var shares = await _db.ReportShares
            .Where(s => s.ReportDefinitionId == q.ReportDefinitionId)
            .ToListAsync(ct);
        return _mapper.Map<List<ReportShareDto>>(shares);
    }
}
