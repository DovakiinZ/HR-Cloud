using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public record AddReportRelationshipCommand : IRequest<ReportRelationshipDto>
{
    public Guid ReportDefinitionId { get; init; }
    public Guid SourceObjectId { get; init; }
    public Guid TargetObjectId { get; init; }
    public string JoinField { get; init; } = null!;
    public string JoinType { get; init; } = "Inner";
    public int SortOrder { get; init; }
}

public record DeleteReportRelationshipCommand(Guid Id) : IRequest;

public class AddReportRelationshipCommandHandler : IRequestHandler<AddReportRelationshipCommand, ReportRelationshipDto>
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly IReportAccessService _access;

    public AddReportRelationshipCommandHandler(ApplicationDbContext db, IMapper mapper, IReportAccessService access)
    { _db = db; _mapper = mapper; _access = access; }

    public async Task<ReportRelationshipDto> Handle(AddReportRelationshipCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);

        var report = await _db.Set<ReportDefinition>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == r.ReportDefinitionId, ct)
            ?? throw new NotFoundException("ReportDefinition", r.ReportDefinitionId);

        var existing = await _db.Set<ReportRelationship>().AsNoTracking()
            .Where(x => x.ReportDefinitionId == r.ReportDefinitionId)
            .Select(x => new { x.TargetObjectId, x.SourceObjectId, x.SortOrder })
            .ToListAsync(ct);

        var error = ReportRelationshipRules.Validate(
            report.PrimaryObjectId,
            existing.Select(e => new ReportRelationshipRules.Existing(e.TargetObjectId, e.SourceObjectId, e.SortOrder)).ToList(),
            r.SourceObjectId, r.TargetObjectId, r.JoinType, r.SortOrder);
        if (error is not null)
            throw new ValidationException(new[] { new ValidationFailure("relationship", error) });

        var entity = new ReportRelationship
        {
            ReportDefinitionId = r.ReportDefinitionId,
            SourceObjectId = r.SourceObjectId,
            TargetObjectId = r.TargetObjectId,
            JoinField = r.JoinField,
            JoinType = r.JoinType,
            SortOrder = r.SortOrder,
        };
        _db.Set<ReportRelationship>().Add(entity);
        await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportRelationshipDto>(entity);
    }
}

public class DeleteReportRelationshipCommandHandler : IRequestHandler<DeleteReportRelationshipCommand>
{
    private readonly ApplicationDbContext _db;
    private readonly IReportAccessService _access;

    public DeleteReportRelationshipCommandHandler(ApplicationDbContext db, IReportAccessService access)
    { _db = db; _access = access; }

    public async Task Handle(DeleteReportRelationshipCommand r, CancellationToken ct)
    {
        var entity = await _db.Set<ReportRelationship>().FindAsync(new object[] { r.Id }, ct)
            ?? throw new NotFoundException("ReportRelationship", r.Id);
        await _access.EnsureCanEditAsync(entity.ReportDefinitionId, ct);
        _db.Set<ReportRelationship>().Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
