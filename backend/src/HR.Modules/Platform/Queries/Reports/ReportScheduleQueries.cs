using AutoMapper;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record GetReportSchedulesQuery(Guid ReportDefinitionId) : IRequest<List<ReportScheduleDto>>;

public class GetReportSchedulesQueryHandler : IRequestHandler<GetReportSchedulesQuery, List<ReportScheduleDto>>
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;

    public GetReportSchedulesQueryHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }

    public async Task<List<ReportScheduleDto>> Handle(GetReportSchedulesQuery request, CancellationToken ct)
    {
        var schedules = await _db.ReportSchedules
            .Where(s => s.ReportDefinitionId == request.ReportDefinitionId)
            .OrderByDescending(s => s.Id)
            .ToListAsync(ct);
        return _mapper.Map<List<ReportScheduleDto>>(schedules);
    }
}
