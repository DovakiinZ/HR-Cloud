using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public record CreateReportFolderCommand(string NameEn, string NameAr, Guid? ParentFolderId) : IRequest<ReportFolderDto>;
public record UpdateReportFolderCommand(Guid Id, string NameEn, string NameAr, Guid? ParentFolderId) : IRequest<ReportFolderDto>;
public record DeleteReportFolderCommand(Guid Id) : IRequest;

public class CreateReportFolderCommandHandler : IRequestHandler<CreateReportFolderCommand, ReportFolderDto>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public CreateReportFolderCommandHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<ReportFolderDto> Handle(CreateReportFolderCommand r, CancellationToken ct)
    {
        var e = new ReportFolder { Id = Guid.NewGuid(), NameEn = r.NameEn, NameAr = r.NameAr, ParentFolderId = r.ParentFolderId };
        _db.ReportFolders.Add(e); await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportFolderDto>(e);
    }
}

public class UpdateReportFolderCommandHandler : IRequestHandler<UpdateReportFolderCommand, ReportFolderDto>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public UpdateReportFolderCommandHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<ReportFolderDto> Handle(UpdateReportFolderCommand r, CancellationToken ct)
    {
        var e = await _db.ReportFolders.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("ReportFolder", r.Id);
        e.NameEn = r.NameEn; e.NameAr = r.NameAr; e.ParentFolderId = r.ParentFolderId;
        await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportFolderDto>(e);
    }
}

public class DeleteReportFolderCommandHandler : IRequestHandler<DeleteReportFolderCommand>
{
    private readonly ApplicationDbContext _db;
    public DeleteReportFolderCommandHandler(ApplicationDbContext db) { _db = db; }
    public async Task Handle(DeleteReportFolderCommand r, CancellationToken ct)
    {
        var e = await _db.ReportFolders.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("ReportFolder", r.Id);
        // Unfile any reports currently in this folder before deleting it.
        var reports = await _db.Set<ReportDefinition>().Where(rd => rd.FolderId == r.Id).ToListAsync(ct);
        foreach (var rd in reports) rd.FolderId = null;
        _db.ReportFolders.Remove(e);
        await _db.SaveChangesAsync(ct);
    }
}
