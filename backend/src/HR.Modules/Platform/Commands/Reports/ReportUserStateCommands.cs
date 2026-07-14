using System;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public static class ReportUserStateHelper
{
    public static async Task<ReportUserState> GetOrCreateAsync(ApplicationDbContext db, Guid userId, Guid reportId, CancellationToken ct)
    {
        var state = await db.ReportUserStates.FirstOrDefaultAsync(s => s.UserId == userId && s.ReportDefinitionId == reportId, ct);
        if (state is null)
        {
            state = new ReportUserState { Id = Guid.NewGuid(), UserId = userId, ReportDefinitionId = reportId };
            db.ReportUserStates.Add(state);
        }
        return state;
    }
}

public record ToggleReportFavoriteCommand(Guid ReportDefinitionId) : IRequest<bool>;
public record ToggleReportPinCommand(Guid ReportDefinitionId) : IRequest<bool>;

public class ToggleReportFavoriteCommandHandler : IRequestHandler<ToggleReportFavoriteCommand, bool>
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IReportAccessService _access;

    public ToggleReportFavoriteCommandHandler(ApplicationDbContext db, ICurrentUserService user, IReportAccessService access)
    { _db = db; _user = user; _access = access; }

    public async Task<bool> Handle(ToggleReportFavoriteCommand r, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(r.ReportDefinitionId, ct);
        var state = await ReportUserStateHelper.GetOrCreateAsync(_db, _user.UserId, r.ReportDefinitionId, ct);
        state.IsFavorite = !state.IsFavorite;
        await _db.SaveChangesAsync(ct);
        return state.IsFavorite;
    }
}

public class ToggleReportPinCommandHandler : IRequestHandler<ToggleReportPinCommand, bool>
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IReportAccessService _access;

    public ToggleReportPinCommandHandler(ApplicationDbContext db, ICurrentUserService user, IReportAccessService access)
    { _db = db; _user = user; _access = access; }

    public async Task<bool> Handle(ToggleReportPinCommand r, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(r.ReportDefinitionId, ct);
        var state = await ReportUserStateHelper.GetOrCreateAsync(_db, _user.UserId, r.ReportDefinitionId, ct);
        state.IsPinned = !state.IsPinned;
        await _db.SaveChangesAsync(ct);
        return state.IsPinned;
    }
}
