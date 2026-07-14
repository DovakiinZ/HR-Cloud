using System;
using System.Collections.Generic;
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

public record AddReportShareCommand(Guid ReportDefinitionId, Guid? SharedWithUserId, Guid? SharedWithRoleId, Guid? SharedWithDepartmentId, bool CanEdit) : IRequest<ReportShareDto>;
public record RemoveReportShareCommand(Guid ReportDefinitionId, Guid ShareId) : IRequest;

public class AddReportShareCommandHandler : IRequestHandler<AddReportShareCommand, ReportShareDto>
{
    private readonly ApplicationDbContext _db;
    private readonly IMapper _mapper;
    private readonly IReportAccessService _access;

    public AddReportShareCommandHandler(ApplicationDbContext db, IMapper mapper, IReportAccessService access)
    { _db = db; _mapper = mapper; _access = access; }

    public async Task<ReportShareDto> Handle(AddReportShareCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        if (r.SharedWithUserId is null && r.SharedWithRoleId is null && r.SharedWithDepartmentId is null)
            throw new ValidationException(new[] { new ValidationFailure("Target", "A share must target a user, role, or department.") });
        var entity = new ReportShare
        {
            Id = Guid.NewGuid(),
            ReportDefinitionId = r.ReportDefinitionId,
            SharedWithUserId = r.SharedWithUserId,
            SharedWithRoleId = r.SharedWithRoleId,
            SharedWithDepartmentId = r.SharedWithDepartmentId,
            CanEdit = r.CanEdit,
            SharedAt = DateTime.UtcNow,
        };
        _db.ReportShares.Add(entity);
        await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportShareDto>(entity);
    }
}

public class RemoveReportShareCommandHandler : IRequestHandler<RemoveReportShareCommand>
{
    private readonly ApplicationDbContext _db;
    private readonly IReportAccessService _access;

    public RemoveReportShareCommandHandler(ApplicationDbContext db, IReportAccessService access)
    { _db = db; _access = access; }

    public async Task Handle(RemoveReportShareCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        var entity = await _db.ReportShares.FirstOrDefaultAsync(s => s.Id == r.ShareId && s.ReportDefinitionId == r.ReportDefinitionId, ct)
            ?? throw new NotFoundException("ReportShare", r.ShareId);
        _db.ReportShares.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
