using System;
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

public record CreateReportTagCommand(string Name, string? Color) : IRequest<ReportTagDto>;
public record DeleteReportTagCommand(Guid Id) : IRequest;
public record AssignReportTagCommand(Guid ReportDefinitionId, Guid ReportTagId) : IRequest;
public record UnassignReportTagCommand(Guid ReportDefinitionId, Guid ReportTagId) : IRequest;

public class CreateReportTagCommandHandler : IRequestHandler<CreateReportTagCommand, ReportTagDto>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public CreateReportTagCommandHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<ReportTagDto> Handle(CreateReportTagCommand r, CancellationToken ct)
    {
        var dup = await _db.ReportTags.AnyAsync(t => t.Name == r.Name, ct);
        if (dup) throw new ValidationException(new[] { new ValidationFailure("Name", $"A tag named '{r.Name}' already exists.") });
        var e = new ReportTag { Id = Guid.NewGuid(), Name = r.Name, Color = r.Color };
        _db.ReportTags.Add(e); await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportTagDto>(e);
    }
}

public class DeleteReportTagCommandHandler : IRequestHandler<DeleteReportTagCommand>
{
    private readonly ApplicationDbContext _db;
    public DeleteReportTagCommandHandler(ApplicationDbContext db) { _db = db; }
    public async Task Handle(DeleteReportTagCommand r, CancellationToken ct)
    {
        var e = await _db.ReportTags.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("ReportTag", r.Id);
        var links = _db.ReportDefinitionTags.Where(l => l.ReportTagId == r.Id);
        _db.ReportDefinitionTags.RemoveRange(links);
        _db.ReportTags.Remove(e);
        await _db.SaveChangesAsync(ct);
    }
}

public class AssignReportTagCommandHandler : IRequestHandler<AssignReportTagCommand>
{
    private readonly ApplicationDbContext _db; private readonly IReportAccessService _access;
    public AssignReportTagCommandHandler(ApplicationDbContext db, IReportAccessService access) { _db = db; _access = access; }
    public async Task Handle(AssignReportTagCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        var tagExists = await _db.ReportTags.AnyAsync(t => t.Id == r.ReportTagId, ct);
        if (!tagExists) throw new NotFoundException("ReportTag", r.ReportTagId);
        var exists = await _db.ReportDefinitionTags.AnyAsync(l => l.ReportDefinitionId == r.ReportDefinitionId && l.ReportTagId == r.ReportTagId, ct);
        if (!exists)
        {
            _db.ReportDefinitionTags.Add(new ReportDefinitionTag { Id = Guid.NewGuid(), ReportDefinitionId = r.ReportDefinitionId, ReportTagId = r.ReportTagId });
            await _db.SaveChangesAsync(ct);
        }
    }
}

public class UnassignReportTagCommandHandler : IRequestHandler<UnassignReportTagCommand>
{
    private readonly ApplicationDbContext _db; private readonly IReportAccessService _access;
    public UnassignReportTagCommandHandler(ApplicationDbContext db, IReportAccessService access) { _db = db; _access = access; }
    public async Task Handle(UnassignReportTagCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        var link = await _db.ReportDefinitionTags.FirstOrDefaultAsync(l => l.ReportDefinitionId == r.ReportDefinitionId && l.ReportTagId == r.ReportTagId, ct);
        if (link is not null) { _db.ReportDefinitionTags.Remove(link); await _db.SaveChangesAsync(ct); }
    }
}
